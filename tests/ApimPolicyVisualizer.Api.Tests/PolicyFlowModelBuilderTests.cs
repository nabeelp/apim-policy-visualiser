using ApimPolicyVisualizer.Api.Policy.Model;
using static ApimPolicyVisualizer.Api.Tests.PolicyModelTestHelpers;

namespace ApimPolicyVisualizer.Api.Tests;

public class PolicyFlowModelBuilderTests
{
    [Fact]
    public void ChooseWithoutOtherwise_HasPriorityBranches_NoMatchEdge_BypassForEmptyWhen_AndMerge()
    {
        var model = Build(PolicyXml(inbound:
            "<choose>" +
            "<when condition=\"@(context.Request.Method == &quot;GET&quot;)\"><set-variable name=\"a\" value=\"1\" /></when>" +
            "<when condition=\"@(context.Request.Method == &quot;PUT&quot;)\" />" +
            "<when condition=\"@(context.Request.Method == &quot;DELETE&quot;)\"><return-response><set-status code=\"405\" reason=\"Method Not Allowed\" /></return-response></when>" +
            "</choose><base />"));
        AssertIntegrity(model);

        var decision = model.Element("inbound/0/decision");
        var merge = model.Element("inbound/0/merge");
        Assert.Equal(FlowElementKinds.Decision, decision.Kind);
        Assert.Equal(FlowElementKinds.Merge, merge.Kind);
        Assert.Equal("inbound/0", decision.ParentId);
        Assert.Equal(0, decision.Order);
        Assert.Equal(model.Elements.Where(e => e.ParentId == "inbound/0").Max(e => e.Order), merge.Order);

        var branches = model.From(decision.Id).Where(e => e.Kind == FlowEdgeKinds.Branch).OrderBy(e => e.Priority).ToList();
        Assert.Equal([1, 3], branches.Select(e => e.Priority));
        Assert.Equal("@(context.Request.Method == \"GET\")", branches[0].Condition!.Text);
        Assert.Equal("Request method = \"GET\"", branches[0].Condition!.Summary);
        Assert.Equal(FlowProvenance.Inferred, branches[0].Condition!.Provenance);

        var bypass = Assert.Single(model.From(decision.Id), e => e.Kind == FlowEdgeKinds.Bypass);
        Assert.Equal(merge.Id, bypass.To);
        Assert.Equal(2, bypass.Priority);

        var noMatch = Assert.Single(model.From(decision.Id), e => e.Kind == FlowEdgeKinds.NoMatch);
        Assert.Equal(merge.Id, noMatch.To);
        Assert.Equal("no match: continue", noMatch.Label);
        Assert.DoesNotContain(model.Edges, e => e.Kind == FlowEdgeKinds.Otherwise);

        // The when ending in return-response never reaches the merge.
        var returnStep = model.Element("inbound/0/when3/0");
        Assert.DoesNotContain(model.From(returnStep.Id), e => e.To == merge.Id);
        Assert.Single(model.From("inbound/0/when1/0"), e => e.To == merge.Id && e.Kind == FlowEdgeKinds.Merge);

        // The merge continues to the next sibling.
        Assert.Single(model.From(merge.Id), e => e.To == "inbound/1" && e.Kind == FlowEdgeKinds.Sequence);
        Assert.Equal("Choose: Request method = \"GET\"", model.Element("inbound/0").Label);
    }

    [Fact]
    public void Otherwise_GetsOtherwiseEdge_AndEmptyOtherwiseIsBypass()
    {
        var model = Build(PolicyXml(inbound:
            "<choose><when condition=\"@(true)\"><base /></when><otherwise><set-header name=\"x\" exists-action=\"delete\" /></otherwise></choose>" +
            "<choose><when condition=\"@(true)\"><base /></when><otherwise /></choose>"));

        var otherwise = Assert.Single(model.From("inbound/0/decision"), e => e.Kind == FlowEdgeKinds.Otherwise);
        Assert.Equal("inbound/0/otherwise/0", otherwise.To);
        Assert.DoesNotContain(model.From("inbound/0/decision"), e => e.Kind == FlowEdgeKinds.NoMatch);
        var bypass = Assert.Single(model.From("inbound/1/decision"), e => e.Kind == FlowEdgeKinds.Bypass);
        Assert.Equal("inbound/1/merge", bypass.To);
        Assert.Null(bypass.Priority);
    }

    [Fact]
    public void ChooseWhereEveryBranchTerminates_HasNoMerge_AndNoContinuation()
    {
        var model = Build(PolicyXml(inbound:
            "<choose><when condition=\"@(true)\"><return-response /></when><otherwise><return-response /></otherwise></choose><base />"));

        Assert.DoesNotContain(model.Elements, e => e.Id == "inbound/0/merge");
        Assert.Empty(model.To("inbound/1"));
    }

    [Fact]
    public void DeeplyNestedReturnResponse_IsTerminal_WithSingleExplicitEdge_AndAggregatedCodes()
    {
        var model = Build(PolicyXml(inbound:
            "<choose><when condition=\"@(true)\">" +
            "<choose><when condition=\"@(true)\">" +
            "<choose><when condition=\"@(true)\"><return-response><set-status code=\"403\" reason=\"Forbidden\" /></return-response><set-header name=\"never\" exists-action=\"delete\" /></when></choose>" +
            "</when></choose>" +
            "<return-response><set-status code=\"403\" reason=\"Forbidden\" /></return-response>" +
            "</when></choose><base />"));
        AssertIntegrity(model);

        var nested = model.Element("inbound/0/when1/0/when1/0/when1/0");
        Assert.Equal("Return 403 Forbidden", nested.Label);
        Assert.Equal(FlowCategories.Response, nested.Category);
        var outgoing = model.From(nested.Id).ToList();
        var explicitEdge = Assert.Single(outgoing);
        Assert.Equal(FlowEdgeKinds.ExplicitResponse, explicitEdge.Kind);
        Assert.Equal(FlowIds.ExplicitResponseTerminal, explicitEdge.To);
        Assert.DoesNotContain(model.Edges, e => e.From == nested.Id && e.Kind == FlowEdgeKinds.Sequence);
        Assert.Empty(model.To("inbound/0/when1/0/when1/0/when1/1"));

        foreach (var ancestor in new[] { "inbound/0/when1/0/when1/0", "inbound/0/when1/0", "inbound/0", FlowIds.Stage("inbound") })
        {
            Assert.Equal(["403"], model.Element(ancestor).Exits.ExplicitResponseCodes);
        }

        Assert.Contains(nested.Facts, f => f.RuleId == ApimSemanticRules.ReturnResponseTerminates && f.Provenance == FlowProvenance.ApimRule);
        Assert.Equal(FlowElementKinds.Terminal, model.Element(FlowIds.ExplicitResponseTerminal).Kind);
    }

    [Theory]
    [InlineData("<return-response />", "Return 200 OK (no body)", "200")]
    [InlineData("<return-response response-variable-name=\"cached\" />", "Return status from variable cached", "variable")]
    [InlineData("<return-response><set-status code=\"429\" /></return-response>", "Return 429", "429")]
    [InlineData("<return-response><set-status code=\"@(403)\" reason=\"x\" /></return-response>", "Return status from expression", "expression")]
    public void ReturnResponseStatus_FollowsSetStatusThenVariableThenDefault(string markup, string label, string code)
    {
        var model = Build(PolicyXml(inbound: markup));
        var step = model.ByTag("return-response");
        Assert.Equal(label, step.Label);
        Assert.Equal([code], step.Exits.ExplicitResponseCodes);
    }

    [Fact]
    public void Retry_HasLoopTest_ReachedByEveryNonTerminatingBodyExit_LoopBack_LoopExit_AndFacts()
    {
        var model = Build(PolicyXml(
            inbound: "<base />",
            backend:
                "<retry condition=\"@(context.Response.StatusCode == 503)\" count=\"3\" interval=\"2\" max-interval=\"10\" delta=\"1\" first-fast-retry=\"true\">" +
                "<forward-request />" +
                "<choose><when condition=\"@(context.Response.StatusCode == 401)\"><return-response><set-status code=\"401\" /></return-response></when>" +
                "<when condition=\"@(context.Response.StatusCode == 500)\"><set-variable name=\"a\" value=\"1\" /></when>" +
                "<otherwise><set-variable name=\"b\" value=\"2\" /></otherwise></choose>" +
                "</retry>" +
                "<set-header name=\"after\" exists-action=\"delete\" />"));
        AssertIntegrity(model);

        var loop = model.Element("backend/0");
        var test = model.Element("backend/0/test");
        Assert.Equal(FlowElementKinds.Loop, loop.Kind);
        Assert.Equal(FlowElementKinds.LoopTest, test.Kind);
        Assert.Equal(loop.Id, test.ParentId);
        Assert.Equal(model.Elements.Where(e => e.ParentId == loop.Id).Max(e => e.Order), test.Order);
        Assert.Equal("Retry while Response status = 503", loop.Label);

        // Both non-terminating arms rejoin at the merge, which reaches the loop test; the return arm does not.
        Assert.Single(model.From("backend/0/1/merge"), e => e.To == test.Id);
        Assert.Single(model.From("backend/0/1/when2/0"), e => e.To == "backend/0/1/merge");
        Assert.Single(model.From("backend/0/1/otherwise/0"), e => e.To == "backend/0/1/merge");
        Assert.DoesNotContain(model.From("backend/0/1/when1/0"), e => e.To == test.Id || e.To == "backend/0/1/merge");
        Assert.DoesNotContain(model.From("backend/0/0"), e => e.Kind == FlowEdgeKinds.RaisesError && e.To == test.Id);

        var loopBack = Assert.Single(model.OfKind(FlowEdgeKinds.LoopBack));
        Assert.Equal(test.Id, loopBack.From);
        Assert.Equal("backend/0/0", loopBack.To);
        Assert.True(model.IsDescendantOf(loopBack.To, loop.Id));

        var loopExit = Assert.Single(model.OfKind(FlowEdgeKinds.LoopExit));
        Assert.Equal(test.Id, loopExit.From);
        Assert.Equal("backend/1", loopExit.To);

        var facts = loop.Facts.Select(f => f.Text).ToList();
        Assert.Contains("count = 3", facts);
        Assert.Contains("interval = 2", facts);
        Assert.Contains("max-interval = 10", facts);
        Assert.Contains("delta = 1", facts);
        Assert.Contains("first-fast-retry = true", facts);
        Assert.Contains(loop.Facts, f => f.Provenance == FlowProvenance.Inferred && f.Text.Contains("Response status = 503", StringComparison.Ordinal));
        Assert.Contains(loop.Facts, f => f.RuleId == ApimSemanticRules.RetryFirstAttempt);
        Assert.Contains(test.Expressions, e => e.Location == "@condition");
    }

    [Fact]
    public void RetryLastInStage_ExitsToTheNextStage()
    {
        var model = Build(PolicyXml(
            backend: "<retry condition=\"@(false)\" count=\"1\" interval=\"0\"><forward-request /></retry>",
            outbound: "<set-header name=\"x\" exists-action=\"delete\" />"));

        var loopExit = Assert.Single(model.OfKind(FlowEdgeKinds.LoopExit));
        Assert.Equal("outbound/0", loopExit.To);
    }

    [Fact]
    public void ConfigurationChildren_AreFoldedIntoProperties_AndNeverEmittedAsSteps()
    {
        var model = Build(PolicyXml(inbound:
            "<set-header name=\"x-a\" exists-action=\"override\"><value>one</value><value>two</value></set-header>" +
            "<validate-jwt header-name=\"Authorization\"><audiences><audience>api://a</audience></audiences>" +
            "<issuers><issuer>https://issuer</issuer></issuers><required-claims><claim name=\"roles\"><value>r</value></claim></required-claims></validate-jwt>" +
            "<emit-metric name=\"m\"><dimension name=\"d\" value=\"v\" /></emit-metric>" +
            "<trace source=\"s\"><message>hello</message></trace>"));
        AssertIntegrity(model);

        Assert.DoesNotContain(model.Elements, e => e.Tag is "value" or "audiences" or "audience" or "issuers" or "dimension" or "required-claims" or "claim" or "message");

        var header = model.ByTag("set-header");
        Assert.Equal(["one", "two"], header.Properties.Where(p => p.Name == "value").Select(p => p.Value));
        var jwt = model.ByTag("validate-jwt");
        Assert.Contains(jwt.Properties, p => p.Name == "audiences/audience" && p.Value == "api://a");
        Assert.Contains(jwt.Properties, p => p.Name == "required-claims/claim/@name" && p.Value == "roles");
        Assert.Contains(jwt.Properties, p => p.Name == "required-claims/claim/value" && p.Value == "r");
        Assert.Contains(model.ByTag("emit-metric").Properties, p => p.Name == "dimension/@name" && p.Value == "d");

        Assert.True(model.ByTag("emit-metric").Observability);
        Assert.True(model.ByTag("trace").Observability);
        Assert.Equal(FlowCategories.Observability, model.ByTag("trace").Category);
        Assert.False(header.Observability);
    }

    [Fact]
    public void UnknownTag_IsVisibleOpaqueStep_WithUnresolvedControlEffectDiagnostic()
    {
        var model = Build(PolicyXml(inbound: "<base /><my-custom-policy mode=\"x\"><return-response /></my-custom-policy><set-header name=\"after\" exists-action=\"delete\" />"));

        var opaque = model.ByTag("my-custom-policy");
        Assert.Equal(FlowElementKinds.Opaque, opaque.Kind);
        Assert.Equal(FlowCategories.Unknown, opaque.Category);
        Assert.Contains(FlowBadges.Opaque, opaque.Badges);
        var diagnostic = Assert.Single(model.Diagnostics, d => d.Code == "unresolved-control-effect");
        Assert.Equal(opaque.Id, diagnostic.ElementId);
        Assert.Equal(opaque.Span, diagnostic.Span);
        Assert.Single(model.From(opaque.Id), e => e.Kind == FlowEdgeKinds.Sequence && e.To == "inbound/2");
    }

    [Fact]
    public void FriendlyLabels_AndCategories_ArePerTag()
    {
        var model = Build(PolicyXml(inbound:
            "<set-variable name=\"requestedModel\" value=\"x\" /><set-backend-service backend-id=\"pool-a\" /><rate-limit calls=\"5\" renewal-period=\"60\" />" +
            "<cache-lookup-value key=\"k\" variable-name=\"v\" /><cors><allowed-origins><origin>*</origin></allowed-origins></cors>"));

        Assert.Equal("Set variable requestedModel", model.ByTag("set-variable").Label);
        Assert.Equal(FlowCategories.State, model.ByTag("set-variable").Category);
        Assert.Equal("Set backend service pool-a", model.ByTag("set-backend-service").Label);
        Assert.Equal(FlowCategories.Routing, model.ByTag("set-backend-service").Category);
        Assert.Equal("Rate limit 5 calls / 60s", model.ByTag("rate-limit").Label);
        Assert.Equal(FlowCategories.Cache, model.ByTag("cache-lookup-value").Category);
        Assert.Equal(FlowCategories.Cors, model.ByTag("cors").Category);
        Assert.Equal("Forward request to backend", model.ByTag("forward-request").Label);
        Assert.Equal(FlowCategories.BackendCall, model.ByTag("forward-request").Category);
        Assert.Equal(FlowCategories.Inherited, model.ByTag("base").Category);
    }

    [Fact]
    public void PolicyWrapper_ChildrenRunInPlace_AsFollowingSteps()
    {
        var model = Build(PolicyXml(backend: "<limit-concurrency key=\"k\" max-count=\"2\"><forward-request /></limit-concurrency>", outbound: "<base />"));
        AssertIntegrity(model);

        var wrapper = model.ByTag("limit-concurrency");
        var forward = model.ByTag("forward-request");
        Assert.Equal("backend/0/0", forward.Id);
        Assert.Equal(wrapper.ParentId, forward.ParentId);
        Assert.Single(model.From(wrapper.Id), e => e.Kind == FlowEdgeKinds.Sequence && e.To == forward.Id);
        Assert.Single(model.From(forward.Id), e => e.Kind == FlowEdgeKinds.Sequence && e.To == "outbound/0");
    }

    [Fact]
    public void ExpressionReturnAndCatch_AreNeverPipelineTermination()
    {
        var model = BuildFixture();
        var extraction = model.Elements.Single(e => e.Tag == "set-variable" && e.VariablesWritten.Contains("requestedModel"));

        Assert.DoesNotContain(model.From(extraction.Id), e => e.Kind is FlowEdgeKinds.ExplicitResponse or FlowEdgeKinds.RaisesError);
        Assert.Single(model.From(extraction.Id), e => e.Kind == FlowEdgeKinds.Sequence);
        Assert.Empty(extraction.Exits.ExplicitResponseCodes);
        Assert.Contains(extraction.Facts, f => f.Provenance == FlowProvenance.Inferred && f.Text.Contains("expression-local", StringComparison.Ordinal));
        Assert.Contains(extraction.Facts, f => f.Provenance == FlowProvenance.Inferred && f.Text.Contains("not an On-error transfer", StringComparison.Ordinal));
        Assert.Equal(7, Assert.Single(extraction.Expressions).Analysis.ReturnPaths.Count);
    }

    [Fact]
    public void Fixture_FragmentGroups_AreContainers_WithOccurrenceIdentity_AndStageQualifiedLabels()
    {
        var model = BuildFixture();

        var groups = model.Elements.Where(e => e.Kind == FlowElementKinds.Group).ToList();
        Assert.True(groups.Count(g => g.Stage == "inbound") >= 10);
        var repeated = groups.Where(g => g.Fragment!.Name == "set-response-headers").ToList();
        Assert.Equal(["outbound/fragment:set-response-headers#1", "on-error/fragment:set-response-headers#2"], repeated.Select(g => g.Id));
        Assert.Equal("Response headers · outbound", repeated[0].Label);
        Assert.Equal("Set response headers · on-error", repeated[1].Label);
        Assert.Equal(FlowProvenance.FragmentName, repeated[1].LabelProvenance);
        Assert.All(repeated, g => Assert.Equal(2, g.Fragment!.Count));

        var nested = model.Element("inbound/fragment:request-headers#1/fragment:correlation-id#1");
        Assert.Equal("inbound/fragment:request-headers#1", nested.ParentId);
        Assert.Contains(model.Elements, e => e.ParentId == nested.Id);

        var retryGroups = groups.Where(g => g.ParentId == "backend/0").ToList();
        Assert.Equal(2, retryGroups.Count);
        Assert.Contains(model.Elements, e => e.ParentId == "backend/0" && e.Tag == "forward-request");
    }

    [Fact]
    public void Fixture_CommentedOutIncludeFragment_IsNotAnElement()
    {
        var model = BuildFixture();
        Assert.DoesNotContain(model.Elements, e => e.Tag == "include-fragment");
        Assert.DoesNotContain(model.Elements, e => e.Fragment?.Name == "legacy-quota");
    }

    [Fact]
    public void Fixture_BuildsTwiceWithIdenticalIds_AndResolvesEveryReference()
    {
        var first = BuildFixture();
        var second = BuildFixture();

        Assert.Equal(first.Elements.Select(e => e.Id), second.Elements.Select(e => e.Id));
        Assert.Equal(first.Edges.Select(e => e.Id), second.Edges.Select(e => e.Id));
        AssertIntegrity(first);
        Assert.DoesNotContain(first.Diagnostics, d => d.Severity == FlowDiagnosticSeverities.Error);
        Assert.Single(first.Diagnostics, d => d.Code == "unresolved-control-effect");
    }

    [Fact]
    public void Fixture_ContainsOnlyInventedNames()
    {
        var xml = FixtureXml();
        foreach (var forbidden in new[] { "zlyway6g7icoy", "universal-llm-api", "unified-ai-api", "microsoft.com", "azure-api.net", "openai.azure.com" })
        {
            Assert.DoesNotContain(forbidden, xml, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("example", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixture_HasRequiredShapes()
    {
        var model = BuildFixture();
        var codes = model.Element(FlowIds.Stage("inbound")).Exits.ExplicitResponseCodes;
        Assert.Superset(new HashSet<string> { "400", "401", "403", "404", "503" }, codes.ToHashSet());
        Assert.Equal(["500"], model.Element(FlowIds.Stage("on-error")).Exits.ExplicitResponseCodes);
        Assert.Single(model.Elements, e => e.Kind == FlowElementKinds.Loop && e.Stage == "backend");
        Assert.Single(model.Elements, e => e.Tag == "cache-lookup-value" && e.Stage == "inbound");
        Assert.Single(model.Elements, e => e.Tag == "cache-store-value" && e.Stage == "outbound");
        Assert.Single(model.Elements, e => e.Tag == "cors");
        Assert.Single(model.Elements, e => e.Tag == "validate-jwt");
        Assert.Contains(model.Elements, e => e.Tag == "emit-metric" && e.Stage == "on-error");
        Assert.Contains(model.Elements, e => e.Kind == FlowElementKinds.Opaque);
    }

    [Fact]
    public void SequenceEdges_FollowSourceOrder_AcrossGroups()
    {
        var model = BuildFixture();
        Assert.Single(model.From(FlowIds.RequestEntry), e => e.Kind == FlowEdgeKinds.Sequence && e.To == "inbound/0");
        Assert.Single(model.From("inbound/0"), e => e.To == "inbound/fragment:cors-policy#1/0");
        Assert.All(model.Elements, e => Assert.False(string.IsNullOrWhiteSpace(e.Label)));
        Assert.All(model.Elements.SelectMany(e => e.Facts), f => Assert.Contains(f.Provenance, new[] { FlowProvenance.Structural, FlowProvenance.ApimRule, FlowProvenance.Inferred, FlowProvenance.Comment }));
    }
}
