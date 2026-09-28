using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ApimPolicyVisualizer.Api.Azure;
using ApimPolicyVisualizer.Api.Scopes;

namespace ApimPolicyVisualizer.Api.Policy;

public interface IEffectivePolicyClient
{
    /// <summary>
    /// Retrieves the effective (inherited, merged) policy XML for <paramref name="scope"/> on <paramref name="service"/>.
    /// ARM 404/403 are returned as typed <see cref="ArmNotFound"/>/<see cref="ArmForbidden"/> failures; other
    /// non-success statuses throw <see cref="HttpRequestException"/>.
    /// </summary>
    Task<ArmResult<string>> GetEffectivePolicyXmlAsync(
        ApimServiceLocation service,
        ScopeDescriptor scope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls the ARM "get policy" operation with <c>format=xml&amp;effective=true</c> and a pinned api-version.
/// <c>format=xml</c> is used instead of <c>rawxml</c> because rawxml leaves policy expressions unescaped
/// (e.g. <c>condition="@(... "x" ...)"</c>), which is not well-formed XML.
/// The XML is returned to the caller only; it is never persisted or cached (VIS-SEC-02).
/// </summary>
public sealed class EffectivePolicyClient(
    IHttpClientFactory httpClientFactory,
    IArmTokenProvider tokenProvider,
    ILogger<EffectivePolicyClient> logger) : IEffectivePolicyClient
{
    public const string HttpClientName = "ArmEffectivePolicy";

    // Pinned deliberately: ARM's default api-version can change and silently alter the response shape.
    public const string ApiVersion = "2024-05-01";

    public static readonly Uri ArmBaseUri = new("https://management.azure.com");

    public async Task<ArmResult<string>> GetEffectivePolicyXmlAsync(
        ApimServiceLocation service,
        ScopeDescriptor scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(scope);

        var requestUri = BuildRequestUri(service, scope);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                logger.LogWarning("Effective policy for scope {ScopeId} on {ServiceName} was not found (404).", scope.ScopeId, service.ServiceName);
                return new ArmNotFound(
                    $"Effective policy for {DescribeScope(scope)} was not found on APIM service '{service.ServiceName}'. Verify the scope still exists.");
            case HttpStatusCode.Forbidden:
                logger.LogWarning("Access to effective policy for scope {ScopeId} on {ServiceName} was denied (403).", scope.ScopeId, service.ServiceName);
                return new ArmForbidden(
                    $"Access to the effective policy for {DescribeScope(scope)} on APIM service '{service.ServiceName}' was denied. Verify the caller's role assignment.");
        }

        if (!response.IsSuccessStatusCode)
        {
            // The raw ARM error body is deliberately not surfaced (it may contain internal identifiers).
            throw new HttpRequestException(
                $"ARM effective policy request for scope '{scope.ScopeId}' failed with HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ArmResult<string>.Success(ExtractPolicyXml(body, response.Content.Headers.ContentType?.MediaType));
    }

    /// <summary>Builds the ARM policy URL for the scope kind, always including the pinned api-version.</summary>
    public static Uri BuildRequestUri(ApimServiceLocation service, ScopeDescriptor scope)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(scope);

        var serviceId =
            $"/subscriptions/{Segment(service.SubscriptionId)}/resourceGroups/{Segment(service.ResourceGroupName)}" +
            $"/providers/Microsoft.ApiManagement/service/{Segment(service.ServiceName)}";

        var scopePath = scope.Kind switch
        {
            ScopeKinds.Global => string.Empty,
            ScopeKinds.Product => $"/products/{Segment(Required(scope.ProductName, nameof(scope.ProductName), scope))}",
            ScopeKinds.Api => $"/apis/{Segment(Required(scope.ApiName, nameof(scope.ApiName), scope))}",
            ScopeKinds.Operation =>
                $"/apis/{Segment(Required(scope.ApiName, nameof(scope.ApiName), scope))}" +
                $"/operations/{Segment(Required(scope.OperationName, nameof(scope.OperationName), scope))}",
            _ => throw new ArgumentException($"Unsupported scope kind '{scope.Kind}'.", nameof(scope)),
        };

        return new Uri(
            ArmBaseUri,
            $"{serviceId}{scopePath}/policies/policy?format=xml&effective=true&api-version={ApiVersion}");
    }

    private static string DescribeScope(ScopeDescriptor scope) => scope.Kind switch
    {
        ScopeKinds.Global => "the Global scope",
        ScopeKinds.Product => $"product '{scope.ProductName}' (scope '{scope.ScopeId}')",
        ScopeKinds.Api => $"API '{scope.ApiName}' (scope '{scope.ScopeId}')",
        ScopeKinds.Operation => $"operation '{scope.OperationName}' of API '{scope.ApiName}' (scope '{scope.ScopeId}')",
        _ => $"scope '{scope.ScopeId}'",
    };

    // ARM returns a PolicyContract JSON envelope ({"properties":{"format":"xml","value":"<policies>..."}});
    // a bare XML body is accepted as well.
    private static string ExtractPolicyXml(string body, string? mediaType)
    {
        if (body.TrimStart().StartsWith('<') || (mediaType?.Contains("xml", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return body;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }
        }
        catch (JsonException)
        {
        }

        throw new InvalidOperationException("ARM effective policy response did not contain a policy XML value.");
    }

    private static string Required(string? value, string name, ScopeDescriptor scope) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Scope '{scope.ScopeId}' of kind '{scope.Kind}' is missing {name}.", nameof(scope))
            : value;

    private static string Segment(string value) => Uri.EscapeDataString(value);
}
