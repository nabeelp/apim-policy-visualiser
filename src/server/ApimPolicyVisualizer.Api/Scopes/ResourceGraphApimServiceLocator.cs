using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApimPolicyVisualizer.Api.Azure;
using Azure;

namespace ApimPolicyVisualizer.Api.Scopes;

/// <summary>
/// Locates APIM services by name with an Azure Resource Graph query across every subscription visible to
/// the current credential (or only the configured subscription when one is set).
/// </summary>
public sealed class ResourceGraphApimServiceLocator(IHttpClientFactory httpClientFactory, IArmTokenProvider tokenProvider) : IApimServiceLocator
{
    public const string HttpClientName = "AzureResourceGraph";
    public const string ApiVersion = "2022-10-01";
    public static readonly Uri QueryUri =
        new($"https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version={ApiVersion}");

    public async Task<IReadOnlyList<ApimServiceLocation>> FindByNameAsync(string serviceName, string? subscriptionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var query =
            "resources" +
            " | where type =~ 'microsoft.apimanagement/service'" +
            $" and name =~ '{EscapeKqlString(serviceName)}'" +
            " | project subscriptionId, resourceGroup, name";

        var body = new ResourceGraphRequest(query, subscriptionId is null ? null : [subscriptionId]);

        using var request = new HttpRequestMessage(HttpMethod.Post, QueryUri)
        {
            Content = JsonContent.Create(body, options: SerializerOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false));

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // The raw ARM error body is deliberately not surfaced (it may contain internal identifiers).
            throw new RequestFailedException((int)response.StatusCode, $"Azure Resource Graph query failed with HTTP {(int)response.StatusCode}.");
        }

        var result = await response.Content
            .ReadFromJsonAsync<ResourceGraphResponse>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return (result?.Data ?? [])
            .Where(row => !string.IsNullOrWhiteSpace(row.SubscriptionId)
                && !string.IsNullOrWhiteSpace(row.ResourceGroup)
                && !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new ApimServiceLocation(row.SubscriptionId!, row.ResourceGroup!, row.Name!))
            .ToList();
    }

    private static string EscapeKqlString(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record ResourceGraphRequest(string Query, IReadOnlyList<string>? Subscriptions);

    private sealed record ResourceGraphResponse(IReadOnlyList<ResourceGraphRow>? Data);

    private sealed record ResourceGraphRow(string? SubscriptionId, string? ResourceGroup, string? Name);
}
