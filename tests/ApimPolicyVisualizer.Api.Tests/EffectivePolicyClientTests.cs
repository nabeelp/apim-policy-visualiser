using System.Net;
using System.Text;
using System.Text.Json;
using ApimPolicyVisualizer.Api.Azure;
using ApimPolicyVisualizer.Api.Policy;
using ApimPolicyVisualizer.Api.Scopes;
using Microsoft.Extensions.Logging;

namespace ApimPolicyVisualizer.Api.Tests;

public sealed class EffectivePolicyClientTests
{
    private const string Token = "fake-arm-token-value";
    private const string PolicyXml = "<policies><inbound><base /><set-header name=\"x\" exists-action=\"override\"><value>@(context.RequestId)</value></set-header></inbound><backend><base /></backend><outbound><base /></outbound><on-error><base /></on-error></policies>";

    private static readonly ApimServiceLocation Service = new("sub-123", "rg-apim", "apim-zlyway6g7icoy");

    private const string ServiceId =
        "/subscriptions/sub-123/resourceGroups/rg-apim/providers/Microsoft.ApiManagement/service/apim-zlyway6g7icoy";

    public static TheoryData<ScopeDescriptor, string> ScopeCases => new()
    {
        { new ScopeDescriptor("global", ScopeKinds.Global), $"{ServiceId}/policies/policy" },
        { new ScopeDescriptor("products/starter", ScopeKinds.Product, ProductName: "starter"), $"{ServiceId}/products/starter/policies/policy" },
        { new ScopeDescriptor("apis/echo-api", ScopeKinds.Api, ApiName: "echo-api"), $"{ServiceId}/apis/echo-api/policies/policy" },
        {
            new ScopeDescriptor("apis/echo-api/operations/get-item", ScopeKinds.Operation, ApiName: "echo-api", OperationName: "get-item"),
            $"{ServiceId}/apis/echo-api/operations/get-item/policies/policy"
        },
    };

    [Theory]
    [MemberData(nameof(ScopeCases))]
    public async Task RequestUrl_ForEachScopeKind_TargetsPolicyEndpointWithEffectiveRawXmlAndPinnedApiVersion(ScopeDescriptor scope, string expectedPath)
    {
        var handler = new RecordingHandler(_ => JsonPolicyResponse(PolicyXml));
        var client = CreateClient(handler);

        var result = await client.GetEffectivePolicyXmlAsync(Service, scope);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("management.azure.com", request.Uri.Host);
        Assert.Equal(Uri.UriSchemeHttps, request.Uri.Scheme);
        Assert.Equal(expectedPath, request.Uri.AbsolutePath);

        var query = ParseQuery(request.Uri.Query);
        Assert.Equal("xml", query["format"]);
        Assert.Equal("true", query["effective"]);
        Assert.Equal("2024-05-01", query["api-version"]);
        Assert.Equal("2024-05-01", EffectivePolicyClient.ApiVersion);
    }

    [Fact]
    public async Task Request_UsesBearerTokenFromArmTokenProvider()
    {
        var handler = new RecordingHandler(_ => JsonPolicyResponse(PolicyXml));
        var tokenProvider = new FakeTokenProvider();
        var client = CreateClient(handler, tokenProvider);

        await client.GetEffectivePolicyXmlAsync(Service, new ScopeDescriptor("global", ScopeKinds.Global));

        Assert.Equal(1, tokenProvider.Calls);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.AuthScheme);
        Assert.Equal(Token, request.AuthParameter);
    }

    [Fact]
    public async Task Success_ReturnsRawXmlVerbatimFromPolicyContractEnvelope()
    {
        var client = CreateClient(new RecordingHandler(_ => JsonPolicyResponse(PolicyXml)));

        var result = await client.GetEffectivePolicyXmlAsync(Service, new ScopeDescriptor("apis/echo-api", ScopeKinds.Api, ApiName: "echo-api"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);
        Assert.Equal(PolicyXml, result.Value);
    }

    [Fact]
    public async Task Success_AcceptsBareXmlBody()
    {
        var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(PolicyXml, Encoding.UTF8, "application/vnd.ms-azure-apim.policy.raw+xml"),
        }));

        var result = await client.GetEffectivePolicyXmlAsync(Service, new ScopeDescriptor("global", ScopeKinds.Global));

        Assert.Equal(PolicyXml, result.Value);
    }

    [Fact]
    public async Task Arm404_MapsToTypedNotFoundWithDescriptiveMessage()
    {
        var logs = new CapturingLoggerProvider();
        var client = CreateClient(new RecordingHandler(_ => ErrorResponse(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"ResourceNotFound\",\"message\":\"internal-detail-xyz\"}}")), logs: logs);
        var scope = new ScopeDescriptor("apis/echo-api/operations/get-item", ScopeKinds.Operation, ApiName: "echo-api", OperationName: "get-item");

        var result = await client.GetEffectivePolicyXmlAsync(Service, scope);

        Assert.False(result.IsSuccess);
        var notFound = Assert.IsType<ArmNotFound>(result.Failure);
        Assert.Contains("not found", notFound.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("get-item", notFound.Message);
        Assert.Contains("echo-api", notFound.Message);
        Assert.Contains(Service.ServiceName, notFound.Message);
        Assert.DoesNotContain("internal-detail-xyz", notFound.Message);
        Assert.DoesNotContain(Token, logs.AllText);
    }

    [Fact]
    public async Task Arm403_MapsToTypedForbiddenWithDescriptiveMessage()
    {
        var logs = new CapturingLoggerProvider();
        var client = CreateClient(new RecordingHandler(_ => ErrorResponse(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"AuthorizationFailed\",\"message\":\"internal-detail-xyz\"}}")), logs: logs);
        var scope = new ScopeDescriptor("products/starter", ScopeKinds.Product, ProductName: "starter");

        var result = await client.GetEffectivePolicyXmlAsync(Service, scope);

        Assert.False(result.IsSuccess);
        var forbidden = Assert.IsType<ArmForbidden>(result.Failure);
        Assert.Contains("denied", forbidden.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role assignment", forbidden.Message);
        Assert.Contains("starter", forbidden.Message);
        Assert.Contains(Service.ServiceName, forbidden.Message);
        Assert.DoesNotContain("internal-detail-xyz", forbidden.Message);
        Assert.DoesNotContain(Token, logs.AllText);
    }

    [Fact]
    public async Task OtherArmFailure_ThrowsWithoutEchoingResponseBody()
    {
        var client = CreateClient(new RecordingHandler(_ => ErrorResponse(HttpStatusCode.InternalServerError, "internal-detail-xyz")));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetEffectivePolicyXmlAsync(Service, new ScopeDescriptor("global", ScopeKinds.Global)));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.DoesNotContain("internal-detail-xyz", ex.Message);
        Assert.DoesNotContain(Token, ex.Message);
    }

    [Fact]
    public void BuildRequestUri_EscapesResourceNameSegments()
    {
        var uri = EffectivePolicyClient.BuildRequestUri(
            Service,
            new ScopeDescriptor("apis/a b", ScopeKinds.Api, ApiName: "a b/../x"));

        Assert.Contains("/apis/a%20b%2F..%2Fx/policies/policy", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(ScopeKinds.Product)]
    [InlineData(ScopeKinds.Api)]
    [InlineData(ScopeKinds.Operation)]
    [InlineData("Unknown")]
    public void BuildRequestUri_RejectsIncompleteOrUnknownScope(string kind)
    {
        Assert.Throws<ArgumentException>(() => EffectivePolicyClient.BuildRequestUri(Service, new ScopeDescriptor("x", kind)));
    }

    private static EffectivePolicyClient CreateClient(
        HttpMessageHandler handler,
        IArmTokenProvider? tokenProvider = null,
        CapturingLoggerProvider? logs = null)
    {
        var loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null)
            {
                b.AddProvider(logs);
            }
        });
        return new EffectivePolicyClient(
            new SingleClientFactory(handler),
            tokenProvider ?? new FakeTokenProvider(),
            loggerFactory.CreateLogger<EffectivePolicyClient>());
    }

    private static HttpResponseMessage JsonPolicyResponse(string xml) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                id = "/subscriptions/sub-123/.../policies/policy",
                type = "Microsoft.ApiManagement/service/policies",
                name = "policy",
                properties = new { format = "xml", value = xml },
            }),
            Encoding.UTF8,
            "application/json"),
    };

    private static HttpResponseMessage ErrorResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : string.Empty);

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? AuthScheme, string? AuthParameter);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return Task.FromResult(respond(request));
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeTokenProvider : IArmTokenProvider
    {
        public int Calls { get; private set; }

        public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(Token);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _entries = [];

        public string AllText
        {
            get
            {
                lock (_entries)
                {
                    return string.Join('\n', _entries);
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private void Add(string entry)
        {
            lock (_entries)
            {
                _entries.Add(entry);
            }
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Add($"{formatter(state, exception)} {exception}");
        }
    }
}
