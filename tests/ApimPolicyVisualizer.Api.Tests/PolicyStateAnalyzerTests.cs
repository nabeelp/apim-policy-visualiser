using ApimPolicyVisualizer.Api.Policy.Model;
using static ApimPolicyVisualizer.Api.Tests.PolicyModelTestHelpers;

namespace ApimPolicyVisualizer.Api.Tests;

public class PolicyStateAnalyzerTests
{
    [Fact]
    public void VariableIndex_ListsEveryWriterAndReader()
    {
        var model = Build(PolicyXml(
            inbound:
                "<set-variable name=\"target\" value=\"primary\" />" +
                "<set-variable name=\"target\" value=\"@(context.Request.Headers.GetValueOrDefault(&quot;x-target&quot;, &quot;primary&quot;))\" />" +
                "<choose><when condition=\"@((string)context.Variables[&quot;target&quot;] == &quot;secondary&quot;)\"><base /></when></choose>",
            backend: "<retry condition=\"@(context.Variables.GetValueOrDefault&lt;string&gt;(&quot;target&quot;) != &quot;primary&quot;)\" count=\"1\" interval=\"0\"><forward-request /></retry>"));
        AssertIntegrity(model);

        var variable = Assert.Single(model.Variables, v => v.Name == "target");
        Assert.Equal(["inbound/0", "inbound/1"], variable.Writers.Select(w => w.ElementId));
        Assert.True(variable.Writers[0].Literal);
        Assert.Empty(variable.Writers[0].DependencyClasses);
        Assert.False(variable.Writers[1].Literal);
        Assert.Equal([FlowDependencyClasses.Runtime], variable.Writers[1].DependencyClasses);
        Assert.Equal(["inbound/2/decision", "backend/0/test"], variable.Readers);
        Assert.Equal([FlowDependencyClasses.Runtime], variable.DependencyClasses);

        Assert.Equal(["target"], model.Element("inbound/0").VariablesWritten);
        Assert.Equal(["target"], model.Element("inbound/2/decision").VariablesRead);
        Assert.Equal(["target"], model.Element("backend/0/test").VariablesRead);

        // Runtime-only inputs: rendered normally, no badge.
        Assert.DoesNotContain(FlowBadges.ConfigurationDependent, model.Element("inbound/2/decision").Badges);
        Assert.DoesNotContain(FlowBadges.ConfigurationDependent, model.Element("backend/0/test").Badges);
    }

    [Fact]
    public void Decisions_AreBadgedOnlyForConfigurationInputs()
    {
        var model = Build(PolicyXml(inbound:
            "<set-variable name=\"pool\" value=\"@(&quot;{{pool-name}}&quot;)\" />" +
            "<set-variable name=\"method\" value=\"@(context.Request.Method)\" />" +
            "<set-variable name=\"status\" value=\"@(context.Response.StatusCode)\" />" +
            "<choose><when condition=\"@(context.Variables.GetValueOrDefault&lt;bool&gt;(&quot;neverAssigned&quot;, false))\"><base /></when></choose>" +
            "<choose><when condition=\"@((string)context.Variables[&quot;pool&quot;] == &quot;b&quot;)\"><base /></when></choose>" +
            "<choose><when condition=\"@((string)context.Variables[&quot;method&quot;] == &quot;GET&quot;)\"><base /></when></choose>" +
            "<choose><when condition=\"@((int)context.Variables[&quot;status&quot;] == 429)\"><base /></when></choose>" +
            "<choose><when condition=\"@(context.Api.Name == &quot;x&quot;)\"><base /></when></choose>"));

        var unassigned = model.Element("inbound/3/decision");
        Assert.Contains(FlowBadges.ConfigurationDependent, unassigned.Badges);
        Assert.Contains(FlowBadges.VariableNotAssigned, unassigned.Badges);
        Assert.Contains(unassigned.Facts, f => f.Provenance == FlowProvenance.Inferred && f.Text.Contains("neverAssigned (not assigned in this policy)", StringComparison.Ordinal));
        Assert.Contains(FlowBadges.ConfigurationDependent, model.Element("inbound/3").Badges);

        var namedValueDerived = model.Element("inbound/4/decision");
        Assert.Contains(FlowBadges.ConfigurationDependent, namedValueDerived.Badges);
        Assert.DoesNotContain(FlowBadges.VariableNotAssigned, namedValueDerived.Badges);

        foreach (var runtimeOnly in new[] { "inbound/5/decision", "inbound/6/decision" })
        {
            Assert.DoesNotContain(FlowBadges.ConfigurationDependent, model.Element(runtimeOnly).Badges);
            Assert.DoesNotContain(FlowBadges.VariableNotAssigned, model.Element(runtimeOnly).Badges);
        }

        Assert.Contains(FlowBadges.ConfigurationDependent, model.Element("inbound/7/decision").Badges);

        var neverAssigned = Assert.Single(model.Variables, v => v.Name == "neverAssigned");
        Assert.Empty(neverAssigned.Writers);
        Assert.Equal([FlowDependencyClasses.Configuration], neverAssigned.DependencyClasses);
        Assert.Equal([FlowDependencyClasses.Configuration], model.Variables.Single(v => v.Name == "pool").DependencyClasses);
    }

    [Fact]
    public void ConfigurationDependence_PropagatesThroughChainedWriters()
    {
        var model = Build(PolicyXml(inbound:
            "<set-variable name=\"a\" value=\"@(&quot;{{flag}}&quot;)\" />" +
            "<set-variable name=\"b\" value=\"@((string)context.Variables[&quot;a&quot;] + &quot;-x&quot;)\" />" +
            "<choose><when condition=\"@((string)context.Variables[&quot;b&quot;] == &quot;on-x&quot;)\"><base /></when></choose>"));

        Assert.Equal([FlowDependencyClasses.Configuration], model.Variables.Single(v => v.Name == "b").DependencyClasses);
        Assert.Contains(FlowBadges.ConfigurationDependent, model.Element("inbound/2/decision").Badges);
    }

    [Fact]
    public void ConfigurationDependentDecisions_KeepEveryBranch_AndNeverClaimUnreachable()
    {
        var model = BuildFixture();
        var badged = model.Elements.Where(e => e.Kind == FlowElementKinds.Decision && e.Badges.Contains(FlowBadges.ConfigurationDependent)).ToList();

        Assert.NotEmpty(badged);
        Assert.All(badged, d => Assert.Contains(model.Edges, e => e.From == d.Id && e.Kind == FlowEdgeKinds.Branch));
        Assert.DoesNotContain(model.Elements.SelectMany(e => e.Facts), f => f.Text.Contains("unreachable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Elements, e => e.Badges.Any(b => b.Contains("unreachable", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void OtherWriters_AreIndexed_WithRuntimeClass()
    {
        var model = Build(PolicyXml(inbound:
            "<cache-lookup-value key=\"k\" variable-name=\"cached\" />" +
            "<send-request mode=\"new\" response-variable-name=\"reply\" ignore-error=\"true\" />" +
            "<validate-jwt header-name=\"Authorization\" output-token-variable-name=\"jwt\" />" +
            "<return-response response-variable-name=\"reply\" />"));

        foreach (var name in new[] { "cached", "reply", "jwt" })
        {
            var variable = Assert.Single(model.Variables, v => v.Name == name);
            Assert.Equal([FlowDependencyClasses.Runtime], Assert.Single(variable.Writers).DependencyClasses);
        }

        Assert.Equal(["inbound/3"], model.Variables.Single(v => v.Name == "reply").Readers);
        Assert.Empty(model.Element("inbound/3").VariablesWritten);
    }

    [Fact]
    public void Fixture_CacheStoreAndLookup_WithMatchingPrefix_AreLinkedOnce()
    {
        var model = BuildFixture();
        var store = model.ByTag("cache-store-value");
        var lookup = model.ByTag("cache-lookup-value");

        var edge = Assert.Single(model.OfKind(FlowEdgeKinds.DataDependency));
        Assert.Equal(store.Id, edge.From);
        Assert.Equal(lookup.Id, edge.To);
        Assert.Equal("affects subsequent requests", edge.Label);
        Assert.Contains(edge.Facts!, f => f.Provenance == FlowProvenance.Inferred);
        Assert.Equal("outbound", store.Stage);
        Assert.Equal("inbound", lookup.Stage);
    }

    [Theory]
    [InlineData("@(&quot;owner-&quot; + context.Subscription.Id)", "internal", "@(&quot;owner-&quot; + context.Request.IpAddress)", "internal", 1)]
    [InlineData("owner-static", "prefer-external", "@(&quot;owner-&quot; + context.Request.IpAddress)", "prefer-external", 1)]
    [InlineData("@(&quot;owner-&quot; + context.Subscription.Id)", "internal", "@(&quot;model-&quot; + context.Request.IpAddress)", "internal", 0)]
    [InlineData("@(&quot;owner-&quot; + context.Subscription.Id)", "internal", "@(&quot;owner-&quot; + context.Request.IpAddress)", "external", 0)]
    [InlineData("@(context.Subscription.Id)", "internal", "@(context.Subscription.Id)", "internal", 0)]
    public void CacheLinks_RequireEqualCachingTypeAndMatchingLiteralPrefix(string storeKey, string storeType, string lookupKey, string lookupType, int expected)
    {
        var model = Build(PolicyXml(
            inbound: $"<cache-lookup-value key=\"{lookupKey}\" variable-name=\"v\" caching-type=\"{lookupType}\" />",
            outbound: $"<cache-store-value key=\"{storeKey}\" value=\"x\" duration=\"10\" caching-type=\"{storeType}\" />"));

        Assert.Equal(expected, model.OfKind(FlowEdgeKinds.DataDependency).Count());
    }

    [Fact]
    public void ControlFlowHelpers_NeverTraverseDataDependencyEdges()
    {
        var model = BuildFixture();
        var store = model.ByTag("cache-store-value");
        var lookup = model.ByTag("cache-lookup-value");

        // From the outbound store, only a data-dependency edge leads back to the inbound lookup.
        var reachable = PolicyFlowModelQueries.ReachableByControlFlow(model, store.Id);
        Assert.DoesNotContain(lookup.Id, reachable);
        Assert.Contains(FlowIds.FinalResponseTerminal, reachable);
        Assert.DoesNotContain(PolicyFlowModelQueries.ControlEdges(model), e => e.Kind == FlowEdgeKinds.DataDependency);
        Assert.Equal(model.Edges.Count - 1, PolicyFlowModelQueries.ControlEdges(model).Count());

        var fromRequest = PolicyFlowModelQueries.ReachableByControlFlow(model, FlowIds.RequestEntry);
        Assert.Contains(lookup.Id, fromRequest);
        Assert.Contains(FlowIds.ExplicitResponseTerminal, fromRequest);
        Assert.Contains(FlowIds.OnErrorEntry, fromRequest);
    }

    [Fact]
    public void Analyzer_CanBeUsedDirectly_OnInputs()
    {
        var analyzer = new PolicyStateAnalyzer();
        var result = analyzer.Analyze([]);
        Assert.Empty(result.Variables);
        Assert.Empty(result.DataDependencyEdges);
        Assert.Equal([FlowDependencyClasses.Configuration], PolicyStateAnalyzer.DirectClasses(
            new ExpressionAnalysis(false, null, [], ["nv"], [], [], false, null, null, [])));
        Assert.Equal([FlowDependencyClasses.Configuration, FlowDependencyClasses.Runtime], PolicyStateAnalyzer.DirectClasses(
            new ExpressionAnalysis(false, null, [], [], ["Deployment.Region", "Request.Method"], [], false, null, null, [])));
    }
}
