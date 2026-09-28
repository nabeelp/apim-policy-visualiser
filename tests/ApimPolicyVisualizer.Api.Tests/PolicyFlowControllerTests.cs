using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApimPolicyVisualizer.Api.Azure;
using ApimPolicyVisualizer.Api.Policy;
using ApimPolicyVisualizer.Api.Policy.Model;
using ApimPolicyVisualizer.Api.Scopes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ApimPolicyVisualizer.Api.Tests;

public class PolicyFlowControllerTests
{
    private static readonly ApimServiceLocation Location = new("00000000-0000-0000-0000-000000000001", "rg-example", "apim-example");

    private static readonly HashSet<string> ModelProperties =
        ["schemaVersion", "scopeId", "scopeKind", "source", "stages", "elements", "edges", "variables", "diagnostics"];

    private static readonly HashSet<string> ElementProperties =
    [
        "id", "kind", "stage", "parentId", "order", "label", "labelProvenance", "tag", "category", "observability", "fragment",
        "span", "attributes", "expressions", "properties", "comment", "badges", "facts", "exits", "variablesRead", "variablesWritten",
    ];

    private static readonly HashSet<string> EdgeProperties = ["id", "from", "to", "kind", "label", "priority", "condition", "facts"];

    [Fact]
    public async Task ValidScope_Returns200_WithSchemaVersion2Model_AndExactSourceText()
    {
        var xml = PolicyModelTestHelpers.FixtureXml();
        var policyClient = new FakePolicyClient { Result = ArmResult<string>.Success(xml) };
        using var factory = CreateFactory(new FakeScopeDiscovery(), policyClient);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/policy/effective-flow?scope=apis/example-api/operations/create-item");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("apis/example-api/operations/create-item", root.GetProperty("scopeId").GetString());
        Assert.Equal(ScopeKinds.Operation, root.GetProperty("scopeKind").GetString());
        Assert.Equal(xml, root.GetProperty("source").GetProperty("text").GetString());
        Assert.Equal(xml.Split('\n').Length, root.GetProperty("source").GetProperty("lineCount").GetInt32());

        Assert.Equal(["inbound", "backend", "outbound", "on-error"], root.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.All(root.GetProperty("stages").EnumerateArray(), s => Assert.True(s.GetProperty("present").GetBoolean()));

        var elements = root.GetProperty("elements").EnumerateArray().ToList();
        var ids = elements.Select(e => e.GetProperty("id").GetString()!).ToHashSet();
        Assert.Equal(elements.Count, ids.Count);
        Assert.All(elements, e =>
        {
            var parent = e.GetProperty("parentId");
            Assert.True(parent.ValueKind == JsonValueKind.Null || ids.Contains(parent.GetString()!));
        });

        var edges = root.GetProperty("edges").EnumerateArray().ToList();
        Assert.NotEmpty(edges);
        Assert.All(edges, e =>
        {
            Assert.Contains(e.GetProperty("from").GetString()!, ids);
            Assert.Contains(e.GetProperty("to").GetString()!, ids);
        });

        var kinds = edges.Select(e => e.GetProperty("kind").GetString()).ToHashSet();
        Assert.Superset(new HashSet<string?> { "sequence", "branch", "explicit-response", "stage-exception", "loop-back", "raises-error", "data-dependency", "preflight" }, kinds);

        var call = Assert.Single(policyClient.Calls);
        Assert.Equal(Location, call.Service);
        Assert.Equal(new ScopeDescriptor("apis/example-api/operations/create-item", ScopeKinds.Operation, ApiName: "example-api", OperationName: "create-item"), call.Scope);
    }

    [Fact]
    public async Task SerializedModel_ContainsOnlyContractProperties()
    {
        var policyClient = new FakePolicyClient { Result = ArmResult<string>.Success(PolicyModelTestHelpers.FixtureXml()) };
        using var factory = CreateFactory(new FakeScopeDiscovery(), policyClient);
        using var client = factory.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/policy/effective-flow?scope=global"));
        var root = json.RootElement;

        Assert.Equal(ModelProperties, root.EnumerateObject().Select(p => p.Name).ToHashSet());
        Assert.All(root.GetProperty("elements").EnumerateArray(), e => Assert.Subset(ElementProperties, e.EnumerateObject().Select(p => p.Name).ToHashSet()));
        Assert.All(root.GetProperty("edges").EnumerateArray(), e => Assert.Subset(EdgeProperties, e.EnumerateObject().Select(p => p.Name).ToHashSet()));

        var raw = root.GetRawText();
        Assert.DoesNotContain("sourceElement", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("XElement", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"parent\":", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("indexInParent", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logging_ContainsScopeId_ButNeverPolicyContent()
    {
        var logs = new CapturingLoggerProvider();
        var policyClient = new FakePolicyClient { Result = ArmResult<string>.Success(PolicyModelTestHelpers.FixtureXml()) };
        using var factory = CreateFactory(new FakeScopeDiscovery(), policyClient, logs);
        using var client = factory.CreateClient();

        using var ok = await client.GetAsync("/api/policy/effective-flow?scope=apis/example-api");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var malformedClient = new FakePolicyClient { Result = ArmResult<string>.Success("<policies><inbound><set-variable name=\"secretMarker\" value=\"x\" /><inbound>") };
        using var failingFactory = CreateFactory(new FakeScopeDiscovery(), malformedClient, logs);
        using var failing = failingFactory.CreateClient();
        using var unprocessable = await failing.GetAsync("/api/policy/effective-flow?scope=apis/example-api");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unprocessable.StatusCode);

        var messages = logs.Messages.ToList();
        Assert.Contains(messages, m => m.Contains("apis/example-api", StringComparison.Ordinal) && m.Contains("flow model", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("apis/example-api", StringComparison.Ordinal) && m.Contains("could not be parsed", StringComparison.Ordinal));
        foreach (var marker in new[] { "requestedModel", "responses-owner-", "Backend pool", "example-telemetry-probe", "<policies", "secretMarker", "context.Variables" })
        {
            Assert.DoesNotContain(messages, m => m.Contains(marker, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("global", ScopeKinds.Global)]
    [InlineData("products/starter", ScopeKinds.Product)]
    [InlineData("apis/example-api", ScopeKinds.Api)]
    public async Task EveryScopeKind_IsResolvedFromCatalog_AndPassedToClient(string scopeId, string expectedKind)
    {
        var policyClient = new FakePolicyClient { Result = ArmResult<string>.Success(PolicyModelTestHelpers.FixtureXml()) };
        using var factory = CreateFactory(new FakeScopeDiscovery(), policyClient);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/policy/effective-flow?scope={Uri.EscapeDataString(scopeId)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedKind, body.GetProperty("scopeKind").GetString());
        var call = Assert.Single(policyClient.Calls);
        Assert.Equal(scopeId, call.Scope.ScopeId);
        Assert.Equal(expectedKind, call.Scope.Kind);
    }

    [Fact]
    public async Task UnknownScope_Returns400_WithDescriptiveMessage_AndDoesNotCallClient()
    {
        var policyClient = new FakePolicyClient { Result = ArmResult<string>.Success(PolicyModelTestHelpers.FixtureXml()) };
        using var factory = CreateFactory(new FakeScopeDiscovery(), policyClient);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/policy/effective-flow?scope=apis/does-not-exist");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Unknown scope", problem.GetProperty("title").GetString());
        Assert.Contains("apis/does-not-exist", problem.GetProperty("detail").GetString());
        Assert.Empty(policyClient.Calls);
    }

    [Theory]
    [InlineData("/api/policy/effective-flow")]
    [InlineData("/api/policy/effective-flow?scope=")]
    [InlineData("/api/policy/effective-flow?scope=%20")]
    public async Task MissingScope_Returns400_WithDescriptiveMessage(string url)
    {
        var policyClient = new FakePolicyClient { Result = ArmResult<string>.Success(PolicyModelTestHelpers.FixtureXml()) };
        using var factory = CreateFactory(new FakeScopeDiscovery(), policyClient);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Missing scope", problem.GetProperty("title").GetString());
        Assert.Contains("scope", problem.GetProperty("detail").GetString());
        Assert.Empty(policyClient.Calls);
    }

    [Fact]
    public async Task ClientNotFound_PropagatesAs404_WithSameMessage()
    {
        const string message = "Effective policy for API 'example-api' (scope 'apis/example-api') was not found on APIM service 'apim-example'. Verify the scope still exists.";
        var policyClient = new FakePolicyClient { Result = new ArmNotFound(message) };
        using var factory = CreateFactory(new FakeScopeDiscovery(), policyClient);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/policy/effective-flow?scope=apis/example-api");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Effective policy not found", problem.GetProperty("title").GetString());
        Assert.Equal(message, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ClientForbidden_PropagatesAs403_WithSameMessage()
    {
        const string message = "Access to the effective policy for the Global scope on APIM service 'apim-example' was denied. Verify the caller's role assignment.";
        var policyClient = new FakePolicyClient { Result = new ArmForbidden(message) };
        using var factory = CreateFactory(new FakeScopeDiscovery(), policyClient);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/policy/effective-flow?scope=global");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Access to effective policy denied", problem.GetProperty("title").GetString());
        Assert.Equal(message, problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(true, HttpStatusCode.NotFound)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    public async Task ScopeCatalogFailure_PropagatesStatusAndMessage(bool notFound, HttpStatusCode expected)
    {
        ArmFailure failure = notFound ? new ArmNotFound("APIM service 'x' was not found.") : new ArmForbidden("Access to 'x' was denied.");
        var policyClient = new FakePolicyClient { Result = ArmResult<string>.Success(PolicyModelTestHelpers.FixtureXml()) };
        using var factory = CreateFactory(new FakeScopeDiscovery { Failure = failure }, policyClient);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/policy/effective-flow?scope=global");

        Assert.Equal(expected, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(failure.Message, problem.GetProperty("detail").GetString());
        Assert.Empty(policyClient.Calls);
    }

    [Theory]
    [InlineData("<policies><inbound>", "not well-formed")]
    [InlineData("<!DOCTYPE policies [<!ENTITY x \"y\">]><policies />", "DTD")]
    [InlineData("<policy><inbound /></policy>", "<policies>")]
    [InlineData("   ", "empty")]
    public async Task UnparseablePolicyXml_Returns422_WithDescriptiveMessage(string xml, string expectedFragment)
    {
        var policyClient = new FakePolicyClient { Result = ArmResult<string>.Success(xml) };
        using var factory = CreateFactory(new FakeScopeDiscovery(), policyClient);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/policy/effective-flow?scope=global");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("Policy parse failure", problem.GetProperty("title").GetString());
        var detail = problem.GetProperty("detail").GetString()!;
        Assert.Contains("global", detail);
        Assert.Contains(expectedFragment, detail, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeScopeDiscovery discovery, FakePolicyClient policyClient, CapturingLoggerProvider? logs = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IScopeDiscoveryService>();
                services.RemoveAll<IEffectivePolicyClient>();
                services.AddSingleton<IScopeDiscoveryService>(discovery);
                services.AddSingleton<IEffectivePolicyClient>(policyClient);
            });
        });

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner.Messages.Enqueue(formatter(state, exception));
                if (exception is not null)
                {
                    owner.Messages.Enqueue(exception.ToString());
                }
            }
        }
    }

    private sealed class FakeScopeDiscovery : IScopeDiscoveryService
    {
        public ArmFailure? Failure { get; init; }

        public Task<ArmResult<ApimServiceLocation>> ResolveServiceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Failure is null ? ArmResult<ApimServiceLocation>.Success(Location) : ArmResult<ApimServiceLocation>.Fail(Failure));

        public Task<ArmResult<ScopeCatalog>> GetScopesAsync(CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromResult(ArmResult<ScopeCatalog>.Fail(Failure));
            }

            var api = new ApiScope("apis/example-api", "example-api", "Example API", "example",
            [
                new OperationScope("apis/example-api/operations/create-item", "create-item", "Create item", "POST", "/items"),
            ]);
            var catalog = new ScopeCatalog(
                Location,
                new GlobalScope(),
                [new ProductScope("products/starter", "starter", "Starter", [api])],
                [api]);
            return Task.FromResult(ArmResult<ScopeCatalog>.Success(catalog));
        }
    }

    private sealed class FakePolicyClient : IEffectivePolicyClient
    {
        public required ArmResult<string> Result { get; init; }

        public List<(ApimServiceLocation Service, ScopeDescriptor Scope)> Calls { get; } = [];

        public Task<ArmResult<string>> GetEffectivePolicyXmlAsync(ApimServiceLocation service, ScopeDescriptor scope, CancellationToken cancellationToken = default)
        {
            lock (Calls)
            {
                Calls.Add((service, scope));
            }

            return Task.FromResult(Result);
        }
    }
}
