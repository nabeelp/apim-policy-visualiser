using ApimPolicyVisualizer.Api.Policy.Source;

namespace ApimPolicyVisualizer.Api.Policy.Model;

/// <summary>
/// Generic classification and friendly labels for recognised APIM policy elements (SPF-FR-08). Unlisted tags are
/// unknown and become opaque steps.
/// </summary>
public static class PolicyElementCatalog
{
    private static readonly Dictionary<string, string> Categories = Build();

    /// <summary>Tags whose children are policy statements executed in place (rather than configuration).</summary>
    public static readonly IReadOnlySet<string> PolicyWrappers = new HashSet<string>(StringComparer.Ordinal) { "limit-concurrency", "wait" };

    /// <summary>Tags that end the pipeline with a response to the caller.</summary>
    public static readonly IReadOnlySet<string> Terminating = new HashSet<string>(StringComparer.Ordinal) { "return-response", "mock-response" };

    public static bool TryGetCategory(string tag, out string category) => Categories.TryGetValue(tag, out category!);

    public static bool IsKnown(string tag) => Categories.ContainsKey(tag) || tag is "choose" or "retry";

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string category, params string[] tags)
        {
            foreach (var tag in tags)
            {
                map[tag] = category;
            }
        }

        Add(FlowCategories.BackendCall, "forward-request", "send-request", "send-one-way-request", "send-service-bus-message", "publish-to-dapr", "invoke-dapr-binding");
        Add(FlowCategories.Routing, "set-backend-service", "rewrite-uri");
        Add(FlowCategories.Authentication,
            "validate-jwt", "validate-azure-ad-token", "validate-client-certificate", "authentication-managed-identity",
            "authentication-basic", "authentication-certificate", "get-authorization-context");
        Add(FlowCategories.Validation,
            "check-header", "ip-filter", "validate-content", "validate-parameters", "validate-headers", "validate-status-code",
            "validate-odata-request", "validate-graphql-request", "rate-limit", "rate-limit-by-key", "quota", "quota-by-key",
            "limit-concurrency", "llm-token-limit", "azure-openai-token-limit", "llm-content-safety");
        Add(FlowCategories.Transformation,
            "set-header", "set-query-parameter", "set-body", "set-method", "set-status", "find-and-replace", "json-to-xml",
            "xml-to-json", "xsl-transform", "redirect-content-urls", "set-graphql-resolver", "json-rpc-to-rest");
        Add(FlowCategories.State, "set-variable");
        Add(FlowCategories.Cache,
            "cache-lookup", "cache-store", "cache-lookup-value", "cache-store-value", "cache-remove-value",
            "llm-semantic-cache-lookup", "llm-semantic-cache-store", "azure-openai-semantic-cache-lookup", "azure-openai-semantic-cache-store");
        Add(FlowCategories.Observability, "trace", "emit-metric", "llm-emit-token-metric", "azure-openai-emit-token-metric", "log-to-eventhub");
        Add(FlowCategories.Cors, "cors", "jsonp");
        Add(FlowCategories.Inherited, "base", "include-fragment");
        Add(FlowCategories.Response, "return-response", "mock-response");
        Add(FlowCategories.Control, "wait");
        return map;
    }

    /// <summary>A short human label for a step, e.g. <c>Set variable requestedModel</c>.</summary>
    public static string Label(SourceElement element)
    {
        string? A(string name) => element.GetAttribute(name) is { } attribute
            ? attribute.IsExpression ? "(expression)" : attribute.Value
            : null;

        var label = element.Name switch
        {
            "set-variable" => $"Set variable {A("name")}",
            "set-header" => HeaderLabel("header", A("name"), A("exists-action")),
            "set-query-parameter" => HeaderLabel("query parameter", A("name"), A("exists-action")),
            "set-body" => "Set body",
            "set-method" => $"Set method {element.Text?.Trim()}",
            "set-status" => $"Set status {A("code")} {A("reason")}",
            "forward-request" => "Forward request to backend",
            "send-request" => A("response-variable-name") is { } response ? $"Send request (response in {response})" : "Send request",
            "send-one-way-request" => "Send one-way request",
            "set-backend-service" => $"Set backend service {A("backend-id") ?? A("base-url")}",
            "rewrite-uri" => $"Rewrite URI to {A("template")}",
            "validate-jwt" => "Validate JWT",
            "validate-azure-ad-token" => "Validate Microsoft Entra ID token",
            "validate-client-certificate" => "Validate client certificate",
            "check-header" => $"Check header {A("name")}",
            "ip-filter" => $"IP filter ({A("action")})",
            "rate-limit" => $"Rate limit {A("calls")} calls / {A("renewal-period")}s",
            "rate-limit-by-key" => $"Rate limit by key: {A("calls")} calls / {A("renewal-period")}s",
            "quota" => $"Quota {A("calls") ?? A("bandwidth")} / {A("renewal-period")}s",
            "quota-by-key" => $"Quota by key {A("calls") ?? A("bandwidth")} / {A("renewal-period")}s",
            "limit-concurrency" => $"Limit concurrency to {A("max-count")}",
            "llm-token-limit" or "azure-openai-token-limit" => A("tokens-per-minute") is { } tpm ? $"Token limit {tpm} tokens/min" : "Token limit",
            "validate-content" => "Validate content",
            "validate-parameters" => "Validate parameters",
            "validate-headers" => "Validate headers",
            "validate-status-code" => "Validate status code",
            "cache-lookup-value" => $"Look up cached value into {A("variable-name")}",
            "cache-store-value" => "Store value in cache",
            "cache-remove-value" => "Remove cached value",
            "cache-lookup" => "Look up cached response",
            "cache-store" => "Store response in cache",
            "trace" => $"Trace {A("source")}",
            "emit-metric" => $"Emit metric {A("name")}",
            "llm-emit-token-metric" or "azure-openai-emit-token-metric" => "Emit token metric",
            "log-to-eventhub" => $"Log to Event Hub {A("logger-id")}",
            "cors" => "CORS",
            "base" => "Inherited policies (base)",
            "include-fragment" => $"Include fragment {A("fragment-id")}",
            "authentication-managed-identity" => "Authenticate with managed identity",
            "authentication-basic" => "Authenticate with basic credentials",
            "authentication-certificate" => "Authenticate with client certificate",
            "mock-response" => $"Mock response {A("status-code") ?? "200"}",
            "find-and-replace" => "Find and replace",
            "json-to-xml" => "Convert JSON to XML",
            "xml-to-json" => "Convert XML to JSON",
            "wait" => $"Wait for {A("for") ?? "all"}",
            _ => IsKnown(element.Name) ? FragmentRegionBuilder.Humanize(element.Name) : $"{element.Name} (unrecognised)",
        };

        return Truncate(string.Join(' ', label.Split(' ', StringSplitOptions.RemoveEmptyEntries)), 80);
    }

    public static string Truncate(string text, int max)
    {
        var singleLine = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        return singleLine.Length <= max ? singleLine : singleLine[..(max - 1)] + "…";
    }

    private static string HeaderLabel(string kind, string? name, string? existsAction) => existsAction switch
    {
        "delete" => $"Delete {kind} {name}",
        "skip" => $"Set {kind} {name} (if absent)",
        "append" => $"Append {kind} {name}",
        _ => $"Set {kind} {name}",
    };
}
