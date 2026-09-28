using ApimPolicyVisualizer.Api.Policy.Source;

namespace ApimPolicyVisualizer.Api.Policy.Model;

/// <summary>A documented APIM execution rule; every <see cref="FlowProvenance.ApimRule"/> fact cites one by <see cref="Id"/>.</summary>
public sealed record ApimRule(string Id, string Description);

/// <summary>The effect of one matched rule on one policy element.</summary>
/// <param name="RaisesError">True when the element gains a <c>raises-error</c> exit for this rule.</param>
/// <param name="ErrorLabel">Label of the raises-error edge.</param>
/// <param name="Qualifier">Edge-ID qualifier distinguishing a second raises-error exit of the same element.</param>
/// <param name="ConfigurationDependent">True when the outcome depends on an expression-valued attribute.</param>
public sealed record RuleOutcome(
    string RuleId,
    string Fact,
    bool RaisesError = false,
    string? ErrorLabel = null,
    string? Qualifier = null,
    bool ConfigurationDependent = false);

/// <summary>
/// Versioned, predicate-based APIM rule table (SPF-FR-03, SPF-FR-07, SPF-FR-09). Each row is a tag plus an attribute
/// predicate and produces facts with a rule ID; raises-error rows add error exits distinct from explicit responses.
/// Generic APIM semantics only — no knowledge of any particular policy.
/// </summary>
public sealed class ApimSemanticRules
{
    public const string Version = "2026-09-25";

    // Stage / structural rule IDs used by the model builder.
    public const string StageSequence = "stage.sequence";
    public const string StageException = "stage.exception";
    public const string OnErrorLane = "on-error.lane";
    public const string OnErrorNoResume = "on-error.no-resume";
    public const string OnErrorInternalFailure = "on-error.internal-failure";
    public const string GatewayDefaultError = "gateway.default-error";
    public const string OutboundFinalResponse = "outbound.final-response";
    public const string CorsPreflight = "cors.preflight";
    public const string ChooseFirstMatch = "choose.first-match";
    public const string ReturnResponseTerminates = "return-response.terminates";
    public const string MockResponseTerminates = "mock-response.terminates";
    public const string RetryFirstAttempt = "retry.first-attempt";
    public const string RetryLoopTest = "retry.loop-test";
    public const string RetryCount = "retry.count";

    private static readonly string[] AlwaysRaising =
    [
        "validate-jwt", "validate-azure-ad-token", "check-header", "ip-filter", "rate-limit", "rate-limit-by-key",
        "quota", "quota-by-key", "limit-concurrency", "llm-token-limit", "azure-openai-token-limit",
    ];

    private static readonly string[] ActionValidators = ["validate-content", "validate-parameters", "validate-headers", "validate-status-code"];

    private static readonly string[] ObservabilityTags = ["trace", "emit-metric", "llm-emit-token-metric", "azure-openai-emit-token-metric", "log-to-eventhub"];

    private static readonly IReadOnlyList<ApimRule> StructuralRules =
    [
        new(StageSequence, "Inbound, Backend and Outbound run sequentially in that order for every request."),
        new(StageException, "An unhandled execution failure in Inbound, Backend or Outbound transfers control to On-error (or to the gateway default error response when there is no on-error section)."),
        new(OnErrorLane, "On-error is an exception lane: it runs only after an execution failure."),
        new(OnErrorNoResume, "On-error never resumes Outbound; when it ends without return-response, the error response is returned to the caller."),
        new(OnErrorInternalFailure, "A failure while running On-error returns the gateway default error response."),
        new(GatewayDefaultError, "Without an on-error section APIM returns its default error response (typically 400/500)."),
        new(OutboundFinalResponse, "After Outbound the final backend response (including HTTP error statuses) is returned to the caller."),
        new(CorsPreflight, "When cors is present and no explicitly defined OPTIONS operation matches, APIM answers preflight requests at the cors policy; a matching OPTIONS operation disables this handling."),
        new(ChooseFirstMatch, "choose evaluates when conditions in order; only the first true branch runs, otherwise runs when none match."),
        new(ReturnResponseTerminates, "return-response ends the pipeline immediately and returns the response to the caller; later policies and stages do not run and On-error is not entered."),
        new(MockResponseTerminates, "mock-response ends the pipeline and returns a mocked response to the caller."),
        new(RetryFirstAttempt, "The retry body runs once before the condition is evaluated; the first attempt uses the state prepared before the loop."),
        new(RetryLoopTest, "After each attempt the body repeats only while the condition is true and the retry count budget remains."),
        new(RetryCount, "count is the maximum number of retries after the first attempt."),
    ];

    private static readonly IReadOnlyList<Row> Table = BuildTable();

    public static IReadOnlyList<ApimRule> Rules { get; } = StructuralRules.Concat(Table.Select(r => r.Rule)).DistinctBy(r => r.Id).ToList();

    public static IReadOnlySet<string> RuleIds { get; } = Rules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

    public static string Describe(string ruleId) => Rules.First(r => r.Id == ruleId).Description;

    public static bool IsObservability(string tag) => ObservabilityTags.Contains(tag, StringComparer.Ordinal);

    /// <summary>Evaluates every matching row for <paramref name="element"/> in table order.</summary>
    public IReadOnlyList<RuleOutcome> Evaluate(SourceElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        List<RuleOutcome>? outcomes = null;
        foreach (var row in Table)
        {
            if (string.Equals(row.Tag, element.Name, StringComparison.Ordinal) && row.Predicate(element))
            {
                (outcomes ??= []).Add(row.Outcome(element));
            }
        }

        return outcomes ?? (IReadOnlyList<RuleOutcome>)[];
    }

    private sealed record Row(string Tag, ApimRule Rule, Func<SourceElement, bool> Predicate, Func<SourceElement, RuleOutcome> Outcome);

    private static List<Row> BuildTable()
    {
        var rows = new List<Row>();

        void Add(string tag, string id, string description, Func<SourceElement, bool> predicate, Func<SourceElement, RuleOutcome> outcome) =>
            rows.Add(new Row(tag, new ApimRule(id, description), predicate, outcome));

        // forward-request: the primary backend call.
        Add("forward-request", "forward-request.primary-call",
            "forward-request is the primary backend call; it sends the request to the backend selected by routing state.",
            Always, e => Fact("forward-request.primary-call", "Primary backend call: sends the request to the backend selected by routing state."));
        Add("forward-request", "forward-request.transport-failure",
            "Transport failures of forward-request (timeout, connection errors) raise an error.",
            Always, _ => Raise("forward-request.transport-failure",
                "Transport failures (timeout, connection errors) raise an error.", "transport failure (timeout, connection)"));
        Add("forward-request", "forward-request.http-status-continues",
            "Backend HTTP error responses are responses, not errors: processing continues to Outbound unless fail-on-error-status-code is true.",
            e => !IsTrue(e, "fail-on-error-status-code") && !IsExpression(e, "fail-on-error-status-code"),
            _ => Fact("forward-request.http-status-continues",
                "Backend HTTP error responses (4xx/5xx) continue to Outbound; they are not On-error events."));
        Add("forward-request", "forward-request.fail-on-error-status-code",
            "With fail-on-error-status-code=\"true\" a backend HTTP error status raises an error.",
            e => IsTrue(e, "fail-on-error-status-code"),
            _ => Raise("forward-request.fail-on-error-status-code",
                "fail-on-error-status-code=\"true\": backend HTTP error statuses raise an error.", "backend HTTP error status", "http-status"));
        Add("forward-request", "forward-request.fail-on-error-status-code-expression",
            "An expression-valued fail-on-error-status-code makes HTTP-status error raising configuration-dependent.",
            e => IsExpression(e, "fail-on-error-status-code"),
            _ => Raise("forward-request.fail-on-error-status-code-expression",
                "fail-on-error-status-code is an expression: backend HTTP error statuses raise an error only when it evaluates to true.",
                "backend HTTP error status (if fail-on-error-status-code is true)", "http-status", configurationDependent: true));

        // send-request.
        Add("send-request", "send-request.raises",
            "send-request raises an error when the request fails unless ignore-error is true.",
            e => !IsTrue(e, "ignore-error") && !IsExpression(e, "ignore-error"),
            _ => Raise("send-request.raises", "A failed request raises an error (ignore-error is not true).", "request failure"));
        Add("send-request", "send-request.ignore-error",
            "With ignore-error=\"true\" a failed send-request continues with a null response variable.",
            e => IsTrue(e, "ignore-error"),
            e => Fact("send-request.ignore-error",
                $"ignore-error=\"true\": on failure processing continues with a null response variable{VariableSuffix(e)}."));
        Add("send-request", "send-request.ignore-error-expression",
            "An expression-valued ignore-error makes send-request error raising configuration-dependent.",
            e => IsExpression(e, "ignore-error"),
            _ => Raise("send-request.ignore-error-expression",
                "ignore-error is an expression: a failed request raises an error only when it evaluates to false.",
                "request failure (if ignore-error is false)", configurationDependent: true));

        // Always-raising access, throttling and token validation policies.
        foreach (var tag in AlwaysRaising)
        {
            var id = $"{tag}.raises-on-failure";
            Add(tag, id, $"{tag} raises an error when its check fails; the error is handled by On-error.",
                Always, _ => Raise(id, $"A failed {tag} check raises an error (handled by On-error, not an explicit response).", FailureLabel(tag)));
        }

        foreach (var tag in new[] { "validate-jwt", "validate-azure-ad-token" })
        {
            var id = $"{tag}.failed-validation-httpcode";
            Add(tag, id, "failed-validation-httpcode sets the status of the raised error; it is not an explicit response.",
                e => e.GetAttribute("failed-validation-httpcode") is not null,
                e => Fact(id, $"failed-validation-httpcode={e.AttributeValue("failed-validation-httpcode")} sets the status of the raised error; it is not an explicit response."));
        }

        // validate-content / parameters / headers / status-code: raise only for action "prevent".
        foreach (var tag in ActionValidators)
        {
            var prevent = $"{tag}.action-prevent";
            var expression = $"{tag}.action-expression";
            var detect = $"{tag}.action-detect";
            Add(tag, prevent, $"{tag} raises an error only where an action attribute is prevent.",
                e => Actions(e).Any(a => string.Equals(a.Value, "prevent", StringComparison.OrdinalIgnoreCase)),
                _ => Raise(prevent, "An action is prevent: a validation violation raises an error.", "validation violation (action prevent)"));
            Add(tag, expression, $"An expression-valued {tag} action makes error raising configuration-dependent.",
                e => !Actions(e).Any(a => string.Equals(a.Value, "prevent", StringComparison.OrdinalIgnoreCase)) && Actions(e).Any(a => a.IsExpression),
                _ => Raise(expression, "An action is an expression: a violation raises an error only when it evaluates to prevent.",
                    "validation violation (if action is prevent)", configurationDependent: true));
            Add(tag, detect, $"{tag} with only detect/ignore actions logs violations and continues.",
                e => !Actions(e).Any(a => a.IsExpression || string.Equals(a.Value, "prevent", StringComparison.OrdinalIgnoreCase)),
                _ => Fact(detect, "Actions are detect/ignore: violations are recorded and processing continues."));
        }

        // Routing and state.
        Add("set-backend-service", "set-backend-service.routing-state",
            "set-backend-service updates routing state used by forward-request; it does not call the backend.",
            Always, _ => Fact("set-backend-service.routing-state", "Routing-state update: selects the backend used by forward-request; this is not a call."));
        Add("rewrite-uri", "rewrite-uri.routing-state",
            "rewrite-uri changes the backend request URL; it does not call the backend.",
            Always, _ => Fact("rewrite-uri.routing-state", "Routing-state update: rewrites the backend request URL; this is not a call."));
        Add("cache-lookup-value", "cache-lookup-value.state-access",
            "cache-lookup-value reads a cached value into a variable; it is state access, not an early response.",
            Always, e => Fact("cache-lookup-value.state-access",
                $"State access: reads a cached value into variable {e.AttributeValue("variable-name") ?? "(unnamed)"} (default value when missing); not an early response."));
        Add("cache-store-value", "cache-store-value.state-write",
            "cache-store-value stores a value that affects subsequent requests.",
            Always, _ => Fact("cache-store-value.state-write", "State write: stores a value in the cache for subsequent requests."));
        Add("cache-lookup", "cache-lookup.response-cache",
            "cache-lookup serves a cached response on a hit.",
            Always, _ => Fact("cache-lookup.response-cache", "Response cache lookup: on a cache hit the cached response is served without calling the backend."));
        Add("base", "base.inherited",
            "base marks where policies of the enclosing scope run.",
            Always, _ => Fact("base.inherited", "Inherited policies of the enclosing scope run at this position."));
        Add("include-fragment", "include-fragment.inlined",
            "include-fragment inserts the fragment's policies at this position.",
            Always, e => Fact("include-fragment.inlined", $"Runs the policies of fragment {e.AttributeValue("fragment-id") ?? "(unnamed)"} at this position."));
        Add("cors", "cors.headers",
            "cors adds CORS headers for cross-origin requests.",
            Always, _ => Fact("cors.headers", "Adds CORS response headers for allowed cross-origin requests; the ordinary request continues."));
        foreach (var tag in ObservabilityTags)
        {
            var id = $"{tag}.observability";
            Add(tag, id, $"{tag} records telemetry and has no control-flow effect.",
                Always, _ => Fact(id, "Observability only: records telemetry and does not change control flow."));
        }

        return rows;
    }

    private static bool Always(SourceElement element) => true;

    private static bool IsTrue(SourceElement element, string attribute) =>
        string.Equals(element.AttributeValue(attribute)?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpression(SourceElement element, string attribute) => element.GetAttribute(attribute)?.IsExpression == true;

    /// <summary>Every <c>action</c> / <c>*-action</c> attribute on the element and its configuration children.</summary>
    private static IEnumerable<SourceAttribute> Actions(SourceElement element) =>
        element.DescendantsAndSelf().SelectMany(e => e.Attributes)
            .Where(a => a.Name == "action" || a.Name.EndsWith("-action", StringComparison.Ordinal));

    private static string VariableSuffix(SourceElement element) =>
        element.AttributeValue("response-variable-name") is { } name ? $" ({name})" : string.Empty;

    private static string FailureLabel(string tag) => tag switch
    {
        "validate-jwt" or "validate-azure-ad-token" => "token validation failed",
        "check-header" => "header check failed",
        "ip-filter" => "caller IP not allowed",
        "rate-limit" or "rate-limit-by-key" => "rate limit exceeded",
        "quota" or "quota-by-key" => "quota exceeded",
        "limit-concurrency" => "concurrency limit exceeded",
        "llm-token-limit" or "azure-openai-token-limit" => "token limit exceeded",
        _ => "policy failure",
    };

    private static RuleOutcome Fact(string id, string text) => new(id, text);

    private static RuleOutcome Raise(string id, string text, string label, string? qualifier = null, bool configurationDependent = false) =>
        new(id, text, RaisesError: true, ErrorLabel: label, Qualifier: qualifier, ConfigurationDependent: configurationDependent);
}
