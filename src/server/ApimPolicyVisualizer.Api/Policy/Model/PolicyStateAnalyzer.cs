using ApimPolicyVisualizer.Api.Policy.Source;

namespace ApimPolicyVisualizer.Api.Policy.Model;

/// <summary>One model element as seen by the state analyzer.</summary>
/// <param name="Source">The policy element for steps (used to discover variable writers and cache accesses); null for synthesized elements.</param>
/// <param name="IsDecision">True for decision and loop-test elements, whose inputs are classified into dependency classes.</param>
public sealed record StateElementInput(string Id, string Kind, SourceElement? Source, IReadOnlyList<FlowExpression> Expressions, bool IsDecision);

/// <summary>State facts for one element.</summary>
public sealed record ElementStateResult(
    IReadOnlyList<string> VariablesRead,
    IReadOnlyList<string> VariablesWritten,
    IReadOnlyList<string> Badges,
    IReadOnlyList<FlowFact> Facts);

public sealed record StateAnalysisResult(
    IReadOnlyList<FlowVariable> Variables,
    IReadOnlyDictionary<string, ElementStateResult> Elements,
    IReadOnlyList<FlowEdge> DataDependencyEdges);

/// <summary>
/// Builds the variable index, classifies decision inputs into configuration/runtime dependency classes propagated
/// through variable writers, and links cache-store-value → cache-lookup-value data dependencies (SPF-FR-11).
/// It never claims a branch is unreachable; configuration-dependent decisions are only badged.
/// </summary>
public sealed class PolicyStateAnalyzer
{
    public const string DefaultCachingType = "prefer-external";

    private static readonly HashSet<string> ConfigurationRoots = new(StringComparer.Ordinal) { "Api", "Deployment", "Product" };

    private sealed record Write(string Variable, string ElementId, bool Literal, IReadOnlyList<string> DirectClasses, IReadOnlyList<string> ReadsVariables);

    public StateAnalysisResult Analyze(IReadOnlyList<StateElementInput> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var writes = new List<Write>();
        var readsByElement = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var writesByElement = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var readers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var element in elements)
        {
            var reads = new List<string>();
            foreach (var expression in element.Expressions)
            {
                foreach (var variable in expression.Analysis.VariablesRead)
                {
                    AddDistinct(reads, variable);
                }
            }

            if (element.Source is { Name: "return-response" } returnResponse && returnResponse.AttributeValue("response-variable-name") is { Length: > 0 } responseVariable)
            {
                AddDistinct(reads, responseVariable);
            }

            foreach (var variable in reads)
            {
                AddReader(readers, order, variable, element.Id);
            }

            readsByElement[element.Id] = reads;

            if (element.Source is not null)
            {
                var written = new List<string>();
                foreach (var write in DiscoverWrites(element))
                {
                    writes.Add(write);
                    AddDistinct(written, write.Variable);
                    if (!readers.ContainsKey(write.Variable))
                    {
                        readers[write.Variable] = [];
                        order.Add(write.Variable);
                    }
                }

                writesByElement[element.Id] = written;
            }
        }

        var classes = PropagateClasses(writes, readers.Keys);
        var writersByVariable = writes.GroupBy(w => w.Variable, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var variables = readers.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(name =>
        {
            var variableWriters = writersByVariable.GetValueOrDefault(name) ?? [];
            return new FlowVariable(
                name,
                variableWriters.Select(w => new VariableWriter(w.ElementId, w.Literal, WriterClasses(w, classes))).ToList(),
                readers[name],
                Sorted(classes[name]));
        }).ToList();

        var results = new Dictionary<string, ElementStateResult>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            var reads = readsByElement[element.Id];
            var badges = new List<string>();
            var facts = new List<FlowFact>();
            if (element.IsDecision)
            {
                ClassifyDecision(element, reads, classes, writersByVariable, badges, facts);
            }

            results[element.Id] = new ElementStateResult(reads, writesByElement.GetValueOrDefault(element.Id) ?? [], badges, facts);
        }

        return new StateAnalysisResult(variables, results, LinkCaches(elements));
    }

    /// <summary>The dependency classes read directly by one expression (variables excluded).</summary>
    public static IReadOnlyList<string> DirectClasses(ExpressionAnalysis analysis)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        if (analysis.NamedValues.Count > 0)
        {
            result.Add(FlowDependencyClasses.Configuration);
        }

        foreach (var member in analysis.ContextMembers)
        {
            result.Add(ConfigurationRoots.Contains(RootOf(member)) ? FlowDependencyClasses.Configuration : FlowDependencyClasses.Runtime);
        }

        return result.ToList();
    }

    private static string RootOf(string member)
    {
        var end = member.IndexOfAny(['.', '[']);
        return end < 0 ? member : member[..end];
    }

    private static IEnumerable<Write> DiscoverWrites(StateElementInput element)
    {
        var source = element.Source!;
        if (source.Name == "set-variable")
        {
            if (source.AttributeValue("name") is { Length: > 0 } name)
            {
                var value = element.Expressions.FirstOrDefault(e => e.Location == "@value")
                            ?? element.Expressions.FirstOrDefault(e => e.Location is "text()" or "value");
                yield return value is null
                    ? new Write(name, element.Id, true, [], [])
                    : new Write(name, element.Id, false, DirectClasses(value.Analysis), value.Analysis.VariablesRead);
            }

            yield break;
        }

        foreach (var attribute in source.Attributes)
        {
            if (!attribute.Name.EndsWith("variable-name", StringComparison.Ordinal) || attribute.Value.Length == 0 || attribute.IsExpression)
            {
                continue;
            }

            // return-response/@response-variable-name reads the variable; every other *variable-name attribute writes it
            // with a value produced at runtime (cache contents, responses, tokens, counters, validation errors).
            if (source.Name == "return-response" && attribute.Name == "response-variable-name")
            {
                continue;
            }

            yield return new Write(attribute.Value, element.Id, false, [FlowDependencyClasses.Runtime], []);
        }
    }

    private static Dictionary<string, SortedSet<string>> PropagateClasses(List<Write> writes, IEnumerable<string> variables)
    {
        var classes = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var written = writes.Select(w => w.Variable).ToHashSet(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            classes[variable] = written.Contains(variable)
                ? new SortedSet<string>(StringComparer.Ordinal)
                : new SortedSet<string>(StringComparer.Ordinal) { FlowDependencyClasses.Configuration };
        }

        foreach (var write in writes)
        {
            classes[write.Variable].UnionWith(write.DirectClasses);
            foreach (var read in write.ReadsVariables)
            {
                if (!classes.ContainsKey(read))
                {
                    classes[read] = new SortedSet<string>(StringComparer.Ordinal) { FlowDependencyClasses.Configuration };
                }
            }
        }

        // Monotone fixed point: classes only grow, and there are at most two, so this terminates quickly.
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var write in writes)
            {
                var target = classes[write.Variable];
                foreach (var read in write.ReadsVariables)
                {
                    var before = target.Count;
                    target.UnionWith(classes[read]);
                    changed |= target.Count != before;
                }
            }
        }

        return classes;
    }

    private static IReadOnlyList<string> WriterClasses(Write write, Dictionary<string, SortedSet<string>> classes)
    {
        var result = new SortedSet<string>(write.DirectClasses, StringComparer.Ordinal);
        foreach (var read in write.ReadsVariables)
        {
            result.UnionWith(classes[read]);
        }

        return result.ToList();
    }

    private static void ClassifyDecision(
        StateElementInput element,
        List<string> reads,
        Dictionary<string, SortedSet<string>> classes,
        Dictionary<string, List<Write>> writersByVariable,
        List<string> badges,
        List<FlowFact> facts)
    {
        var configurationInputs = new List<string>();
        var unassigned = false;

        foreach (var expression in element.Expressions)
        {
            foreach (var namedValue in expression.Analysis.NamedValues)
            {
                AddDistinct(configurationInputs, $"named value {{{{{namedValue}}}}}");
            }

            foreach (var member in expression.Analysis.ContextMembers)
            {
                if (ConfigurationRoots.Contains(RootOf(member)))
                {
                    AddDistinct(configurationInputs, $"context.{member}");
                }
            }
        }

        foreach (var variable in reads)
        {
            if (!writersByVariable.ContainsKey(variable))
            {
                unassigned = true;
                AddDistinct(configurationInputs, $"variable {variable} (not assigned in this policy)");
            }
            else if (classes.TryGetValue(variable, out var variableClasses) && variableClasses.Contains(FlowDependencyClasses.Configuration))
            {
                AddDistinct(configurationInputs, $"variable {variable} (derived from configuration)");
            }
        }

        if (configurationInputs.Count == 0)
        {
            return;
        }

        badges.Add(FlowBadges.ConfigurationDependent);
        if (unassigned)
        {
            badges.Add(FlowBadges.VariableNotAssigned);
        }

        facts.Add(new FlowFact(
            $"Configuration-dependent: reads {string.Join(", ", configurationInputs)}. Every branch is still shown.",
            FlowProvenance.Inferred));
    }

    private static IReadOnlyList<FlowEdge> LinkCaches(IReadOnlyList<StateElementInput> elements)
    {
        var stores = new List<(string Id, string Type, string Prefix)>();
        var lookups = new List<(string Id, string Type, string Prefix)>();
        foreach (var element in elements)
        {
            if (element.Source is not { Name: "cache-store-value" or "cache-lookup-value" } source)
            {
                continue;
            }

            var key = source.GetAttribute("key");
            var prefix = key is null
                ? null
                : key.IsExpression
                    ? element.Expressions.FirstOrDefault(e => e.Location == "@key")?.Analysis.KeyPrefix
                    : key.Value;
            if (string.IsNullOrEmpty(prefix))
            {
                continue;
            }

            var type = (source.AttributeValue("caching-type") ?? DefaultCachingType).Trim().ToLowerInvariant();
            (source.Name == "cache-store-value" ? stores : lookups).Add((element.Id, type, prefix));
        }

        var edges = new List<FlowEdge>();
        foreach (var store in stores)
        {
            foreach (var lookup in lookups)
            {
                if (store.Type == lookup.Type
                    && (store.Prefix.StartsWith(lookup.Prefix, StringComparison.Ordinal) || lookup.Prefix.StartsWith(store.Prefix, StringComparison.Ordinal)))
                {
                    edges.Add(new FlowEdge(
                        $"e:{store.Id}->{lookup.Id}:{FlowEdgeKinds.DataDependency}",
                        store.Id,
                        lookup.Id,
                        FlowEdgeKinds.DataDependency,
                        "affects subsequent requests",
                        Facts:
                        [
                            new FlowFact(
                                $"Values stored here (caching-type {store.Type}, key prefix \"{store.Prefix}\") can be read by this lookup in subsequent requests; this is not control flow.",
                                FlowProvenance.Inferred),
                        ]));
                }
            }
        }

        return edges;
    }

    private static void AddReader(Dictionary<string, List<string>> readers, List<string> order, string variable, string elementId)
    {
        if (!readers.TryGetValue(variable, out var list))
        {
            list = [];
            readers[variable] = list;
            order.Add(variable);
        }

        AddDistinct(list, elementId);
    }

    private static IReadOnlyList<string> Sorted(SortedSet<string> set) => set.ToList();

    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }
}
