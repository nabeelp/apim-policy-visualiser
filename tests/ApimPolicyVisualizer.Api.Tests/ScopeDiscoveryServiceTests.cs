using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApimPolicyVisualizer.Api.Azure;
using ApimPolicyVisualizer.Api.Configuration;
using ApimPolicyVisualizer.Api.Scopes;
using Azure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApimPolicyVisualizer.Api.Tests;

public class ScopeDiscoveryServiceTests
{
    private const string SubscriptionId = "00000000-0000-0000-0000-000000000001";
    private const string ResourceGroup = "rg-apim";
    private const string ServiceName = "apim-zlyway6g7icoy";

    private static readonly ApimServiceLocation Location = new(SubscriptionId, ResourceGroup, ServiceName);

    // ---------- Service-level behaviour ----------

    [Fact]
    public async Task GetScopes_ReturnsGlobalProductsApisAndOperations_ForMockedClient()
    {
        var catalog = FakeCatalogClient.Sample();
        var locator = new FakeLocator();
        var service = CreateService(Explicit(), locator, catalog);

        var result = await service.GetScopesAsync();

        Assert.True(result.IsSuccess);
        var scopes = result.Value!;
        Assert.Equal(Location, scopes.Service);
        Assert.Equal("global", scopes.Global.ScopeId);
        Assert.Equal("Global", scopes.Global.Kind);

        Assert.Equal(["Starter", "Unlimited"], scopes.Products.Select(p => p.DisplayName));
        var starter = scopes.Products[0];
        Assert.Equal("products/starter", starter.ScopeId);
        Assert.Equal("Product", starter.Kind);
        var starterApi = Assert.Single(starter.Apis);
        Assert.Equal("apis/echo-api", starterApi.ScopeId);
        Assert.Equal(["Create resource", "Retrieve resource"], starterApi.Operations.Select(o => o.DisplayName));
        Assert.Equal("apis/echo-api/operations/create-resource", starterApi.Operations[0].ScopeId);
        Assert.Equal("POST", starterApi.Operations[0].Method);
        Assert.Equal("Operation", starterApi.Operations[0].Kind);

        var unlimited = scopes.Products[1];
        Assert.Equal(["Echo API", "Petstore"], unlimited.Apis.Select(a => a.DisplayName));

        Assert.Equal(["apis/echo-api", "apis/petstore", "apis/unpublished-api"], scopes.Apis.Select(a => a.ScopeId));
        var petstore = scopes.Apis.Single(a => a.Name == "petstore");
        Assert.Equal("pets", petstore.Path);
        Assert.Equal("apis/petstore/operations/list-pets", Assert.Single(petstore.Operations).ScopeId);
        Assert.Empty(scopes.Apis.Single(a => a.Name == "unpublished-api").Operations);

        Assert.Equal(0, locator.Calls);
    }

    [Fact]
    public async Task ExplicitConfiguration_TakesPrecedence_OverResourceGraph()
    {
        var locator = new FakeLocator { Results = [new ApimServiceLocation("other-sub", "other-rg", ServiceName)] };
        var service = CreateService(Explicit(), locator, FakeCatalogClient.Sample());

        var result = await service.ResolveServiceAsync();

        Assert.Equal(Location, result.Value);
        Assert.Equal(0, locator.Calls);
    }

    [Fact]
    public async Task MissingSubscriptionAndResourceGroup_AreAutoResolvedByServiceName_AndCached()
    {
        var locator = new FakeLocator
        {
            Results =
            [
                new ApimServiceLocation("sub-x", "rg-x", "some-other-apim"),
                new ApimServiceLocation(SubscriptionId, ResourceGroup, ServiceName.ToUpperInvariant()),
            ],
        };
        var catalog = FakeCatalogClient.Sample();
        var service = CreateService(new ApimOptions(), locator, catalog);

        var first = await service.GetScopesAsync();
        var second = await service.GetScopesAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(Location, first.Value!.Service);
        Assert.Equal(1, locator.Calls);
        Assert.Equal(ServiceName, locator.LastServiceName);
        Assert.Null(locator.LastSubscriptionId);
        Assert.All(catalog.RequestedServices, s => Assert.Equal(Location, s));
    }

    [Fact]
    public async Task ConfiguredSubscriptionOnly_ScopesResourceGraphLookupToThatSubscription()
    {
        var locator = new FakeLocator
        {
            Results =
            [
                new ApimServiceLocation("sub-other", "rg-other", ServiceName),
                new ApimServiceLocation(SubscriptionId, ResourceGroup, ServiceName),
            ],
        };
        var service = CreateService(new ApimOptions { SubscriptionId = SubscriptionId }, locator, FakeCatalogClient.Sample());

        var result = await service.ResolveServiceAsync();

        Assert.Equal(Location, result.Value);
        Assert.Equal(SubscriptionId, locator.LastSubscriptionId);
    }

    [Fact]
    public async Task ServiceNotFoundViaResourceGraph_ReturnsDescriptiveNotFound()
    {
        var service = CreateService(new ApimOptions(), new FakeLocator(), FakeCatalogClient.Sample());

        var result = await service.GetScopesAsync();

        var notFound = Assert.IsType<ArmNotFound>(result.Failure);
        Assert.Contains($"APIM service '{ServiceName}' was not found", notFound.Message);
        Assert.Contains("subscription visible to the current credential", notFound.Message);
    }

    [Fact]
    public async Task ResourceGraphForbidden_ReturnsDescriptiveForbidden()
    {
        var locator = new FakeLocator { Failure = new RequestFailedException(403, "denied") };
        var service = CreateService(new ApimOptions(), locator, FakeCatalogClient.Sample());

        var result = await service.GetScopesAsync();

        var forbidden = Assert.IsType<ArmForbidden>(result.Failure);
        Assert.Contains("was denied", forbidden.Message);
        Assert.Contains(ServiceName, forbidden.Message);
    }

    [Fact]
    public async Task ArmServiceLookup404_ReturnsDescriptiveNotFound()
    {
        var catalog = FakeCatalogClient.Sample();
        catalog.EnsureFailure = new RequestFailedException(404, "ResourceNotFound");
        var service = CreateService(Explicit(), new FakeLocator(), catalog);

        var result = await service.GetScopesAsync();

        var notFound = Assert.IsType<ArmNotFound>(result.Failure);
        Assert.Contains($"APIM service '{ServiceName}' was not found", notFound.Message);
        Assert.Contains(ResourceGroup, notFound.Message);
    }

    [Fact]
    public async Task ArmListing403_ReturnsDescriptiveForbidden()
    {
        var catalog = FakeCatalogClient.Sample();
        catalog.ListFailure = new RequestFailedException(403, "AuthorizationFailed");
        var service = CreateService(Explicit(), new FakeLocator(), catalog);

        var result = await service.GetScopesAsync();

        var forbidden = Assert.IsType<ArmForbidden>(result.Failure);
        Assert.Equal(
            $"Access to '{Location.ResourceId}' was denied. Verify the caller's role assignment.",
            forbidden.Message);
    }

    [Fact]
    public async Task FindScope_ResolvesEveryHierarchyLevel()
    {
        var result = await CreateService(Explicit(), new FakeLocator(), FakeCatalogClient.Sample()).GetScopesAsync();
        var scopes = result.Value!;

        Assert.Equal(ScopeKinds.Global, scopes.FindScope("global")!.Kind);
        Assert.Equal("starter", scopes.FindScope("products/starter")!.ProductName);
        Assert.Equal("petstore", scopes.FindScope("apis/petstore")!.ApiName);
        var operation = scopes.FindScope("apis/echo-api/operations/retrieve-resource")!;
        Assert.Equal(ScopeKinds.Operation, operation.Kind);
        Assert.Equal("echo-api", operation.ApiName);
        Assert.Equal("retrieve-resource", operation.OperationName);
        Assert.Null(scopes.FindScope("apis/does-not-exist"));
    }

    // ---------- HTTP endpoint ----------

    [Fact]
    public async Task GetApiScopes_Returns200_WithGlobalProductsAndOperations()
    {
        using var factory = CreateFactory(FakeCatalogClient.Sample(), new FakeLocator(), explicitConfig: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/scopes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("global", root.GetProperty("global").GetProperty("scopeId").GetString());
        Assert.Equal("Global", root.GetProperty("global").GetProperty("kind").GetString());
        Assert.Equal(ServiceName, root.GetProperty("service").GetProperty("serviceName").GetString());

        var products = root.GetProperty("products").EnumerateArray().ToList();
        Assert.Equal(2, products.Count);
        Assert.Equal("products/starter", products[0].GetProperty("scopeId").GetString());
        var echoOps = products[0].GetProperty("apis")[0].GetProperty("operations").EnumerateArray()
            .Select(o => o.GetProperty("scopeId").GetString())
            .ToList();
        Assert.Equal(["apis/echo-api/operations/create-resource", "apis/echo-api/operations/retrieve-resource"], echoOps);

        var apis = root.GetProperty("apis").EnumerateArray().ToList();
        Assert.Equal(3, apis.Count);
        Assert.All(apis, a => Assert.Equal("Api", a.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task GetApiScopes_Returns404_WithDescriptiveBody_WhenServiceNotFound()
    {
        using var factory = CreateFactory(FakeCatalogClient.Sample(), new FakeLocator(), explicitConfig: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/scopes");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("APIM service not found", problem.GetProperty("title").GetString());
        Assert.Contains($"APIM service '{ServiceName}' was not found", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetApiScopes_Returns404_WhenArmReportsServiceMissing()
    {
        var catalog = FakeCatalogClient.Sample();
        catalog.EnsureFailure = new RequestFailedException(404, "ResourceNotFound");
        using var factory = CreateFactory(catalog, new FakeLocator(), explicitConfig: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/scopes");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("was not found", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetApiScopes_Returns403_WithDescriptiveBody_OnAuthorizationFailure()
    {
        var catalog = FakeCatalogClient.Sample();
        catalog.EnsureFailure = new RequestFailedException(403, "AuthorizationFailed");
        using var factory = CreateFactory(catalog, new FakeLocator(), explicitConfig: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/scopes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(403, problem.GetProperty("status").GetInt32());
        Assert.Equal("Access to APIM service denied", problem.GetProperty("title").GetString());
        var detail = problem.GetProperty("detail").GetString();
        Assert.Contains("was denied", detail);
        Assert.Contains(ServiceName, detail);
        Assert.DoesNotContain("AuthorizationFailed", detail);
    }

    [Fact]
    public void Application_RegistersScopeDiscoveryWithArmBackedClients()
    {
        using var factory = new WebApplicationFactory<Program>();

        Assert.IsType<ScopeDiscoveryService>(factory.Services.GetRequiredService<IScopeDiscoveryService>());
        Assert.IsType<ArmApimCatalogClient>(factory.Services.GetRequiredService<IApimCatalogClient>());
        Assert.IsType<ResourceGraphApimServiceLocator>(factory.Services.GetRequiredService<IApimServiceLocator>());
    }

    // ---------- Azure Resource Graph locator ----------

    [Fact]
    public async Task ResourceGraphLocator_PostsPinnedApiVersionQuery_WithBearerToken_AndParsesRows()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            $$"""{"totalRecords":1,"count":1,"data":[{"subscriptionId":"{{SubscriptionId}}","resourceGroup":"{{ResourceGroup}}","name":"{{ServiceName}}"}]}""");
        var locator = new ResourceGraphApimServiceLocator(new FakeHttpClientFactory(handler), new FakeTokenProvider("arm-token"));

        var results = await locator.FindByNameAsync(ServiceName, null, CancellationToken.None);

        Assert.Equal([Location], results);
        var request = handler.Request!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01",
            request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("arm-token", request.Headers.Authorization.Parameter);

        using var body = JsonDocument.Parse(handler.Body!);
        var query = body.RootElement.GetProperty("query").GetString()!;
        Assert.Contains("microsoft.apimanagement/service", query);
        Assert.Contains($"name =~ '{ServiceName}'", query);
        Assert.False(body.RootElement.TryGetProperty("subscriptions", out _));
    }

    [Fact]
    public async Task ResourceGraphLocator_ScopesToConfiguredSubscription_AndEscapesName()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{"data":[]}""");
        var locator = new ResourceGraphApimServiceLocator(new FakeHttpClientFactory(handler), new FakeTokenProvider("t"));

        var results = await locator.FindByNameAsync("bad'name", SubscriptionId, CancellationToken.None);

        Assert.Empty(results);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(SubscriptionId, body.RootElement.GetProperty("subscriptions")[0].GetString());
        Assert.Contains(@"name =~ 'bad\'name'", body.RootElement.GetProperty("query").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.NotFound, 404)]
    public async Task ResourceGraphLocator_ThrowsRequestFailed_WithStatus_WithoutEchoingBody(HttpStatusCode status, int expected)
    {
        var handler = new CapturingHandler(status, """{"error":{"code":"AuthorizationFailed","message":"internal-detail"}}""");
        var locator = new ResourceGraphApimServiceLocator(new FakeHttpClientFactory(handler), new FakeTokenProvider("t"));

        var ex = await Assert.ThrowsAsync<RequestFailedException>(
            () => locator.FindByNameAsync(ServiceName, null, CancellationToken.None));

        Assert.Equal(expected, ex.Status);
        Assert.DoesNotContain("internal-detail", ex.Message);
    }

    // ---------- helpers ----------

    private static ApimOptions Explicit() => new()
    {
        SubscriptionId = SubscriptionId,
        ResourceGroupName = ResourceGroup,
        ServiceName = ServiceName,
    };

    private static ScopeDiscoveryService CreateService(ApimOptions options, IApimServiceLocator locator, IApimCatalogClient catalog) =>
        new(Options.Create(options), locator, catalog, NullLogger<ScopeDiscoveryService>.Instance);

    private static WebApplicationFactory<Program> CreateFactory(FakeCatalogClient catalog, FakeLocator locator, bool explicitConfig) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (explicitConfig)
            {
                builder.UseSetting("Apim:SubscriptionId", SubscriptionId);
                builder.UseSetting("Apim:ResourceGroupName", ResourceGroup);
            }

            builder.UseSetting("Apim:ServiceName", ServiceName);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApimCatalogClient>();
                services.RemoveAll<IApimServiceLocator>();
                services.AddSingleton<IApimCatalogClient>(catalog);
                services.AddSingleton<IApimServiceLocator>(locator);
            });
        });

    private sealed class FakeLocator : IApimServiceLocator
    {
        public IReadOnlyList<ApimServiceLocation> Results { get; init; } = [];

        public Exception? Failure { get; init; }

        public int Calls { get; private set; }

        public string? LastServiceName { get; private set; }

        public string? LastSubscriptionId { get; private set; }

        public Task<IReadOnlyList<ApimServiceLocation>> FindByNameAsync(string serviceName, string? subscriptionId, CancellationToken cancellationToken)
        {
            Calls++;
            LastServiceName = serviceName;
            LastSubscriptionId = subscriptionId;
            return Failure is null ? Task.FromResult(Results) : Task.FromException<IReadOnlyList<ApimServiceLocation>>(Failure);
        }
    }

    private sealed class FakeCatalogClient : IApimCatalogClient
    {
        public List<ApimProductInfo> Products { get; } = [];

        public Dictionary<string, List<string>> ProductApis { get; } = [];

        public List<ApimApiInfo> Apis { get; } = [];

        public Dictionary<string, List<ApimOperationInfo>> Operations { get; } = [];

        public Exception? EnsureFailure { get; set; }

        public Exception? ListFailure { get; set; }

        public System.Collections.Concurrent.ConcurrentBag<ApimServiceLocation> RequestedServices { get; } = [];

        public static FakeCatalogClient Sample()
        {
            var client = new FakeCatalogClient();
            client.Products.Add(new ApimProductInfo("unlimited", "Unlimited"));
            client.Products.Add(new ApimProductInfo("starter", "Starter"));
            client.ProductApis["starter"] = ["echo-api"];
            client.ProductApis["unlimited"] = ["petstore", "echo-api"];
            client.Apis.Add(new ApimApiInfo("petstore", "Petstore", "pets"));
            client.Apis.Add(new ApimApiInfo("echo-api", "Echo API", "echo"));
            client.Apis.Add(new ApimApiInfo("unpublished-api", "Unpublished API", "hidden"));
            client.Operations["echo-api"] =
            [
                new ApimOperationInfo("retrieve-resource", "Retrieve resource", "GET", "/resource"),
                new ApimOperationInfo("create-resource", "Create resource", "POST", "/resource"),
            ];
            client.Operations["petstore"] = [new ApimOperationInfo("list-pets", "List pets", "GET", "/pets")];
            return client;
        }

        public Task EnsureServiceExistsAsync(ApimServiceLocation service, CancellationToken cancellationToken)
        {
            RequestedServices.Add(service);
            return EnsureFailure is null ? Task.CompletedTask : Task.FromException(EnsureFailure);
        }

        public Task<IReadOnlyList<ApimProductInfo>> ListProductsAsync(ApimServiceLocation service, CancellationToken cancellationToken) =>
            Respond<IReadOnlyList<ApimProductInfo>>(service, Products);

        public Task<IReadOnlyList<string>> ListProductApiNamesAsync(ApimServiceLocation service, string productName, CancellationToken cancellationToken) =>
            Respond<IReadOnlyList<string>>(service, ProductApis.GetValueOrDefault(productName) ?? []);

        public Task<IReadOnlyList<ApimApiInfo>> ListApisAsync(ApimServiceLocation service, CancellationToken cancellationToken) =>
            Respond<IReadOnlyList<ApimApiInfo>>(service, Apis);

        public Task<IReadOnlyList<ApimOperationInfo>> ListOperationsAsync(ApimServiceLocation service, string apiName, CancellationToken cancellationToken) =>
            Respond<IReadOnlyList<ApimOperationInfo>>(service, Operations.GetValueOrDefault(apiName) ?? []);

        private Task<T> Respond<T>(ApimServiceLocation service, T value)
        {
            RequestedServices.Add(service);
            return ListFailure is null ? Task.FromResult(value) : Task.FromException<T>(ListFailure);
        }
    }

    private sealed class FakeTokenProvider(string token) : IArmTokenProvider
    {
        public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(token);
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
