using ApimPolicyVisualizer.Api.Azure;
using ApimPolicyVisualizer.Api.Policy.Model;
using ApimPolicyVisualizer.Api.Policy.Source;
using ApimPolicyVisualizer.Api.Scopes;
using Microsoft.AspNetCore.Mvc;

namespace ApimPolicyVisualizer.Api.Policy;

/// <summary>
/// Resolves a scope from the scope catalog, retrieves its effective policy XML and returns the schemaVersion 2
/// execution model (SPF-FR-12, POL-FR-04). Neither the XML nor the model is persisted beyond the request
/// (VIS-SEC-02), and only the scope ID is ever logged — never policy content (SPF-SEC-01).
/// </summary>
[ApiController]
[Route("api/policy")]
public sealed class PolicyFlowController(
    IScopeDiscoveryService scopeDiscoveryService,
    IEffectivePolicyClient effectivePolicyClient,
    IPolicySourceLoader sourceLoader,
    IFragmentRegionBuilder fragmentRegionBuilder,
    IPolicyFlowModelBuilder modelBuilder,
    ILogger<PolicyFlowController> logger) : ControllerBase
{
    [HttpGet("effective-flow")]
    [ProducesResponseType<EffectivePolicyFlowModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetEffectiveFlow([FromQuery] string? scope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return Problem(
                title: "Missing scope",
                detail: "The 'scope' query parameter is required, e.g. 'global', 'products/{product}', 'apis/{api}' or 'apis/{api}/operations/{operation}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var catalogResult = await scopeDiscoveryService.GetScopesAsync(cancellationToken);
        if (catalogResult.Failure is { } catalogFailure)
        {
            return FailureResult(catalogFailure, "APIM service not found", "Access to APIM service denied");
        }

        var catalog = catalogResult.Value!;
        var descriptor = catalog.FindScope(scope);
        if (descriptor is null)
        {
            return Problem(
                title: "Unknown scope",
                detail: $"Scope '{scope}' is not a known scope of APIM service '{catalog.Service.ServiceName}'. Use GET /api/scopes to list valid scope IDs.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var xmlResult = await effectivePolicyClient.GetEffectivePolicyXmlAsync(catalog.Service, descriptor, cancellationToken);
        if (xmlResult.Failure is { } xmlFailure)
        {
            return FailureResult(xmlFailure, "Effective policy not found", "Access to effective policy denied");
        }

        EffectivePolicyFlowModel model;
        try
        {
            var document = sourceLoader.Load(xmlResult.Value!);
            var regions = fragmentRegionBuilder.Build(document);
            model = modelBuilder.Build(document, regions, descriptor.ScopeId, descriptor.Kind);
        }
        catch (PolicyParseException ex)
        {
            logger.LogWarning("Effective policy for scope {ScopeId} could not be parsed.", descriptor.ScopeId);
            return Problem(
                title: "Policy parse failure",
                detail: $"The effective policy for scope '{descriptor.ScopeId}' could not be parsed: {ex.Message}",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        logger.LogInformation(
            "Built effective policy flow model for scope {ScopeId} ({ElementCount} elements, {EdgeCount} edges).",
            descriptor.ScopeId, model.Elements.Count, model.Edges.Count);
        return Ok(model);
    }

    private ObjectResult FailureResult(ArmFailure failure, string notFoundTitle, string forbiddenTitle) => failure switch
    {
        ArmNotFound notFound => Problem(title: notFoundTitle, detail: notFound.Message, statusCode: StatusCodes.Status404NotFound),
        ArmForbidden forbidden => Problem(title: forbiddenTitle, detail: forbidden.Message, statusCode: StatusCodes.Status403Forbidden),
        var other => Problem(detail: other.Message, statusCode: StatusCodes.Status500InternalServerError),
    };
}
