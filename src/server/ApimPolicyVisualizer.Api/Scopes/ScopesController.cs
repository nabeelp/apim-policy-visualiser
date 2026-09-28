using ApimPolicyVisualizer.Api.Azure;
using Microsoft.AspNetCore.Mvc;

namespace ApimPolicyVisualizer.Api.Scopes;

[ApiController]
[Route("api/scopes")]
public sealed class ScopesController(IScopeDiscoveryService scopeDiscoveryService) : ControllerBase
{
    /// <summary>Returns the Global scope plus the product/API/operation hierarchy of the configured APIM service.</summary>
    [HttpGet]
    [ProducesResponseType<ScopeCatalog>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetScopes(CancellationToken cancellationToken)
    {
        var result = await scopeDiscoveryService.GetScopesAsync(cancellationToken);

        return result.Failure switch
        {
            null => Ok(result.Value),
            ArmNotFound notFound => Problem(
                title: "APIM service not found",
                detail: notFound.Message,
                statusCode: StatusCodes.Status404NotFound),
            ArmForbidden forbidden => Problem(
                title: "Access to APIM service denied",
                detail: forbidden.Message,
                statusCode: StatusCodes.Status403Forbidden),
            var other => Problem(detail: other.Message, statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
