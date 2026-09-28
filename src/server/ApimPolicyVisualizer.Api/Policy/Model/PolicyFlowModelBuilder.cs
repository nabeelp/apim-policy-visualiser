using ApimPolicyVisualizer.Api.Policy.Expressions;
using ApimPolicyVisualizer.Api.Policy.Source;

namespace ApimPolicyVisualizer.Api.Policy.Model;

public interface IPolicyFlowModelBuilder
{
    EffectivePolicyFlowModel Build(PolicySourceDocument document, FragmentRegionSet regions, string scopeId, string scopeKind);
}

/// <summary>
/// Builds the schemaVersion 2 execution model (SPF-4..SPF-6) from the source-mapped tree, fragment regions and syntax-only
/// expression analysis. Control flow is connected with the open-exits algorithm: every builder returns its entry leaf and
/// the open exits that must be joined to whatever follows; terminals and raises-error exits are closed.
/// IDs derive from structural paths, so identical input always yields identical IDs.
/// </summary>
public sealed class PolicyFlowModelBuilder(
    IPolicyExpressionAnalyzer analyzer,
    ApimSemanticRules rules,
    PolicyStateAnalyzer stateAnalyzer) : IPolicyFlowModelBuilder
{
    public static readonly IReadOnlyList<string> StageNames = ["inbound", "backend", "outbound", "on-error"];

    private IPolicyExpressionAnalyzer Analyzer { get; } = analyzer;

    private ApimSemanticRules Rules { get; } = rules;

    private PolicyStateAnalyzer StateAnalyzer { get; } = stateAnalyzer;

    public static PolicyFlowModelBuilder CreateDefault() =>
        new(new PolicyExpressionAnalyzer(), new ApimSemanticRules(), new PolicyStateAnalyzer());

    /// <summary>Runs the full pipeline (loader, fragment regions, analysis, model) over <paramref name="policyXml"/>.</summary>
    public EffectivePolicyFlowModel BuildFromXml(string policyXml, string scopeId = "fixture", string scopeKind = "api")
    {
        var document = new PolicySourceLoader().Load(policyXml);
        return Build(document, new FragmentRegionBuilder().Build(document), scopeId, scopeKind);
    }

    public EffectivePolicyFlowModel Build(PolicySourceDocument document, FragmentRegionSet regions, string scopeId, string scopeKind)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(regions);
        return new Run(this, document, regions).Execute(scopeId, scopeKind);
    }

    private sealed record OpenExit(string From, string Kind, string? Label = null, int? Priority = null, FlowCondition? Condition = null);

    /// <summary><see cref="Entry"/> is the first leaf; null means the block is empty and passes control through.</summary>
    private readonly record struct Flow(string? Entry, List<OpenExit> Exits)
    {
        public static Flow Empty => new(null, []);
    }

    private abstract record Item;

    private sealed record ElementItem(SourceElement Element, SourceComment? Comment) : Item;

    private sealed record GroupItem(FragmentRegion Region, List<Item> Items) : Item;

    private sealed class Draft
    {
        public required string Id { get; init; }
        public required string Kind { get; init; }
        public string? Stage { get; init; }
        public Draft? Parent { get; init; }
        public int Order { get; init; }
        public string Label { get; set; } = string.Empty;
        public string LabelProvenance { get; set; } = FlowProvenance.Structural;
        public string? Tag { get; set; }
        public string Category { get; set; } = FlowCategories.Control;
        public bool Observability { get; set; }
        public FragmentOccurrence? Fragment { get; set; }
        public SourceSpan? Span { get; set; }
        public List<FlowAttribute> Attributes { get; } = [];
        public List<FlowExpression> Expressions { get; } = [];
        public List<FlowAttribute> Properties { get; } = [];
        public string? Comment { get; set; }
        public List<string> Badges { get; } = [];
        public List<FlowFact> Facts { get; } = [];
        public SortedSet<string> Codes { get; } = new(CodeComparer.Instance);
        public bool RaisesError { get; set; }
        public IReadOnlyList<string> VariablesRead { get; set; } = [];
        public IReadOnlyList<string> VariablesWritten { get; set; } = [];
        public SourceElement? Source { get; set; }
        public int NextChildOrder { get; set; }

        public void AddBadge(string badge)
        {
            if (!Badges.Contains(badge))
            {
                Badges.Add(badge);
            }
        }
    }

    private sealed class Run(PolicyFlowModelBuilder owner, PolicySourceDocument document, FragmentRegionSet regions)
    {
        private readonly List<Draft> _drafts = [];
        private readonly Dictionary<string, Draft> _byId = new(StringComparer.Ordinal);
        private readonly List<FlowEdge> _edges = [];
        private readonly HashSet<string> _edgeIds = new(StringComparer.Ordinal);
        private readonly List<FlowDiagnostic> _diagnostics = [.. regions.Diagnostics];
        private readonly HashSet<string> _usedTerminals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _fragmentStages = new(StringComparer.Ordinal);
        private readonly Dictionary<SourceElement, List<FragmentRegion>> _regionsByParent = new();
        private int _topOrder;
        private bool _onErrorPresent;
        private string? _corsStepId;

        public EffectivePolicyFlowModel Execute(string scopeId, string scopeKind)
        {
            foreach (var region in regions.Regions)
            {
                if (!_fragmentStages.TryGetValue(region.Name, out var stages))
                {
                    _fragmentStages[region.Name] = stages = new HashSet<string>(StringComparer.Ordinal);
                }

                if (region.Stage is not null)
                {
                    stages.Add(region.Stage);
                }

                if (!_regionsByParent.TryGetValue(region.Parent, out var list))
                {
                    _regionsByParent[region.Parent] = list = [];
                }

                list.Add(region);
            }

            var sections = FindSections();
            _onErrorPresent = sections.ContainsKey("on-error");
            var hasCors = sections.TryGetValue("inbound", out var inboundSection)
                          && inboundSection.DescendantsAndSelf().Any(e => e.Name == "cors");

            var request = Create(FlowElementKinds.Entry, FlowIds.RequestEntry, null, null, "Request");
            request.Facts.Add(RuleFact(ApimSemanticRules.StageSequence));

            Draft? preflight = null;
            if (hasCors)
            {
                preflight = Create(FlowElementKinds.Entry, FlowIds.PreflightEntry, null, null, "CORS preflight request (if no matching OPTIONS operation)");
                preflight.AddBadge(FlowBadges.ConfigurationDependent);
                preflight.Facts.Add(RuleFact(ApimSemanticRules.CorsPreflight));
            }

            var stageDrafts = new Dictionary<string, Draft>(StringComparer.Ordinal);
            foreach (var name in StageNames)
            {
                var stage = Create(FlowElementKinds.Stage, FlowIds.Stage(name), null, null, StageLabel(name));
                if (sections.TryGetValue(name, out var section))
                {
                    stage.Tag = name;
                    stage.Span = section.Span;
                }

                if (name == "on-error")
                {
                    stage.Facts.Add(RuleFact(ApimSemanticRules.OnErrorLane));
                }

                stageDrafts[name] = stage;
            }

            // Normal stages, joined through open exits.
            var pending = new List<OpenExit> { new(FlowIds.RequestEntry, FlowEdgeKinds.Sequence) };
            foreach (var name in StageNames.Take(3))
            {
                if (!sections.TryGetValue(name, out var section))
                {
                    continue;
                }

                var flow = BuildBlock(ItemsOf(section), stageDrafts[name], name, name);
                if (flow.Entry is not null)
                {
                    Connect(pending, flow.Entry);
                    pending = flow.Exits;
                }
            }

            Connect(pending, Use(FlowIds.FinalResponseTerminal));

            foreach (var name in StageNames.Take(3))
            {
                if (sections.ContainsKey(name))
                {
                    AddEdge(FlowIds.Stage(name), ErrorTarget(name), FlowEdgeKinds.StageException, "unhandled execution failure",
                        facts: [RuleFact(ApimSemanticRules.StageException)]);
                }
            }

            if (sections.TryGetValue("on-error", out var onErrorSection))
            {
                var entry = Create(FlowElementKinds.Entry, FlowIds.OnErrorEntry, stageDrafts["on-error"], "on-error", "On-error");
                entry.Facts.Add(RuleFact(ApimSemanticRules.OnErrorLane));
                var flow = BuildBlock(ItemsOf(onErrorSection), stageDrafts["on-error"], "on-error", "on-error");
                var exits = new List<OpenExit> { new(FlowIds.OnErrorEntry, FlowEdgeKinds.Sequence) };
                if (flow.Entry is not null)
                {
                    Connect(exits, flow.Entry);
                    exits = flow.Exits;
                }

                Connect(exits, Use(FlowIds.ErrorResponseTerminal));
            }

            if (preflight is not null && _corsStepId is not null)
            {
                AddEdge(FlowIds.PreflightEntry, _corsStepId, FlowEdgeKinds.Preflight, "preflight request", facts: [RuleFact(ApimSemanticRules.CorsPreflight)]);
                AddEdge(_corsStepId, Use(FlowIds.PreflightResponseTerminal), FlowEdgeKinds.Preflight, "preflight response");
            }

            CreateTerminals();
            ApplyState();
            AggregateExits();
            ValidateIntegrity();

            return new EffectivePolicyFlowModel
            {
                ScopeId = scopeId,
                ScopeKind = scopeKind,
                Source = new FlowSource(document.Text, document.LineCount),
                Stages = StageNames.Select(n => new FlowStage(FlowIds.Stage(n), n, sections.ContainsKey(n))).ToList(),
                Elements = _drafts.Select(ToElement).ToList(),
                Edges = _edges,
                Variables = _variables,
                Diagnostics = _diagnostics,
            };
        }

        private IReadOnlyList<FlowVariable> _variables = [];

        // ---------------------------------------------------------------- structure

        private Dictionary<string, SourceElement> FindSections()
        {
            var sections = new Dictionary<string, SourceElement>(StringComparer.Ordinal);
            foreach (var element in document.Root.Elements)
            {
                if (!StageNames.Contains(element.Name))
                {
                    _diagnostics.Add(new FlowDiagnostic(FlowDiagnosticSeverities.Warning, "unknown-section",
                        $"<{element.Name}> is not a policy section (inbound, backend, outbound, on-error) and is not modelled.", element.Span));
                }
                else if (!sections.TryAdd(element.Name, element))
                {
                    _diagnostics.Add(new FlowDiagnostic(FlowDiagnosticSeverities.Warning, "duplicate-section",
                        $"Duplicate <{element.Name}> section; only the first is modelled.", element.Span));
                }
            }

            foreach (var region in regions.Regions.Where(r => ReferenceEquals(r.Parent, document.Root)))
            {
                _diagnostics.Add(new FlowDiagnostic(FlowDiagnosticSeverities.Warning, "fragment-region-outside-stage",
                    $"Fragment region '{region.OccurrenceId}' is outside any policy section and is not grouped.", region.Span));
            }

            return sections;
        }

        private List<Item> ItemsOf(SourceElement parent) => ItemsIn(parent, 0, parent.Nodes.Count);

        private List<Item> ItemsIn(SourceElement parent, int from, int to)
        {
            var items = new List<Item>();
            for (var i = from; i < to; i++)
            {
                switch (parent.Nodes[i])
                {
                    case SourceComment comment when regions.RegionStartingAt(comment) is { } region && region.EndIndex < to:
                        items.Add(new GroupItem(region, ItemsIn(parent, region.BeginIndex + 1, region.EndIndex)));
                        i = region.EndIndex;
                        break;
                    case SourceElement element:
                        var preceding = i > from && parent.Nodes[i - 1] is SourceComment c && !regions.IsMarker(c) ? c : null;
                        items.Add(new ElementItem(element, preceding));
                        break;
                }
            }

            return items;
        }

        private Flow BuildBlock(List<Item> items, Draft parent, string pathPrefix, string stage)
        {
            string? entry = null;
            var pending = new List<OpenExit>();
            var index = 0;
            foreach (var item in items)
            {
                var flow = item switch
                {
                    ElementItem element => BuildElement(element, parent, $"{pathPrefix}/{index}", stage),
                    GroupItem group => BuildGroup(group, parent, pathPrefix, stage),
                    _ => Flow.Empty,
                };
                index++;

                if (flow.Entry is null)
                {
                    continue;
                }

                if (entry is null)
                {
                    entry = flow.Entry;
                }
                else
                {
                    Connect(pending, flow.Entry);
                }

                pending = flow.Exits;
            }

            return new Flow(entry, pending);
        }

        private Flow BuildElement(ElementItem item, Draft parent, string id, string stage) => item.Element.Name switch
        {
            "choose" => BuildChoose(item, parent, id, stage),
            "retry" => BuildRetry(item, parent, id, stage),
            _ => BuildStep(item, parent, id, stage),
        };

        private Flow BuildGroup(GroupItem item, Draft parent, string pathPrefix, string stage)
        {
            var region = item.Region;
            var id = $"{pathPrefix}/fragment:{region.OccurrenceId}";
            var repeatedAcrossStages = _fragmentStages.TryGetValue(region.Name, out var stages) && stages.Count > 1;
            var group = Create(FlowElementKinds.Group, id, parent, stage,
                repeatedAcrossStages && region.Stage is not null ? $"{region.Label} · {region.Stage}" : region.Label);
            group.LabelProvenance = region.LabelProvenance;
            group.Fragment = new FragmentOccurrence(region.Name, region.OccurrenceId, region.Index, region.Count, region.Description);
            group.Span = region.Span;
            group.Comment = region.Description;
            group.Facts.Add(new FlowFact(
                $"Fragment '{region.Name}' occurrence {region.Index} of {region.Count}, delimited by include-fragment Begin/End marker comments.",
                FlowProvenance.Comment));
            return BuildBlock(item.Items, group, id, stage);
        }

        private Flow BuildStep(ElementItem item, Draft parent, string id, string stage)
        {
            var element = item.Element;
            var tag = element.Name;
            var known = PolicyElementCatalog.TryGetCategory(tag, out var category);
            var isWrapper = PolicyElementCatalog.PolicyWrappers.Contains(tag);

            var step = Create(known ? FlowElementKinds.Step : FlowElementKinds.Opaque, id, parent, stage, PolicyElementCatalog.Label(element));
            step.Tag = tag;
            step.Category = known ? category : FlowCategories.Unknown;
            step.Observability = ApimSemanticRules.IsObservability(tag);
            step.Span = element.Span;
            step.Source = element;
            step.Comment = CommentText(item.Comment);
            step.Attributes.AddRange(element.Attributes.Select(a => new FlowAttribute(a.Name, a.Value, a.IsExpression)));
            FoldProperties(element, string.Empty, step.Properties, includeChildren: !isWrapper);
            CollectExpressions(element, string.Empty, step, includeChildren: !isWrapper);

            if (!known)
            {
                step.AddBadge(FlowBadges.Opaque);
                AddDiagnostic(step, FlowDiagnosticSeverities.Warning, "unresolved-control-effect",
                    $"<{tag}> is not a recognised policy; its effect on control flow is not modelled. It is shown as an opaque step.");
            }

            foreach (var outcome in owner.Rules.Evaluate(element))
            {
                var fact = new FlowFact(outcome.Fact, FlowProvenance.ApimRule, outcome.RuleId);
                step.Facts.Add(fact);
                if (outcome.ConfigurationDependent)
                {
                    step.AddBadge(FlowBadges.ConfigurationDependent);
                }

                if (outcome.RaisesError)
                {
                    step.RaisesError = true;
                    var facts = new List<FlowFact> { fact };
                    if (stage == "on-error")
                    {
                        facts.Add(RuleFact(ApimSemanticRules.OnErrorInternalFailure));
                    }

                    AddEdge(id, ErrorTarget(stage), FlowEdgeKinds.RaisesError, outcome.ErrorLabel, facts: facts, qualifier: outcome.Qualifier);
                }
            }

            if (tag == "cors" && stage == "inbound")
            {
                _corsStepId ??= id;
            }

            if (PolicyElementCatalog.Terminating.Contains(tag))
            {
                var (code, label, detail) = ResponseStatus(element);
                step.Label = label;
                step.Codes.Add(code);
                step.Facts.Add(new FlowFact(detail, FlowProvenance.Structural));
                step.Facts.Add(RuleFact(tag == "mock-response" ? ApimSemanticRules.MockResponseTerminates : ApimSemanticRules.ReturnResponseTerminates));
                AddEdge(id, Use(FlowIds.ExplicitResponseTerminal), FlowEdgeKinds.ExplicitResponse, code == "variable" ? "status from variable" : code);
                return new Flow(id, []);
            }

            if (isWrapper)
            {
                step.Facts.Add(new FlowFact(
                    tag == "wait"
                        ? "Child policies run concurrently; they are shown in source order after this step."
                        : "Child policies run inside this policy; they are shown in source order after this step.",
                    FlowProvenance.Structural));
                var children = BuildBlock(ItemsOf(element), parent, id, stage);
                if (children.Entry is not null)
                {
                    Connect([new OpenExit(id, FlowEdgeKinds.Sequence)], children.Entry);
                    return new Flow(id, children.Exits);
                }
            }

            return new Flow(id, [new OpenExit(id, FlowEdgeKinds.Sequence)]);
        }

        private Flow BuildChoose(ElementItem item, Draft parent, string id, string stage)
        {
            var element = item.Element;
            var whens = element.Elements.Where(e => e.Name == "when").ToList();
            var otherwise = element.FirstElement("otherwise");

            var choose = Create(FlowElementKinds.Choose, id, parent, stage, "Choose");
            choose.Tag = "choose";
            choose.Span = element.Span;
            choose.Source = element;
            choose.Comment = CommentText(item.Comment);

            var decision = Create(FlowElementKinds.Decision, $"{id}/decision", choose, stage,
                $"First match of {whens.Count} condition{(whens.Count == 1 ? string.Empty : "s")}{(otherwise is null ? ", else continue" : ", else otherwise")}");
            decision.Span = element.Span;
            decision.Facts.Add(RuleFact(ApimSemanticRules.ChooseFirstMatch));
            decision.Facts.Add(new FlowFact(
                $"{whens.Count} when clause{(whens.Count == 1 ? string.Empty : "s")} in priority order; {(otherwise is null ? "no otherwise (no match continues)" : "otherwise present")}.",
                FlowProvenance.Structural));

            foreach (var child in element.Elements.Where(e => e.Name is not ("when" or "otherwise")))
            {
                AddDiagnostic(choose, FlowDiagnosticSeverities.Warning, "unexpected-choose-child",
                    $"<{child.Name}> inside <choose> is not a when/otherwise clause and is not modelled.", child.Span);
            }

            if (_regionsByParent.TryGetValue(element, out var misplaced))
            {
                foreach (var region in misplaced)
                {
                    AddDiagnostic(choose, FlowDiagnosticSeverities.Warning, "fragment-region-unsupported-position",
                        $"Fragment region '{region.OccurrenceId}' spans choose clauses and is not grouped.", region.Span);
                }
            }

            var mergeInputs = new List<OpenExit>();
            string? firstSummary = null;
            var priority = 0;
            foreach (var when in whens)
            {
                priority++;
                var conditionAttribute = when.GetAttribute("condition");
                var text = conditionAttribute?.Value ?? string.Empty;
                var analysis = Analyze(text, conditionAttribute?.IsExpression ?? false);
                var expression = new FlowExpression($"when[{priority}]/@condition", text, conditionAttribute?.ValueSpan, analysis);
                decision.Expressions.Add(expression);
                ReportExpression(decision, expression);
                var summary = analysis.Summary ?? (conditionAttribute is { IsExpression: false } ? text : null);
                if (priority == 1)
                {
                    firstSummary = summary;
                }

                var condition = new FlowCondition(text, summary, analysis.Summary is null ? FlowProvenance.Structural : FlowProvenance.Inferred);
                var label = PolicyElementCatalog.Truncate(summary ?? text, 60);
                var body = BuildBlock(ItemsOf(when), choose, $"{id}/when{priority}", stage);
                if (body.Entry is not null)
                {
                    AddEdge(decision.Id, body.Entry, FlowEdgeKinds.Branch, label, priority, condition);
                    mergeInputs.AddRange(body.Exits);
                }
                else
                {
                    mergeInputs.Add(new OpenExit(decision.Id, FlowEdgeKinds.Bypass, $"{label} (empty branch)", priority, condition));
                }
            }

            if (otherwise is not null)
            {
                var body = BuildBlock(ItemsOf(otherwise), choose, $"{id}/otherwise", stage);
                if (body.Entry is not null)
                {
                    AddEdge(decision.Id, body.Entry, FlowEdgeKinds.Otherwise, "otherwise");
                    mergeInputs.AddRange(body.Exits);
                }
                else
                {
                    mergeInputs.Add(new OpenExit(decision.Id, FlowEdgeKinds.Bypass, "otherwise (empty branch)"));
                }
            }
            else
            {
                mergeInputs.Add(new OpenExit(decision.Id, FlowEdgeKinds.NoMatch, "no match: continue"));
            }

            var branchCount = whens.Count + (otherwise is null ? 0 : 1);
            choose.Label = PolicyElementCatalog.Truncate(
                firstSummary is not null ? $"Choose: {firstSummary}" : $"Choose: {branchCount} branch{(branchCount == 1 ? string.Empty : "es")}", 80);

            if (mergeInputs.Count == 0)
            {
                return new Flow(decision.Id, []);
            }

            var merge = Create(FlowElementKinds.Merge, $"{id}/merge", choose, stage, "Merge");
            merge.Facts.Add(new FlowFact("Every non-terminating branch rejoins here before the next policy.", FlowProvenance.Structural));
            Connect(mergeInputs, merge.Id, sequenceAs: FlowEdgeKinds.Merge);
            return new Flow(decision.Id, [new OpenExit(merge.Id, FlowEdgeKinds.Sequence)]);
        }

        private Flow BuildRetry(ElementItem item, Draft parent, string id, string stage)
        {
            var element = item.Element;
            var loop = Create(FlowElementKinds.Loop, id, parent, stage, "Retry");
            loop.Tag = "retry";
            loop.Span = element.Span;
            loop.Source = element;
            loop.Comment = CommentText(item.Comment);
            loop.Attributes.AddRange(element.Attributes.Select(a => new FlowAttribute(a.Name, a.Value, a.IsExpression)));

            var conditionAttribute = element.GetAttribute("condition");
            var text = conditionAttribute?.Value ?? string.Empty;
            var analysis = Analyze(text, conditionAttribute?.IsExpression ?? false);
            var summary = analysis.Summary ?? (conditionAttribute is { IsExpression: false } ? text : null);
            loop.Label = PolicyElementCatalog.Truncate(summary is null ? "Retry while condition holds" : $"Retry while {summary}", 80);

            foreach (var name in new[] { "condition", "count", "interval", "max-interval", "delta", "first-fast-retry" })
            {
                if (element.AttributeValue(name) is { } value)
                {
                    loop.Facts.Add(new FlowFact($"{name} = {value}", FlowProvenance.Structural));
                }
            }

            if (analysis.Summary is not null)
            {
                loop.Facts.Add(new FlowFact($"Repeats while {analysis.Summary} (and the retry budget remains).", FlowProvenance.Inferred));
            }

            if (int.TryParse(element.AttributeValue("count"), out var count))
            {
                loop.Facts.Add(new FlowFact($"At most {count} retr{(count == 1 ? "y" : "ies")} after the first attempt ({count + 1} attempts in total).", FlowProvenance.Inferred));
            }

            loop.Facts.Add(RuleFact(ApimSemanticRules.RetryCount));
            loop.Facts.Add(RuleFact(ApimSemanticRules.RetryFirstAttempt));

            var body = BuildBlock(ItemsOf(element), loop, id, stage);

            var test = Create(FlowElementKinds.LoopTest, $"{id}/test", loop, stage,
                PolicyElementCatalog.Truncate(summary is null ? "Retry if condition is true and budget remains" : $"Retry if {summary} and budget remains", 80));
            test.Span = conditionAttribute?.Span ?? element.Span;
            var expression = new FlowExpression("@condition", text, conditionAttribute?.ValueSpan, analysis);
            test.Expressions.Add(expression);
            ReportExpression(test, expression);
            test.Facts.Add(RuleFact(ApimSemanticRules.RetryLoopTest));

            if (body.Entry is null)
            {
                AddDiagnostic(loop, FlowDiagnosticSeverities.Info, "empty-retry", "The retry body is empty; nothing repeats.");
                return new Flow(test.Id, [new OpenExit(test.Id, FlowEdgeKinds.LoopExit, "retry finished")]);
            }

            Connect(body.Exits, test.Id);
            AddEdge(test.Id, body.Entry, FlowEdgeKinds.LoopBack, "retry: condition true and budget remains");
            return new Flow(body.Entry, [new OpenExit(test.Id, FlowEdgeKinds.LoopExit, "retry finished")]);
        }

        private static (string Code, string Label, string Detail) ResponseStatus(SourceElement element)
        {
            if (element.Name == "mock-response")
            {
                var mockCode = element.AttributeValue("status-code") ?? "200";
                return (mockCode, $"Mock response {mockCode}", $"Mocked response with status {mockCode}.");
            }

            if (element.FirstElement("set-status") is { } setStatus)
            {
                var code = setStatus.GetAttribute("code");
                var reason = setStatus.AttributeValue("reason");
                if (code is null || code.IsExpression)
                {
                    return ("expression", "Return status from expression", "Status code is computed by an expression in set-status.");
                }

                var text = string.IsNullOrWhiteSpace(reason) ? code.Value : $"{code.Value} {reason}";
                return (code.Value.Trim(), $"Return {text}", $"Status {text} from set-status.");
            }

            if (element.AttributeValue("response-variable-name") is { Length: > 0 } variable)
            {
                return ("variable", $"Return status from variable {variable}", $"Response (and status) taken from variable {variable}.");
            }

            return ("200", "Return 200 OK (no body)", "No set-status or response-variable-name: APIM returns 200 OK with no body.");
        }

        // ---------------------------------------------------------------- expressions and properties

        private ExpressionAnalysis Analyze(string text, bool isExpression) =>
            isExpression ? owner.Analyzer.Analyze(text) : new ExpressionAnalysis(false, null, [], [], [], [], false, null, null, []);

        private void CollectExpressions(SourceElement element, string path, Draft draft, bool includeChildren)
        {
            foreach (var attribute in element.Attributes)
            {
                if (attribute.IsExpression)
                {
                    var expression = new FlowExpression(Combine(path, "@" + attribute.Name), attribute.Value, attribute.ValueSpan, owner.Analyzer.Analyze(attribute.Value));
                    draft.Expressions.Add(expression);
                    ReportExpression(draft, expression);
                }
            }

            foreach (var node in element.Nodes)
            {
                switch (node)
                {
                    case SourceText { IsExpression: true } text:
                        var expression = new FlowExpression(path.Length == 0 ? "text()" : path, text.Value, text.Span, owner.Analyzer.Analyze(text.Value));
                        draft.Expressions.Add(expression);
                        ReportExpression(draft, expression);
                        break;
                    case SourceElement child when includeChildren:
                        CollectExpressions(child, Combine(path, child.Name), draft, includeChildren: true);
                        break;
                }
            }
        }

        private void ReportExpression(Draft draft, FlowExpression expression)
        {
            if (expression.Analysis.Diagnostics.FirstOrDefault(d => d.StartsWith("C# syntax error", StringComparison.Ordinal)) is { } message)
            {
                AddDiagnostic(draft, FlowDiagnosticSeverities.Warning, "expression-parse-error",
                    $"Expression at {expression.Location} could not be parsed and is shown verbatim: {message}", expression.Span);
            }

            // A C# return / catch is local to the expression: it yields a value to this policy and never ends the
            // pipeline or transfers to On-error (SPF-FR-10).
            if (expression.Analysis.ReturnPaths.Count > 0)
            {
                draft.Facts.Add(new FlowFact(
                    $"{expression.Location}: {expression.Analysis.ReturnPaths.Count} ordered return path(s) yield the value used by this policy; a C# return is expression-local, not a pipeline response.",
                    FlowProvenance.Inferred));
            }

            if (expression.Analysis.HasLocalCatch)
            {
                draft.Facts.Add(new FlowFact(
                    $"{expression.Location}: exceptions are caught inside the expression{(expression.Analysis.CatchFallback is { } fallback ? $" (fallback {fallback})" : string.Empty)}; this is not an On-error transfer.",
                    FlowProvenance.Inferred));
            }
        }

        private static void FoldProperties(SourceElement element, string path, List<FlowAttribute> properties, bool includeChildren)
        {
            if (path.Length == 0 && element.Text is { } ownText && ownText.Trim().Length > 0)
            {
                properties.Add(new FlowAttribute("text()", ownText.Trim(), SourcePolicyText(ownText)));
            }

            if (!includeChildren)
            {
                return;
            }

            foreach (var child in element.Elements)
            {
                var childPath = Combine(path, child.Name);
                foreach (var attribute in child.Attributes)
                {
                    properties.Add(new FlowAttribute($"{childPath}/@{attribute.Name}", attribute.Value, attribute.IsExpression));
                }

                if (child.Text is { } text && text.Trim().Length > 0)
                {
                    properties.Add(new FlowAttribute(childPath, text.Trim(), SourcePolicyText(text)));
                }

                FoldProperties(child, childPath, properties, includeChildren: true);
            }
        }

        private static bool SourcePolicyText(string text) => PolicySourceLoader.IsExpression(text);

        private static string Combine(string path, string segment) => path.Length == 0 ? segment : $"{path}/{segment}";

        private static string? CommentText(SourceComment? comment) => comment is null ? null : FragmentRegionBuilder.Dedent(comment.Text);

        // ---------------------------------------------------------------- stages, terminals, state

        private string ErrorTarget(string stage) =>
            Use(stage == "on-error" || !_onErrorPresent ? FlowIds.GatewayDefaultErrorTerminal : FlowIds.OnErrorEntry);

        private string Use(string id)
        {
            if (id.StartsWith("terminal:", StringComparison.Ordinal))
            {
                _usedTerminals.Add(id);
            }

            return id;
        }

        private void CreateTerminals()
        {
            (string Id, string Label, string? Rule)[] terminals =
            [
                (FlowIds.ExplicitResponseTerminal, "Explicit response to caller", ApimSemanticRules.ReturnResponseTerminates),
                (FlowIds.FinalResponseTerminal, "Final backend response to caller", ApimSemanticRules.OutboundFinalResponse),
                (FlowIds.ErrorResponseTerminal, "Error response to caller", ApimSemanticRules.OnErrorNoResume),
                (FlowIds.GatewayDefaultErrorTerminal, "Gateway default error response", ApimSemanticRules.GatewayDefaultError),
                (FlowIds.PreflightResponseTerminal, "Preflight response to caller", ApimSemanticRules.CorsPreflight),
            ];

            foreach (var (id, label, rule) in terminals)
            {
                if (_usedTerminals.Contains(id))
                {
                    var terminal = Create(FlowElementKinds.Terminal, id, null, null, label);
                    terminal.Category = FlowCategories.Response;
                    if (rule is not null)
                    {
                        terminal.Facts.Add(RuleFact(rule));
                    }
                }
            }
        }

        private void ApplyState()
        {
            var inputs = _drafts
                .Where(d => d.Source is not null || d.Expressions.Count > 0)
                .Select(d => new StateElementInput(d.Id, d.Kind, d.Kind is FlowElementKinds.Step or FlowElementKinds.Opaque ? d.Source : null, d.Expressions,
                    d.Kind is FlowElementKinds.Decision or FlowElementKinds.LoopTest))
                .ToList();

            var result = owner.StateAnalyzer.Analyze(inputs);
            _variables = result.Variables;
            foreach (var (id, state) in result.Elements)
            {
                var draft = _byId[id];
                draft.VariablesRead = state.VariablesRead;
                draft.VariablesWritten = state.VariablesWritten;
                draft.Facts.AddRange(state.Facts);
                foreach (var badge in state.Badges)
                {
                    draft.AddBadge(badge);
                    if (draft.Kind == FlowElementKinds.Decision && draft.Parent is { } choose)
                    {
                        choose.AddBadge(badge);
                    }
                }
            }

            foreach (var edge in result.DataDependencyEdges)
            {
                if (_edgeIds.Add(edge.Id))
                {
                    _edges.Add(edge);
                }
            }
        }

        private void AggregateExits()
        {
            foreach (var edge in _edges)
            {
                if (edge.Kind == FlowEdgeKinds.RaisesError && _byId.TryGetValue(edge.From, out var from))
                {
                    from.RaisesError = true;
                }
            }

            // Children are always created after their parent, so a reverse pass sees every child before its parent.
            for (var i = _drafts.Count - 1; i >= 0; i--)
            {
                var draft = _drafts[i];
                if (draft.Parent is { } parent)
                {
                    parent.Codes.UnionWith(draft.Codes);
                    parent.RaisesError |= draft.RaisesError;
                }
            }
        }

        private void ValidateIntegrity()
        {
            foreach (var edge in _edges)
            {
                if (!_byId.ContainsKey(edge.From) || !_byId.ContainsKey(edge.To))
                {
                    _diagnostics.Add(new FlowDiagnostic(FlowDiagnosticSeverities.Error, "model-integrity",
                        $"Edge {edge.Id} references an element that does not exist."));
                }
            }
        }

        // ---------------------------------------------------------------- primitives

        private Draft Create(string kind, string id, Draft? parent, string? stage, string label)
        {
            if (_byId.ContainsKey(id))
            {
                _diagnostics.Add(new FlowDiagnostic(FlowDiagnosticSeverities.Error, "model-integrity", $"Duplicate element ID {id}."));
                id = $"{id}~{_drafts.Count}";
            }

            var draft = new Draft
            {
                Id = id,
                Kind = kind,
                Stage = stage,
                Parent = parent,
                Order = parent is null ? _topOrder++ : parent.NextChildOrder++,
                Label = label,
            };
            _drafts.Add(draft);
            _byId[id] = draft;
            return draft;
        }

        private void Connect(List<OpenExit> exits, string to, string? sequenceAs = null)
        {
            foreach (var exit in exits)
            {
                var kind = sequenceAs is not null && exit.Kind == FlowEdgeKinds.Sequence ? sequenceAs : exit.Kind;
                AddEdge(exit.From, to, kind, exit.Label, exit.Priority, exit.Condition);
            }
        }

        private void AddEdge(
            string from,
            string to,
            string kind,
            string? label = null,
            int? priority = null,
            FlowCondition? condition = null,
            IReadOnlyList<FlowFact>? facts = null,
            string? qualifier = null)
        {
            var id = $"e:{from}->{to}:{kind}";
            if (priority is not null)
            {
                id += $":{priority}";
            }

            if (qualifier is not null)
            {
                id += $":{qualifier}";
            }

            if (_edgeIds.Add(id))
            {
                _edges.Add(new FlowEdge(id, from, to, kind, label, priority, condition, facts));
            }
        }

        private void AddDiagnostic(Draft draft, string severity, string code, string message, SourceSpan? span = null)
        {
            _diagnostics.Add(new FlowDiagnostic(severity, code, message, span ?? draft.Span, draft.Id));
            draft.AddBadge(FlowBadges.HasDiagnostics);
        }

        private static FlowFact RuleFact(string ruleId) => new(ApimSemanticRules.Describe(ruleId), FlowProvenance.ApimRule, ruleId);

        private static string StageLabel(string name) => name switch
        {
            "inbound" => "Inbound",
            "backend" => "Backend",
            "outbound" => "Outbound",
            _ => "On-error",
        };

        private static FlowElement ToElement(Draft draft) => new()
        {
            Id = draft.Id,
            Kind = draft.Kind,
            Stage = draft.Stage,
            ParentId = draft.Parent?.Id,
            Order = draft.Order,
            Label = draft.Label,
            LabelProvenance = draft.LabelProvenance,
            Tag = draft.Tag,
            Category = draft.Category,
            Observability = draft.Observability,
            Fragment = draft.Fragment,
            Span = draft.Span,
            Attributes = draft.Attributes,
            Expressions = draft.Expressions,
            Properties = draft.Properties,
            Comment = draft.Comment,
            Badges = draft.Badges,
            Facts = draft.Facts,
            Exits = draft.Codes.Count == 0 && !draft.RaisesError ? FlowExits.None : new FlowExits(draft.Codes.ToList(), draft.RaisesError),
            VariablesRead = draft.VariablesRead,
            VariablesWritten = draft.VariablesWritten,
        };
    }

    /// <summary>Numeric status codes ascending, then non-numeric tokens (e.g. "variable") ordinally.</summary>
    private sealed class CodeComparer : IComparer<string>
    {
        public static readonly CodeComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var xNumeric = int.TryParse(x, out var xi);
            var yNumeric = int.TryParse(y, out var yi);
            return (xNumeric, yNumeric) switch
            {
                (true, true) => xi.CompareTo(yi),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(x, y),
            };
        }
    }
}

/// <summary>Traversal helpers over the model. Control-flow helpers never follow non-control (data-dependency) edges.</summary>
public static class PolicyFlowModelQueries
{
    public static bool IsControlEdge(FlowEdge edge) => !FlowEdgeKinds.NonControl.Contains(edge.Kind);

    /// <summary>Element IDs reachable from <paramref name="startId"/> over control edges only (never data-dependency).</summary>
    public static IReadOnlySet<string> ReachableByControlFlow(EffectivePolicyFlowModel model, string startId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var successors = model.Edges.Where(IsControlEdge).ToLookup(e => e.From, e => e.To, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { startId };
        var queue = new Queue<string>();
        queue.Enqueue(startId);
        while (queue.Count > 0)
        {
            foreach (var next in successors[queue.Dequeue()])
            {
                if (seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return seen;
    }

    /// <summary>Control edges only, e.g. for counting; data-dependency edges are excluded.</summary>
    public static IEnumerable<FlowEdge> ControlEdges(EffectivePolicyFlowModel model) => model.Edges.Where(IsControlEdge);
}
