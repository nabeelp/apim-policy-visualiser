using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ApimPolicyVisualizer.Api.Azure;
using ApimPolicyVisualizer.Api.Configuration;
using Azure;
using Microsoft.Extensions.Options;

namespace ApimPolicyVisualizer.Api.Scopes;

/// <summary>Fully-resolved coordinates of the target APIM service (identifiers only, never credentials).</summary>
public sealed record ApimServiceLocation(string SubscriptionId, string ResourceGroupName, string ServiceName)
{
    public string ResourceId =>
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroupName}/providers/Microsoft.ApiManagement/service/{ServiceName}";
}

public static class ScopeKinds
{
    public const string Global = "Global";
    public const string Product = "Product";
    public const string Api = "Api";
    public const string Operation = "Operation";
}

public sealed record GlobalScope
{
    public const string GlobalScopeId = "global";

    public string ScopeId { get; init; } = GlobalScopeId;

    public string Kind { get; init; } = ScopeKinds.Global;

    public string DisplayName { get; init; } = "Global";
}

public sealed record OperationScope(string ScopeId, string Name, string DisplayName, string? Method, string? UrlTemplate)
{
    public string Kind => ScopeKinds.Operation;
}

public sealed record ApiScope(string ScopeId, string Name, string DisplayName, string? Path, IReadOnlyList<OperationScope> Operations)
{
    public string Kind => ScopeKinds.Api;
}

public sealed record ProductScope(string ScopeId, string Name, string DisplayName, IReadOnlyList<ApiScope> Apis)
{
    public string Kind => ScopeKinds.Product;
}

/// <summary>A single selectable scope with the resource names needed to address its policy.</summary>
public sealed record ScopeDescriptor(string ScopeId, string Kind, string? ProductName = null, string? ApiName = null, string? OperationName = null);

/// <summary>
/// The response of <c>GET /api/scopes</c>: the Global scope plus the product → API → operation hierarchy.
/// Scope IDs are ARM paths relative to the APIM service (<c>global</c>, <c>products/{p}</c>, <c>apis/{a}</c>,
/// <c>apis/{a}/operations/{o}</c>); an API listed under several products shares one scope ID.
/// </summary>
public sealed record ScopeCatalog(
    ApimServiceLocation Service,
    GlobalScope Global,
    IReadOnlyList<ProductScope> Products,
    IReadOnlyList<ApiScope> Apis)
{
    public ScopeDescriptor? FindScope(string? scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            return null;
        }

        if (string.Equals(scopeId, Global.ScopeId, StringComparison.OrdinalIgnoreCase))
        {
            return new ScopeDescriptor(Global.ScopeId, ScopeKinds.Global);
        }

        foreach (var product in Products)
        {
            if (string.Equals(product.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase))
            {
                return new ScopeDescriptor(product.ScopeId, ScopeKinds.Product, ProductName: product.Name);
            }
        }

        foreach (var api in Apis.Concat(Products.SelectMany(p => p.Apis)))
        {
            if (string.Equals(api.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase))
            {
                return new ScopeDescriptor(api.ScopeId, ScopeKinds.Api, ApiName: api.Name);
            }

            foreach (var operation in api.Operations)
            {
                if (string.Equals(operation.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase))
                {
                    return new ScopeDescriptor(operation.ScopeId, ScopeKinds.Operation, ApiName: api.Name, OperationName: operation.Name);
                }
            }
        }

        return null;
    }
}

public sealed record ApimProductInfo(string Name, string DisplayName);

public sealed record ApimApiInfo(string Name, string DisplayName, string? Path);

public sealed record ApimOperationInfo(string Name, string DisplayName, string? Method, string? UrlTemplate);

/// <summary>
/// Reads the APIM management-plane hierarchy. Implementations throw <see cref="RequestFailedException"/>
/// for ARM failures (status 404/403 are mapped by <see cref="ScopeDiscoveryService"/>).
/// </summary>
public interface IApimCatalogClient
{
    Task EnsureServiceExistsAsync(ApimServiceLocation service, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApimProductInfo>> ListProductsAsync(ApimServiceLocation service, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListProductApiNamesAsync(ApimServiceLocation service, string productName, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApimApiInfo>> ListApisAsync(ApimServiceLocation service, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApimOperationInfo>> ListOperationsAsync(ApimServiceLocation service, string apiName, CancellationToken cancellationToken);
}

/// <summary>
/// Finds APIM services by name across the subscriptions visible to the current credential (Azure Resource Graph).
/// Throws <see cref="RequestFailedException"/> on ARM failures.
/// </summary>
public interface IApimServiceLocator
{
    Task<IReadOnlyList<ApimServiceLocation>> FindByNameAsync(string serviceName, string? subscriptionId, CancellationToken cancellationToken);
}

public interface IScopeDiscoveryService
{
    Task<ArmResult<ApimServiceLocation>> ResolveServiceAsync(CancellationToken cancellationToken = default);

    Task<ArmResult<ScopeCatalog>> GetScopesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the configured APIM service (explicit configuration first, Azure Resource Graph otherwise)
/// and builds the Global/Product/API/Operation scope catalog, mapping ARM 404/403 to typed failures.
/// </summary>
public sealed partial class ScopeDiscoveryService : IScopeDiscoveryService
{
    private const string ServiceResourceKind = "APIM service";
    private const int MaxParallelRequests = 8;

    private readonly IOptions<ApimOptions> _options;
    private readonly IApimServiceLocator _locator;
    private readonly IApimCatalogClient _catalogClient;
    private readonly ILogger<ScopeDiscoveryService> _logger;
    private readonly SemaphoreSlim _resolveLock = new(1, 1);
    private ApimServiceLocation? _resolvedLocation;

    public ScopeDiscoveryService(
        IOptions<ApimOptions> options,
        IApimServiceLocator locator,
        IApimCatalogClient catalogClient,
        ILogger<ScopeDiscoveryService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _catalogClient = catalogClient ?? throw new ArgumentNullException(nameof(catalogClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ArmResult<ApimServiceLocation>> ResolveServiceAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var serviceName = string.IsNullOrWhiteSpace(options.ServiceName) ? ApimOptions.DefaultServiceName : options.ServiceName.Trim();
        var subscriptionId = string.IsNullOrWhiteSpace(options.SubscriptionId) ? null : options.SubscriptionId.Trim();
        var resourceGroupName = string.IsNullOrWhiteSpace(options.ResourceGroupName) ? null : options.ResourceGroupName.Trim();

        if (!ServiceNamePattern().IsMatch(serviceName))
        {
            return new ArmNotFound(
                $"{ServiceResourceKind} '{serviceName}' was not found. The configured name is not a valid APIM service name.");
        }

        // Explicit configuration always wins and skips Resource Graph entirely.
        if (subscriptionId is not null && resourceGroupName is not null)
        {
            return ArmResult<ApimServiceLocation>.Success(new ApimServiceLocation(subscriptionId, resourceGroupName, serviceName));
        }

        if (_resolvedLocation is { } cached)
        {
            return ArmResult<ApimServiceLocation>.Success(cached);
        }

        await _resolveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resolvedLocation is { } cachedAfterLock)
            {
                return ArmResult<ApimServiceLocation>.Success(cachedAfterLock);
            }

            IReadOnlyList<ApimServiceLocation> matches;
            try
            {
                matches = await _locator.FindByNameAsync(serviceName, subscriptionId, cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status403Forbidden)
            {
                _logger.LogWarning("Azure Resource Graph lookup for APIM service {ServiceName} was denied (403).", serviceName);
                return ArmFailureMessages.Forbidden($"Azure Resource Graph query for {ServiceResourceKind} '{serviceName}'");
            }
            catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
            {
                return NotFoundViaResourceGraph(serviceName, subscriptionId, resourceGroupName);
            }

            var candidates = matches
                .Where(m => string.Equals(m.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                .Where(m => subscriptionId is null || string.Equals(m.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase))
                .Where(m => resourceGroupName is null || string.Equals(m.ResourceGroupName, resourceGroupName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.SubscriptionId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.ResourceGroupName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
            {
                return NotFoundViaResourceGraph(serviceName, subscriptionId, resourceGroupName);
            }

            if (candidates.Count > 1)
            {
                _logger.LogWarning(
                    "Found {Count} APIM services named {ServiceName}; using the one in subscription {SubscriptionId}, resource group {ResourceGroupName}. Configure Apim:SubscriptionId and Apim:ResourceGroupName to choose explicitly.",
                    candidates.Count, serviceName, candidates[0].SubscriptionId, candidates[0].ResourceGroupName);
            }

            var resolved = candidates[0] with { ServiceName = serviceName };
            _resolvedLocation = resolved;
            _logger.LogInformation(
                "Resolved APIM service {ServiceName} to subscription {SubscriptionId}, resource group {ResourceGroupName} via Azure Resource Graph.",
                resolved.ServiceName, resolved.SubscriptionId, resolved.ResourceGroupName);
            return ArmResult<ApimServiceLocation>.Success(resolved);
        }
        finally
        {
            _resolveLock.Release();
        }
    }

    public async Task<ArmResult<ScopeCatalog>> GetScopesAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveServiceAsync(cancellationToken).ConfigureAwait(false);
        if (resolution.Failure is { } resolutionFailure)
        {
            return resolutionFailure;
        }

        var service = resolution.Value!;
        try
        {
            await _catalogClient.EnsureServiceExistsAsync(service, cancellationToken).ConfigureAwait(false);
            var catalog = await BuildCatalogAsync(service, cancellationToken).ConfigureAwait(false);
            return ArmResult<ScopeCatalog>.Success(catalog);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            _logger.LogWarning("APIM service {ResourceId} or one of its child resources was not found (404).", service.ResourceId);
            return new ArmNotFound(
                $"{ServiceResourceKind} '{service.ServiceName}' was not found in subscription '{service.SubscriptionId}', resource group '{service.ResourceGroupName}'.");
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status403Forbidden)
        {
            _logger.LogWarning("Access to APIM service {ResourceId} was denied (403).", service.ResourceId);
            return ArmFailureMessages.Forbidden(service.ResourceId);
        }
    }

    private async Task<ScopeCatalog> BuildCatalogAsync(ApimServiceLocation service, CancellationToken cancellationToken)
    {
        var productsTask = _catalogClient.ListProductsAsync(service, cancellationToken);
        var apisTask = _catalogClient.ListApisAsync(service, cancellationToken);
        await Task.WhenAll(productsTask, apisTask).ConfigureAwait(false);

        var products = productsTask.Result.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        var apis = apisTask.Result;

        using var throttle = new SemaphoreSlim(MaxParallelRequests);

        var productApiNames = new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        await Task.WhenAll(products.Select(product => ThrottledAsync(throttle, async () =>
        {
            productApiNames[product.Name] = await _catalogClient
                .ListProductApiNamesAsync(service, product.Name, cancellationToken)
                .ConfigureAwait(false);
        }))).ConfigureAwait(false);

        var apiInfos = apis.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in productApiNames.Values.SelectMany(n => n))
        {
            apiInfos.TryAdd(name, new ApimApiInfo(name, name, null));
        }

        var operationsByApi = new ConcurrentDictionary<string, IReadOnlyList<ApimOperationInfo>>(StringComparer.OrdinalIgnoreCase);
        await Task.WhenAll(apiInfos.Keys.Select(apiName => ThrottledAsync(throttle, async () =>
        {
            operationsByApi[apiName] = await _catalogClient
                .ListOperationsAsync(service, apiName, cancellationToken)
                .ConfigureAwait(false);
        }))).ConfigureAwait(false);

        var apiScopes = apiInfos.Values
            .Select(api => ToApiScope(api, operationsByApi.GetValueOrDefault(api.Name) ?? []))
            .ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        var productScopes = products
            .Select(product => new ProductScope(
                $"products/{product.Name}",
                product.Name,
                DisplayNameOrName(product.DisplayName, product.Name),
                (productApiNames.GetValueOrDefault(product.Name) ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => apiScopes[name])
                    .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();

        return new ScopeCatalog(
            service,
            new GlobalScope(),
            productScopes,
            apiScopes.Values.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static ApiScope ToApiScope(ApimApiInfo api, IReadOnlyList<ApimOperationInfo> operations)
    {
        var apiScopeId = $"apis/{api.Name}";
        return new ApiScope(
            apiScopeId,
            api.Name,
            DisplayNameOrName(api.DisplayName, api.Name),
            api.Path,
            operations
                .Select(op => new OperationScope(
                    $"{apiScopeId}/operations/{op.Name}",
                    op.Name,
                    DisplayNameOrName(op.DisplayName, op.Name),
                    op.Method,
                    op.UrlTemplate))
                .OrderBy(op => op.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static string DisplayNameOrName(string? displayName, string name) =>
        string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    private static async Task ThrottledAsync(SemaphoreSlim throttle, Func<Task> work)
    {
        await throttle.WaitAsync().ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            throttle.Release();
        }
    }

    private static ArmNotFound NotFoundViaResourceGraph(string serviceName, string? subscriptionId, string? resourceGroupName)
    {
        var where = subscriptionId is null
            ? "in any subscription visible to the current credential"
            : $"in subscription '{subscriptionId}'";
        if (resourceGroupName is not null)
        {
            where += $" (resource group '{resourceGroupName}')";
        }

        return new ArmNotFound(
            $"{ServiceResourceKind} '{serviceName}' was not found {where}. Verify the service name and that the signed-in identity has Reader access, or set Apim:SubscriptionId and Apim:ResourceGroupName explicitly.");
    }

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9-]{0,49}$")]
    private static partial Regex ServiceNamePattern();
}
