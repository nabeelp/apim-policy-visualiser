using Azure.ResourceManager;
using Azure.ResourceManager.ApiManagement;

namespace ApimPolicyVisualizer.Api.Scopes;

/// <summary>
/// <see cref="IApimCatalogClient"/> backed by Azure.ResourceManager.ApiManagement. The <see cref="ArmClient"/>
/// is authenticated through <c>ArmTokenProviderCredential</c>, so all calls share the cached ARM token.
/// </summary>
public sealed class ArmApimCatalogClient(ArmClient armClient) : IApimCatalogClient
{
    public async Task EnsureServiceExistsAsync(ApimServiceLocation service, CancellationToken cancellationToken)
    {
        await GetService(service).GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApimProductInfo>> ListProductsAsync(ApimServiceLocation service, CancellationToken cancellationToken)
    {
        var results = new List<ApimProductInfo>();
        await foreach (var product in GetService(service).GetApiManagementProducts().GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ApimProductInfo(product.Data.Name, product.Data.DisplayName ?? product.Data.Name));
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> ListProductApiNamesAsync(ApimServiceLocation service, string productName, CancellationToken cancellationToken)
    {
        var productId = ApiManagementProductResource.CreateResourceIdentifier(
            service.SubscriptionId, service.ResourceGroupName, service.ServiceName, productName);
        var product = armClient.GetApiManagementProductResource(productId);

        var results = new List<string>();
        await foreach (var api in product.GetProductApisAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (api.IsCurrent != false)
            {
                results.Add(api.Name);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ApimApiInfo>> ListApisAsync(ApimServiceLocation service, CancellationToken cancellationToken)
    {
        var results = new List<ApimApiInfo>();
        await foreach (var api in GetService(service).GetApis().GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (api.Data.IsCurrent != false)
            {
                results.Add(new ApimApiInfo(api.Data.Name, api.Data.DisplayName ?? api.Data.Name, api.Data.Path));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ApimOperationInfo>> ListOperationsAsync(ApimServiceLocation service, string apiName, CancellationToken cancellationToken)
    {
        var apiId = ApiResource.CreateResourceIdentifier(service.SubscriptionId, service.ResourceGroupName, service.ServiceName, apiName);
        var api = armClient.GetApiResource(apiId);

        var results = new List<ApimOperationInfo>();
        await foreach (var operation in api.GetApiOperations().GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ApimOperationInfo(
                operation.Data.Name,
                operation.Data.DisplayName ?? operation.Data.Name,
                operation.Data.Method,
                operation.Data.UriTemplate));
        }

        return results;
    }

    private ApiManagementServiceResource GetService(ApimServiceLocation service) =>
        armClient.GetApiManagementServiceResource(
            ApiManagementServiceResource.CreateResourceIdentifier(service.SubscriptionId, service.ResourceGroupName, service.ServiceName));
}
