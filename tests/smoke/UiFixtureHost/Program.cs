using System.Text;
using ApimPolicyVisualizer.Api.Azure;
using ApimPolicyVisualizer.Api.Policy;
using ApimPolicyVisualizer.Api.Scopes;

// This separate, loopback-only test host never registers Azure credentials or clients.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "src", "web", "dist"))
});
builder.WebHost.UseUrls("http://127.0.0.1:5022");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddControllers().AddApplicationPart(typeof(PolicyFlowController).Assembly);
builder.Services.AddSingleton<IScopeDiscoveryService, FixtureScopes>();
builder.Services.AddSingleton<IEffectivePolicyClient, FixturePolicy>();
builder.Services.AddPolicyFlowModel();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/healthz", () => new
{
    mode = "synthetic-ui-acceptance",
    policyBytes = Encoding.UTF8.GetByteCount(FixturePolicy.LargePolicy),
    policyLines = FixturePolicy.LargePolicy.Split('\n').Length
});
app.Run();

internal sealed class FixtureScopes : IScopeDiscoveryService
{
    private static readonly ApimServiceLocation Location =
        new("00000000-0000-0000-0000-000000000001", "fixture", "synthetic-ui-acceptance");

    private static readonly ApiScope Example = new(
        "apis/example", "example", "Example API", "example",
        [new("apis/example/operations/read", "read", "Read example", "GET", "/")]);

    private static readonly ScopeCatalog Catalog = new(
        Location, new GlobalScope(),
        [new ProductScope("products/example", "example", "Example product", [Example])],
        [
            Example,
            new("apis/large", "large", "300 KB policy", "large", []),
            new("apis/legacy", "legacy", "Small legacy-shape policy", "legacy", []),
            new("apis/forbidden", "forbidden", "Permission denied (403)", "forbidden", []),
            new("apis/missing", "missing", "Policy not found (404)", "missing", []),
            new("apis/invalid", "invalid", "Parse failure (422)", "invalid", [])
        ]);

    public Task<ArmResult<ApimServiceLocation>> ResolveServiceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ArmResult<ApimServiceLocation>.Success(Location));

    public Task<ArmResult<ScopeCatalog>> GetScopesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ArmResult<ScopeCatalog>.Success(Catalog));
}

internal sealed class FixturePolicy : IEffectivePolicyClient
{
    // The committed, sanitized synthetic AI-gateway fixture shared with the backend test suite.
    public static readonly string ExamplePolicy = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ai-gateway-synthetic.xml"));

    public static readonly string LargePolicy = BuildLargePolicy();

    private const string OldShapePolicy = """
        <policies><inbound><base />
        <choose>
          <when condition="@(context.Request.Method == &quot;GET&quot;)">
            <set-header name="x-read" exists-action="override"><value>true</value></set-header>
          </when>
          <otherwise><rate-limit calls="10" renewal-period="60" /></otherwise>
        </choose>
        </inbound>
        <backend><retry condition="@(context.Response.StatusCode == 503)" count="3" interval="1"><forward-request /></retry></backend>
        <outbound><set-header name="x-example" exists-action="delete" /></outbound>
        <on-error><return-response><set-status code="500" reason="Error" /></return-response></on-error>
        </policies>
        """;

    public Task<ArmResult<string>> GetEffectivePolicyXmlAsync(
        ApimServiceLocation service, ScopeDescriptor scope, CancellationToken cancellationToken = default) =>
        Task.FromResult(scope.ScopeId switch
        {
            "apis/forbidden" => ArmResult<string>.Fail(new ArmForbidden("Fixture identity cannot read this effective policy.")),
            "apis/missing" => ArmResult<string>.Fail(new ArmNotFound("Fixture effective policy no longer exists.")),
            "apis/invalid" => ArmResult<string>.Success("<policies><inbound>"),
            "apis/large" => ArmResult<string>.Success(LargePolicy),
            "apis/example" or "apis/example/operations/read" => ArmResult<string>.Success(ExamplePolicy),
            _ => ArmResult<string>.Success(OldShapePolicy)
        });

    /// <summary>
    /// About 300 KB and 2,500+ lines: the example's inbound fragment regions repeated with valid structure, each copy
    /// carrying a long descriptive comment (real effective policies are large mostly because of expressions/comments).
    /// </summary>
    private static string BuildLargePolicy()
    {
        const int targetBytes = 300 * 1024;
        var start = ExamplePolicy.IndexOf("<inbound>", StringComparison.Ordinal) + "<inbound>".Length;
        var end = ExamplePolicy.IndexOf("</inbound>", StringComparison.Ordinal);
        var inbound = ExamplePolicy[start..end];
        var padding = "\n<!--\n" + string.Join('\n', Enumerable.Range(0, 6).Select(_ => new string('x', 1400))) + "\n-->\n";
        var copies = new StringBuilder();
        var copy = 0;
        while (ExamplePolicy.Length + copies.Length < targetBytes)
        {
            copies.Append("\n<!-- Synthetic copy ").Append(copy++).Append(" -->").Append(padding).Append(inbound);
        }

        return ExamplePolicy[..end] + copies + ExamplePolicy[end..];
    }
}
