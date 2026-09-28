namespace ApimPolicyVisualizer.Api.Policy.Model;

// schemaVersion 2 contract for GET /api/policy/effective-flow (SPF-FR-12).
// The TypeScript mirror is src/web/src/flow/model.ts; keep the two in sync.

public static class FlowElementKinds
{
    public const string Stage = "stage";
    public const string Group = "group";
    public const string Choose = "choose";
    public const string Decision = "decision";
    public const string Merge = "merge";
    public const string Loop = "loop";
    public const string LoopTest = "loop-test";
    public const string Step = "step";
    public const string Terminal = "terminal";
    public const string Entry = "entry";
    public const string Opaque = "opaque";

    public static readonly IReadOnlySet<string> Containers = new HashSet<string>(StringComparer.Ordinal)
    {
        Stage, Group, Choose, Loop,
    };
}

public static class FlowEdgeKinds
{
    public const string Sequence = "sequence";
    public const string Branch = "branch";
    public const string Otherwise = "otherwise";
    public const string NoMatch = "no-match";
    public const string Bypass = "bypass";
    public const string Merge = "merge";
    public const string LoopBack = "loop-back";
    public const string LoopExit = "loop-exit";
    public const string ExplicitResponse = "explicit-response";
    public const string RaisesError = "raises-error";
    public const string StageException = "stage-exception";
    public const string Preflight = "preflight";
    public const string DataDependency = "data-dependency";

    public static readonly IReadOnlySet<string> NonControl = new HashSet<string>(StringComparer.Ordinal) { DataDependency };
}

public static class FlowCategories
{
    public const string BackendCall = "backend-call";
    public const string Routing = "routing";
    public const string Authentication = "authentication";
    public const string Validation = "validation";
    public const string Transformation = "transformation";
    public const string State = "state";
    public const string Cache = "cache";
    public const string Observability = "observability";
    public const string Cors = "cors";
    public const string Inherited = "inherited";
    public const string Control = "control";
    public const string Response = "response";
    public const string Unknown = "unknown";
}

public static class FlowProvenance
{
    public const string Structural = "structural";
    public const string ApimRule = "apim-rule";
    public const string Inferred = "inferred";
    public const string Comment = "comment";
    public const string FragmentName = "fragment-name";
}

public static class FlowBadges
{
    public const string ConfigurationDependent = "configuration-dependent";
    public const string VariableNotAssigned = "variable-not-assigned";
    public const string Opaque = "opaque";
    public const string HasDiagnostics = "has-diagnostics";
}

public static class FlowDependencyClasses
{
    public const string Configuration = "configuration";
    public const string Runtime = "runtime";
}

public static class FlowIds
{
    public const string RequestEntry = "entry:request";
    public const string PreflightEntry = "entry:preflight";
    public const string OnErrorEntry = "entry:on-error";
    public const string ExplicitResponseTerminal = "terminal:explicit-response";
    public const string FinalResponseTerminal = "terminal:final-response";
    public const string ErrorResponseTerminal = "terminal:error-response";
    public const string GatewayDefaultErrorTerminal = "terminal:gateway-default-error";
    public const string PreflightResponseTerminal = "terminal:preflight-response";

    public static string Stage(string stageName) => $"stage:{stageName}";
}

/// <summary>1-based, inclusive source positions in <see cref="FlowSource.Text"/>.</summary>
public sealed record SourceSpan(int StartLine, int StartColumn, int EndLine, int EndColumn);

public sealed record FlowSource(string Text, int LineCount);

public sealed record FlowStage(string Id, string Name, bool Present);

public sealed record FragmentOccurrence(string Name, string OccurrenceId, int Index, int Count, string? Description);

public sealed record FlowAttribute(string Name, string Value, bool IsExpression);

/// <param name="ValueKind">literal | variable | expression.</param>
public sealed record ReturnPath(int Order, string? Condition, string? ConditionSummary, string ValueKind, string ValueText);

public sealed record ExpressionAnalysis(
    bool Opaque,
    string? Summary,
    IReadOnlyList<string> VariablesRead,
    IReadOnlyList<string> NamedValues,
    IReadOnlyList<string> ContextMembers,
    IReadOnlyList<ReturnPath> ReturnPaths,
    bool HasLocalCatch,
    string? CatchFallback,
    string? KeyPrefix,
    IReadOnlyList<string> Diagnostics)
{
    public static ExpressionAnalysis OpaqueResult(params string[] diagnostics) =>
        new(true, null, [], [], [], [], false, null, null, diagnostics);
}

/// <param name="Location">Relative location within the element, e.g. <c>@condition</c>, <c>value</c>, <c>text()</c>.</param>
public sealed record FlowExpression(string Location, string Text, SourceSpan? Span, ExpressionAnalysis Analysis);

public sealed record FlowFact(string Text, string Provenance, string? RuleId = null);

/// <summary>Outcomes aggregated over the element and all of its descendants.</summary>
/// <param name="ExplicitResponseCodes">Distinct status codes as strings ("403", "200", or "variable"), ascending.</param>
public sealed record FlowExits(IReadOnlyList<string> ExplicitResponseCodes, bool RaisesError)
{
    public static readonly FlowExits None = new([], false);
}

public sealed class FlowElement
{
    public required string Id { get; init; }
    public required string Kind { get; init; }

    /// <summary>inbound | backend | outbound | on-error; null for entries/terminals outside a stage.</summary>
    public string? Stage { get; init; }

    public string? ParentId { get; init; }

    /// <summary>Zero-based position among siblings sharing <see cref="ParentId"/>, in source order.</summary>
    public required int Order { get; init; }

    public required string Label { get; init; }
    public string LabelProvenance { get; init; } = FlowProvenance.Structural;

    /// <summary>Policy element name, e.g. <c>set-variable</c>; null for synthesized elements.</summary>
    public string? Tag { get; init; }

    public string Category { get; init; } = FlowCategories.Control;
    public bool Observability { get; init; }
    public FragmentOccurrence? Fragment { get; init; }
    public SourceSpan? Span { get; init; }
    public IReadOnlyList<FlowAttribute> Attributes { get; init; } = [];
    public IReadOnlyList<FlowExpression> Expressions { get; init; } = [];

    /// <summary>Folded configuration children, e.g. <c>value</c>, <c>audiences/audience</c>, rendered as properties.</summary>
    public IReadOnlyList<FlowAttribute> Properties { get; init; } = [];

    public string? Comment { get; init; }
    public IReadOnlyList<string> Badges { get; init; } = [];
    public IReadOnlyList<FlowFact> Facts { get; init; } = [];
    public FlowExits Exits { get; init; } = FlowExits.None;
    public IReadOnlyList<string> VariablesRead { get; init; } = [];
    public IReadOnlyList<string> VariablesWritten { get; init; } = [];
}

public sealed record FlowCondition(string Text, string? Summary, string Provenance);

public sealed record FlowEdge(
    string Id,
    string From,
    string To,
    string Kind,
    string? Label = null,
    int? Priority = null,
    FlowCondition? Condition = null,
    IReadOnlyList<FlowFact>? Facts = null);

public sealed record VariableWriter(string ElementId, bool Literal, IReadOnlyList<string> DependencyClasses);

/// <param name="DependencyClasses">Union of <see cref="FlowDependencyClasses"/> over all writers; configuration when there is no writer.</param>
public sealed record FlowVariable(
    string Name,
    IReadOnlyList<VariableWriter> Writers,
    IReadOnlyList<string> Readers,
    IReadOnlyList<string> DependencyClasses);

public static class FlowDiagnosticSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
}

public sealed record FlowDiagnostic(string Severity, string Code, string Message, SourceSpan? Span = null, string? ElementId = null);

public sealed class EffectivePolicyFlowModel
{
    public int SchemaVersion { get; init; } = 2;
    public required string ScopeId { get; init; }
    public required string ScopeKind { get; init; }
    public required FlowSource Source { get; init; }
    public required IReadOnlyList<FlowStage> Stages { get; init; }
    public required IReadOnlyList<FlowElement> Elements { get; init; }
    public required IReadOnlyList<FlowEdge> Edges { get; init; }
    public required IReadOnlyList<FlowVariable> Variables { get; init; }
    public required IReadOnlyList<FlowDiagnostic> Diagnostics { get; init; }
}
