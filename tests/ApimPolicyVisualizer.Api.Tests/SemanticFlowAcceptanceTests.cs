using System.Xml.Linq;
using ApimPolicyVisualizer.Api.Policy.Model;
using Xunit.Sdk;
using static ApimPolicyVisualizer.Api.Tests.PolicyModelTestHelpers;

namespace ApimPolicyVisualizer.Api.Tests;

/// <summary>
/// SPF-15's 18 research-derived cases, through the real source/fragment/expression/model/rules/state pipeline.
/// Only the committed synthetic policy and small synthetic boundary policies are used; no ARM calls.
/// </summary>
public class SemanticFlowAcceptanceTests
{
    [Fact]
    public void Case01_ExplicitReturnResponse_GoesDirectlyToCaller_WithoutLaterStagesOrOnError()
    {
        var model = BuildFixture();
        AssertIntegrity(model);
        AssertExplicitResponsesTerminate(model);

        var returns = model.Elements.Where(e => e.Tag == "return-response").ToArray();
        Assert.Equal(6, returns.Length);
        Assert.Equal(["400", "401", "403", "404", "500", "503"],
            returns.SelectMany(e => e.Exits.ExplicitResponseCodes).Distinct().Order());
        foreach (var step in returns)
        {
            var code = Assert.Single(step.Properties, p => p.Name == "set-status/@code").Value;
            var reason = Assert.Single(step.Properties, p => p.Name == "set-status/@reason").Value;
            Assert.Equal($"Return {code} {reason}", step.Label);
            Assert.Equal([code], step.Exits.ExplicitResponseCodes);
        }

        // Every enclosing group must expose the distinct outcomes, not just the immediate parent.
        foreach (var group in model.Elements.Where(e => e.Kind == FlowElementKinds.Group))
        {
            var expected = returns.Where(e => model.IsDescendantOf(e.Id, group.Id))
                .SelectMany(e => e.Exits.ExplicitResponseCodes).Distinct().Order().ToArray();
            Assert.Equal(expected, group.Exits.ExplicitResponseCodes.Order());
        }
    }

    [Fact]
    public void Case02_BackendHttpError_ContinuesThroughOutbound_UnlessFailOnErrorStatusCodeIsTrue()
    {
        var fixture = BuildFixture();
        AssertForwardRequest(fixture, httpErrorsRaise: false);
        foreach (var setting in new[] { "", " fail-on-error-status-code=\"false\"", " fail-on-error-status-code=\"true\"" })
        {
            var model = Build(PolicyXml(backend: $"<forward-request{setting} />", onError: "<base />"));
            AssertForwardRequest(model, httpErrorsRaise: setting.Contains("\"true\"", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Case03_ValidateJwtFailure_EntersOnError_NotTheExplicitResponseTerminal()
    {
        var model = BuildFixture();
        var jwt = model.ByTag("validate-jwt");
        AssertEdge(model, jwt.Id, FlowIds.OnErrorEntry, FlowEdgeKinds.RaisesError);
        Assert.True(jwt.Exits.RaisesError);
        Assert.Empty(jwt.Exits.ExplicitResponseCodes);
        Assert.DoesNotContain(model.From(jwt.Id), e => e.Kind == FlowEdgeKinds.ExplicitResponse);
        Assert.Contains(jwt.Facts, f => f.RuleId == "validate-jwt.failed-validation-httpcode" && f.Text.Contains("401"));
        Assert.True(model.Element(jwt.ParentId!).Exits.RaisesError);
        Assert.Single(model.From(jwt.Id), e => e.Kind == FlowEdgeKinds.Sequence);
        Assert.Contains(FlowIds.ErrorResponseTerminal,
            PolicyFlowModelQueries.ReachableByControlFlow(model, FlowIds.OnErrorEntry));
    }

    [Fact]
    public void Case04_OnError_IsASeparateExceptionLane_AndNeverResumesOutbound()
    {
        var model = BuildFixture();
        AssertOnErrorDoesNotResume(model);
        Assert.Equal(3, model.OfKind(FlowEdgeKinds.StageException).Count());
        foreach (var stage in new[] { "inbound", "backend", "outbound" })
        {
            Assert.True(model.Stages.Single(s => s.Name == stage).Present);
            AssertEdge(model, FlowIds.Stage(stage), FlowIds.OnErrorEntry, FlowEdgeKinds.StageException);
        }

        Assert.Equal(FlowIds.Stage("on-error"), model.Element(FlowIds.OnErrorEntry).ParentId);
        Assert.Equal("Error response to caller", model.Element(FlowIds.ErrorResponseTerminal).Label);
        Assert.Equal("Final backend response to caller", model.Element(FlowIds.FinalResponseTerminal).Label);
        Assert.All(model.To(FlowIds.ErrorResponseTerminal), e => Assert.Equal("on-error", model.Element(e.From).Stage));
        Assert.Single(model.To(FlowIds.ErrorResponseTerminal));
        Assert.Single(model.To(FlowIds.FinalResponseTerminal));
        Assert.All(model.To(FlowIds.FinalResponseTerminal), e => Assert.Equal("outbound", model.Element(e.From).Stage));

        var normal = WithEdges(model, model.Edges.Where(IsNormalEdge));
        var reached = PolicyFlowModelQueries.ReachableByControlFlow(normal, FlowIds.RequestEntry);
        Assert.Contains(FlowIds.FinalResponseTerminal, reached);
        Assert.DoesNotContain(FlowIds.OnErrorEntry, reached);
        var transitions = normal.Edges
            .Select(e => (From: model.Element(e.From).Stage, To: model.Element(e.To).Stage))
            .Where(e => e.From is not null && e.To is not null && e.From != e.To).ToArray();
        Assert.Equal(new (string?, string?)[] { ("inbound", "backend"), ("backend", "outbound") }, transitions);
    }

    [Fact]
    public void Case05_Choose_IsFirstMatch_WithOrderedPriorities_NoMatchContinuation_AndMerge()
    {
        var model = BuildFixture();
        const string choose = "inbound/fragment:model-allowlist#1/0";
        var decision = model.Element($"{choose}/decision");
        Assert.Contains(decision.Facts, f => f.RuleId == ApimSemanticRules.ChooseFirstMatch);
        var branches = model.From(decision.Id).Where(e => e.Kind == FlowEdgeKinds.Branch).ToArray();
        Assert.Equal(new int?[] { 1, 2 }, branches.Select(e => e.Priority));
        Assert.Equal(
            new[] {
                "@(string.IsNullOrEmpty((string)context.Variables[\"requestedModel\"]))",
                "@(!\"{{allowed-models}}\".Contains((string)context.Variables[\"requestedModel\"]))"
            }, branches.Select(e => e.Condition!.Text));
        Assert.Equal(new[] { $"{choose}/when1/0", $"{choose}/when2/0" }, branches.Select(e => e.To));
        var noMatch = AssertEdge(model, decision.Id, $"{choose}/merge", FlowEdgeKinds.NoMatch);
        Assert.Equal("no match: continue", noMatch.Label);
        Assert.DoesNotContain(model.From(decision.Id), e => e.Kind == FlowEdgeKinds.Otherwise);
        AssertEdge(model, $"{choose}/merge", "inbound/fragment:maintenance-gate#1/0/decision", FlowEdgeKinds.Sequence);
        Assert.All(branches, b => Assert.DoesNotContain($"{choose}/merge",
            PolicyFlowModelQueries.ReachableByControlFlow(model, b.To)));

        // The error-shaping branch that continues must merge before the next sibling.
        const string errorChoose = "on-error/fragment:error-shaping#1/0";
        AssertEdge(model, $"{errorChoose}/decision", $"{errorChoose}/when1/0", FlowEdgeKinds.Branch);
        AssertEdge(model, $"{errorChoose}/when1/0", $"{errorChoose}/when1/1", FlowEdgeKinds.Sequence);
        AssertEdge(model, $"{errorChoose}/when1/1", $"{errorChoose}/merge", FlowEdgeKinds.Merge);
        AssertEdge(model, $"{errorChoose}/merge", model.ByTag("set-body").Id, FlowEdgeKinds.Sequence);
        Assert.DoesNotContain(model.From($"{errorChoose}/when2/0"), e => e.To == $"{errorChoose}/merge");
    }

    [Fact]
    public void Case06_EmptyWhen_BypassesOnlyItsOwnBlock_AndRejoinsBeforeTheNextSibling()
    {
        var model = BuildFixture();
        const string choose = "inbound/fragment:backend-routing#1/1";
        var outgoing = model.From($"{choose}/decision").ToArray();
        Assert.Equal(3, outgoing.Length);
        var bypass = Assert.Single(outgoing, e => e.Kind == FlowEdgeKinds.Bypass);
        Assert.Equal(2, bypass.Priority);
        Assert.Equal("@((string)context.Variables[\"backendPool\"] == \"pool-b\")", bypass.Condition!.Text);
        Assert.Equal($"{choose}/merge", bypass.To);
        AssertEdge(model, $"{choose}/decision", $"{choose}/when1/0", FlowEdgeKinds.Branch);
        AssertEdge(model, $"{choose}/decision", $"{choose}/otherwise/0", FlowEdgeKinds.Otherwise);
        AssertEdge(model, $"{choose}/when1/0", bypass.To, FlowEdgeKinds.Merge);
        AssertEdge(model, $"{choose}/otherwise/0", bypass.To, FlowEdgeKinds.Merge);
        AssertEdge(model, bypass.To,
            "inbound/fragment:request-headers#1/fragment:correlation-id#1/0", FlowEdgeKinds.Sequence);
        Assert.DoesNotContain(model.Elements, e => e.Id.StartsWith($"{choose}/when2/", StringComparison.Ordinal));
        Assert.DoesNotContain(model.From($"{choose}/decision"), e => e.Kind == FlowEdgeKinds.NoMatch);
    }

    [Fact]
    public void Case07_RetryBody_RunsBeforeLoopTest_AndLoopBackStaysInBackendWithoutReenteringInbound()
    {
        var model = BuildFixture();
        AssertRetryStaysInBackend(model);
        var loop = model.ByTag("retry");
        foreach (var fact in new[] { "count = 2", "interval = 1", "max-interval = 8", "delta = 1", "first-fast-retry = true" })
            Assert.Contains(loop.Facts, f => f.Text == fact && f.Provenance == FlowProvenance.Structural);
        Assert.Contains(loop.Facts, f => f.RuleId == ApimSemanticRules.RetryFirstAttempt &&
            f.Text.Contains("state prepared before the loop", StringComparison.Ordinal));
        Assert.Contains(loop.Facts, f => f.Provenance == FlowProvenance.Inferred && f.Text.Contains("3 attempts in total"));
        Assert.Contains(model.Element($"{loop.Id}/test").Facts, f => f.RuleId == ApimSemanticRules.RetryLoopTest);
        AssertEdge(model, $"{loop.Id}/test", "outbound/0", FlowEdgeKinds.LoopExit);
        AssertEdge(model, model.ByTag("forward-request").Id, FlowIds.OnErrorEntry, FlowEdgeKinds.RaisesError);
        Assert.Empty(model.From(FlowIds.ExplicitResponseTerminal));
        Assert.DoesNotContain($"{loop.Id}/test", PolicyFlowModelQueries.ReachableByControlFlow(model, FlowIds.OnErrorEntry));
    }

    [Fact]
    public void Case08_RepeatedFragments_AreDistinctOccurrenceNodes_WithSeparateStagePaths()
    {
        var model = BuildFixture();
        var repeated = model.Elements.Where(e => e.Fragment?.Name == "set-response-headers").ToArray();
        Assert.Equal(2, repeated.Length);
        Assert.Equal(new[] { "set-response-headers#1", "set-response-headers#2" }, repeated.Select(e => e.Fragment!.OccurrenceId));
        Assert.Equal(new[] { 1, 2 }, repeated.Select(e => e.Fragment!.Index));
        Assert.Equal(new[] { "outbound", "on-error" }, repeated.Select(e => e.Stage));
        Assert.Equal(2, repeated.Select(e => e.Id).Distinct().Count());
        foreach (var group in repeated)
        {
            Assert.Equal(FlowElementKinds.Group, group.Kind);
            Assert.Equal(2, group.Fragment!.Count);
            Assert.EndsWith($" · {group.Stage}", group.Label);
            var children = model.Elements.Where(e => e.ParentId == group.Id).ToArray();
            Assert.Equal(2, children.Length);
            Assert.All(children, c => Assert.Equal(group.Stage, c.Stage));
        }
        var rebuilt = BuildFixture();
        Assert.Equal(model.Elements.Select(e => e.Id), rebuilt.Elements.Select(e => e.Id));
        Assert.Equal(model.Edges.Select(e => e.Id), rebuilt.Edges.Select(e => e.Id));
    }

    [Fact]
    public void Case09_CommentedOutIncludeFragment_IsRetainedAsSource_NotExecutable()
    {
        var model = BuildFixture();
        Assert.Contains("<!-- <include-fragment fragment-id=\"legacy-quota\" /> -->", model.Source.Text);
        Assert.DoesNotContain(model.Elements, e => e.Tag == "include-fragment");
        Assert.DoesNotContain(model.Elements, e => e.Fragment?.Name == "legacy-quota");
        Assert.DoesNotContain(model.Elements, e => e.Tag is "quota" or "quota-by-key");
        // The actual neighbouring unknown policy must still exist and participate in control flow.
        var probe = model.ByTag("example-telemetry-probe");
        Assert.Equal(FlowElementKinds.Opaque, probe.Kind);
        Assert.Contains(model.Diagnostics, d => d.ElementId == probe.Id && d.Code == "unresolved-control-effect");
        Assert.Single(model.To(probe.Id), e => e.Kind == FlowEdgeKinds.Sequence);
        Assert.Single(model.From(probe.Id), e => e.Kind == FlowEdgeKinds.Sequence);
    }

    [Fact]
    public void Case10_CorsPreflight_HasASeparateEntryAndTerminal_FromTheOrdinaryRequest()
    {
        var model = BuildFixture();
        var cors = model.ByTag("cors");
        var entry = model.Element(FlowIds.PreflightEntry);
        Assert.Equal(FlowElementKinds.Entry, entry.Kind);
        Assert.NotEqual(FlowIds.RequestEntry, entry.Id);
        Assert.Contains(FlowBadges.ConfigurationDependent, entry.Badges);
        Assert.Contains(entry.Facts, f => f.RuleId == ApimSemanticRules.CorsPreflight && f.Text.Contains("OPTIONS operation"));
        Assert.Equal(2, model.OfKind(FlowEdgeKinds.Preflight).Count());
        AssertEdge(model, entry.Id, cors.Id, FlowEdgeKinds.Preflight);
        AssertEdge(model, cors.Id, FlowIds.PreflightResponseTerminal, FlowEdgeKinds.Preflight);
        var preflight = WithEdges(model, model.OfKind(FlowEdgeKinds.Preflight));
        Assert.Equal(new[] { entry.Id, cors.Id, FlowIds.PreflightResponseTerminal }.Order(),
            PolicyFlowModelQueries.ReachableByControlFlow(preflight, entry.Id).Order());
        var normal = WithEdges(model, model.Edges.Where(IsNormalEdge));
        var reached = PolicyFlowModelQueries.ReachableByControlFlow(normal, FlowIds.RequestEntry);
        Assert.Contains(cors.Id, reached);
        Assert.Contains(FlowIds.FinalResponseTerminal, reached);
        Assert.DoesNotContain(entry.Id, reached);
        Assert.DoesNotContain(FlowIds.PreflightResponseTerminal, reached);
        AssertEdge(model, "inbound/0", cors.Id, FlowEdgeKinds.Sequence);
    }

    [Fact]
    public void Case11_CacheStoreToLookup_IsADataDependency_NeverControlFlow()
    {
        var model = BuildFixture();
        var store = model.ByTag("cache-store-value");
        var lookup = model.ByTag("cache-lookup-value");
        var dependency = Assert.Single(model.OfKind(FlowEdgeKinds.DataDependency));
        Assert.Equal(store.Id, dependency.From);
        Assert.Equal(lookup.Id, dependency.To);
        Assert.Equal("affects subsequent requests", dependency.Label);
        Assert.False(PolicyFlowModelQueries.IsControlEdge(dependency));
        Assert.DoesNotContain(dependency, PolicyFlowModelQueries.ControlEdges(model));
        Assert.Equal(model.Edges.Count - 1, PolicyFlowModelQueries.ControlEdges(model).Count());
        Assert.DoesNotContain(lookup.Id, PolicyFlowModelQueries.ReachableByControlFlow(model, store.Id));
        Assert.Contains(FlowIds.FinalResponseTerminal, PolicyFlowModelQueries.ReachableByControlFlow(model, store.Id));
        Assert.Empty(lookup.Exits.ExplicitResponseCodes);
        Assert.DoesNotContain(model.From(lookup.Id), e => e.Kind == FlowEdgeKinds.ExplicitResponse);
        Assert.Single(model.From(lookup.Id), e => e.Kind == FlowEdgeKinds.Sequence);
    }

    [Fact]
    public void Case12_CSharpReturnAndCaughtException_AreExpressionLocal_NotPipelineTermination()
    {
        var model = BuildFixture();
        var extraction = model.Element("inbound/fragment:model-resolution#1/0");
        var analysis = Assert.Single(extraction.Expressions).Analysis;
        Assert.False(analysis.Opaque);
        Assert.True(analysis.HasLocalCatch);
        Assert.Equal("\"\"", analysis.CatchFallback);
        Assert.Equal(7, analysis.ReturnPaths.Count);
        Assert.Empty(extraction.Exits.ExplicitResponseCodes);
        Assert.False(extraction.Exits.RaisesError);
        var next = Assert.Single(model.From(extraction.Id));
        Assert.Equal(FlowEdgeKinds.Sequence, next.Kind);
        Assert.Equal("inbound/fragment:model-allowlist#1/0/decision", next.To);
        Assert.Contains(extraction.Facts, f => f.Provenance == FlowProvenance.Inferred && f.Text.Contains("expression-local"));
        Assert.Contains(extraction.Facts, f => f.Provenance == FlowProvenance.Inferred && f.Text.Contains("not an On-error transfer"));
    }

    [Fact]
    public void Case13_ConfigurationDependentBranches_AreBadgedNotHidden_RuntimeOnlyBranchesAreNotBadged()
    {
        var model = BuildFixture();
        foreach (var fragment in new[] { "maintenance-gate", "model-allowlist", "backend-routing" })
        {
            var decision = Assert.Single(model.Elements, e => e.Kind == FlowElementKinds.Decision &&
                model.IsDescendantOf(e.Id, $"inbound/fragment:{fragment}#1"));
            Assert.Contains(FlowBadges.ConfigurationDependent, decision.Badges);
            Assert.Contains(model.From(decision.Id), e => e.Kind == FlowEdgeKinds.Branch);
        }
        foreach (var fragment in new[] { "subscription-guard", "responses-ownership", "request-headers" })
        {
            var decision = Assert.Single(model.Elements, e => e.Kind == FlowElementKinds.Decision &&
                model.IsDescendantOf(e.Id, $"inbound/fragment:{fragment}#1"));
            Assert.DoesNotContain(FlowBadges.ConfigurationDependent, decision.Badges);
        }
        Assert.DoesNotContain(FlowBadges.ConfigurationDependent, model.Element("backend/0/test").Badges);
        var maintenance = model.Element("inbound/fragment:maintenance-gate#1/0/decision");
        Assert.Contains(FlowBadges.VariableNotAssigned, maintenance.Badges);
        var missing = Assert.Single(model.Variables, v => v.Name == "maintenanceMode");
        Assert.Empty(missing.Writers);
        Assert.Contains(maintenance.Id, missing.Readers);
        Assert.Equal([FlowDependencyClasses.Configuration], missing.DependencyClasses);
        var pool = Assert.Single(model.Variables, v => v.Name == "backendPool");
        Assert.Equal("inbound/fragment:backend-routing#1/0", Assert.Single(pool.Writers).ElementId);
        Assert.Contains("inbound/fragment:backend-routing#1/1/decision", pool.Readers);
        Assert.Equal([FlowDependencyClasses.Configuration], pool.DependencyClasses);
        Assert.Equal([FlowDependencyClasses.Runtime], model.Variables.Single(v => v.Name == "requestedModel").DependencyClasses);

        // Independent XML counts prevent "no unreachable badge" from passing after a branch was dropped.
        var source = XDocument.Parse(model.Source.Text);
        Assert.Equal(source.Descendants("choose").Count(), model.Elements.Count(e => e.Kind == FlowElementKinds.Decision));
        Assert.Equal(source.Descendants("when").Count(),
            model.Edges.Count(e => e.Kind == FlowEdgeKinds.Branch || (e.Kind == FlowEdgeKinds.Bypass && e.Priority is not null)));
        Assert.DoesNotContain(model.Elements.SelectMany(e => e.Badges), b => b.Contains("unreachable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Elements.SelectMany(e => e.Facts), f => f.Text.Contains("unreachable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Case14_ModelExtractionPrecedence_IsReportedAsOrderedInferredReturnPaths()
    {
        var model = BuildFixture();
        var extraction = model.Element("inbound/fragment:model-resolution#1/0");
        var expression = Assert.Single(extraction.Expressions);
        Assert.Equal("@value", expression.Location);
        var paths = expression.Analysis.ReturnPaths;
        Assert.Equal(Enumerable.Range(1, 7), paths.Select(p => p.Order));
        Assert.Equal(new string?[] {
            "context.Request.Method == \"GET\"", "!string.IsNullOrEmpty(fromQuery)",
            "path.Contains(\"/deployments/\")", "path.EndsWith(\"/models\")",
            "fromHeader != \"\"", "body != null && body[\"model\"] != null", null
        }, paths.Select(p => p.Condition));
        Assert.Equal(new[] { "\"none\"", "fromQuery", "path.Split('/')[3]", "\"catalog\"", "fromHeader", "body[\"model\"].ToString()", "\"\"" },
            paths.Select(p => p.ValueText));
        Assert.Equal(new[] { "literal", "variable", "expression", "literal", "variable", "expression", "literal" },
            paths.Select(p => p.ValueKind));
        Assert.Contains(extraction.Facts, f => f.Provenance == FlowProvenance.Inferred && f.Text.Contains("7 ordered return path(s)"));
        Assert.Contains("Request.Url.Query[\"model\"]", expression.Analysis.ContextMembers);
        Assert.Contains("Request.Headers[\"x-model\"]", expression.Analysis.ContextMembers);
    }

    [Fact]
    public void Case15_EveryFactHasProvenance_AndEveryApimRuleFactCitesAVersionedRule()
    {
        var model = BuildFixture();
        AssertIntegrity(model);
        Assert.False(string.IsNullOrWhiteSpace(ApimSemanticRules.Version));
        var facts = model.Elements.SelectMany(e => e.Facts)
            .Concat(model.Edges.SelectMany(e => e.Facts ?? [])).ToArray();
        Assert.NotEmpty(facts);
        Assert.Contains(facts, f => f.Provenance == FlowProvenance.ApimRule);
        Assert.Contains(facts, f => f.Provenance == FlowProvenance.Inferred);
        Assert.Contains(facts, f => f.Provenance == FlowProvenance.Comment);
        Assert.All(facts, fact =>
        {
            Assert.False(string.IsNullOrWhiteSpace(fact.Text));
            Assert.Contains(fact.Provenance,
                new[] { FlowProvenance.Structural, FlowProvenance.ApimRule, FlowProvenance.Inferred, FlowProvenance.Comment });
            if (fact.Provenance == FlowProvenance.ApimRule)
            {
                Assert.False(string.IsNullOrWhiteSpace(fact.RuleId));
                Assert.Contains(fact.RuleId!, ApimSemanticRules.RuleIds);
            }
        });
        Assert.All(model.Edges.Where(e => e.Condition is not null), e =>
            Assert.Contains(e.Condition!.Provenance, new[] { FlowProvenance.Structural, FlowProvenance.Inferred }));
        Assert.DoesNotContain(model.Diagnostics, d => d.Severity == FlowDiagnosticSeverities.Error);
    }

    [Fact]
    public void Case16_OnErrorReturnResponse_ReachesExplicitTerminal_AndNoStatusDefaultsTo200Ok()
    {
        var model = Build(PolicyXml(
            inbound: "<validate-jwt header-name=\"Authorization\" />",
            onError: "<return-response /><set-header name=\"must-not-run\" exists-action=\"delete\" />"));
        AssertExplicitResponsesTerminate(model);
        var response = model.ByTag("return-response");
        Assert.Equal("on-error", response.Stage);
        Assert.Equal("Return 200 OK (no body)", response.Label);
        Assert.Equal(["200"], response.Exits.ExplicitResponseCodes);
        AssertEdge(model, FlowIds.OnErrorEntry, response.Id, FlowEdgeKinds.Sequence);
        Assert.Empty(model.To(model.ByTag("set-header").Id));
        Assert.DoesNotContain(FlowIds.ErrorResponseTerminal,
            PolicyFlowModelQueries.ReachableByControlFlow(model, FlowIds.OnErrorEntry));

        // The other status fallback must not be silently treated as a literal 200.
        var variableModel = Build(PolicyXml(onError: "<return-response response-variable-name=\"savedResponse\" />"));
        var variableResponse = variableModel.ByTag("return-response");
        Assert.Equal("Return status from variable savedResponse", variableResponse.Label);
        Assert.Equal(["variable"], variableResponse.Exits.ExplicitResponseCodes);
        AssertExplicitResponsesTerminate(variableModel);
    }

    [Fact]
    public void Case17_NoOnErrorSection_RoutesStageFailuresToGatewayDefaultTerminal_WithoutFabricatedSteps()
    {
        foreach (var omitBackend in new[] { false, true })
        {
            var model = Build(PolicyXml(inbound: "<validate-jwt header-name=\"Authorization\" />",
                backend: omitBackend ? null : "<forward-request />"));
            AssertIntegrity(model);
            Assert.False(model.Stages.Single(s => s.Name == "on-error").Present);
            Assert.DoesNotContain(model.Elements, e => e.Stage == "on-error");
            Assert.DoesNotContain(model.Elements, e => e.Id is FlowIds.OnErrorEntry or FlowIds.ErrorResponseTerminal);
            Assert.Equal("Gateway default error response", model.Element(FlowIds.GatewayDefaultErrorTerminal).Label);
            Assert.Equal(omitBackend ? 2 : 3, model.OfKind(FlowEdgeKinds.StageException).Count());
            foreach (var stage in model.Stages.Where(s => s.Present))
                AssertEdge(model, stage.Id, FlowIds.GatewayDefaultErrorTerminal, FlowEdgeKinds.StageException);
            AssertEdge(model, model.ByTag("validate-jwt").Id, FlowIds.GatewayDefaultErrorTerminal, FlowEdgeKinds.RaisesError);
            Assert.All(model.OfKind(FlowEdgeKinds.RaisesError), e => Assert.Equal(FlowIds.GatewayDefaultErrorTerminal, e.To));
            if (omitBackend)
            {
                Assert.False(model.Stages.Single(s => s.Name == "backend").Present);
                Assert.DoesNotContain(model.Elements, e => e.Stage == "backend");
                AssertEdge(model, model.ByTag("validate-jwt").Id, "outbound/0", FlowEdgeKinds.Sequence);
            }
        }
    }

    [Fact]
    public void Case18_SendRequestIgnoreError_AndValidateDetectPrevent_ChangeRaisesErrorExits()
    {
        foreach (var setting in new[] { "", " ignore-error=\"false\"", " ignore-error=\"true\"" })
        {
            var model = Build(PolicyXml(
                inbound: $"<send-request mode=\"new\" response-variable-name=\"reply\"{setting} /><base />",
                onError: "<base />"));
            var request = model.ByTag("send-request");
            var ignores = setting.Contains("\"true\"", StringComparison.Ordinal);
            AssertRaisesError(model, request, !ignores);
            AssertEdge(model, request.Id, "inbound/1", FlowEdgeKinds.Sequence);
            if (ignores)
                Assert.Contains(request.Facts, f => f.RuleId == "send-request.ignore-error" && f.Text.Contains("null response variable (reply)"));
        }

        var validators = new (string Tag, string Attribute, bool Outbound)[] {
            ("validate-content", "unspecified-content-type-action", false),
            ("validate-parameters", "specified-parameter-action", false),
            ("validate-headers", "specified-header-action", true),
            ("validate-status-code", "unspecified-status-code-action", true)
        };
        foreach (var (tag, attribute, outbound) in validators)
        foreach (var action in new[] { "detect", "ignore", "prevent", "@(&quot;prevent&quot;)" })
        {
            var policy = $"<{tag} {attribute}=\"{action}\" /><base />";
            var model = Build(outbound
                ? PolicyXml(outbound: policy, onError: "<base />")
                : PolicyXml(inbound: policy, onError: "<base />"));
            var validator = model.ByTag(tag);
            var expression = action.StartsWith('@');
            AssertRaisesError(model, validator, action == "prevent" || expression);
            Assert.Equal(expression, validator.Badges.Contains(FlowBadges.ConfigurationDependent));
            AssertEdge(model, validator.Id, outbound ? "outbound/1" : "inbound/1", FlowEdgeKinds.Sequence);
            var rule = expression ? "action-expression" : action == "prevent" ? "action-prevent" : "action-detect";
            Assert.Contains(validator.Facts, f => f.RuleId == $"{tag}.{rule}");
        }

        // A nested prevent overrides a detect-only parent; configuration children are not execution steps.
        var nested = Build(PolicyXml(inbound: """
            <validate-parameters specified-parameter-action="detect">
              <headers specified-parameter-action="detect"><parameter name="x-example" action="prevent" /></headers>
            </validate-parameters>
            """, onError: "<base />"));
        AssertRaisesError(nested, nested.ByTag("validate-parameters"), true);
        Assert.DoesNotContain(nested.Elements, e => e.Tag is "headers" or "parameter");
    }

    [Fact]
    public void Case01_MutatedModel_ReturnResponseToLaterSibling_IsRejectedByTheSameInvariant()
    {
        var model = BuildFixture();
        AssertExplicitResponsesTerminate(model);
        const string response = "inbound/fragment:subscription-guard#1/0/when1/0";
        var later = model.ByTag("validate-jwt").Id;
        var mutant = WithEdges(model, model.Edges.Append(new FlowEdge("mutation:return-continues", response, later, FlowEdgeKinds.Sequence)));
        AssertIntegrity(mutant);
        var error = Assert.ThrowsAny<XunitException>(() => AssertExplicitResponsesTerminate(mutant));
        Assert.Contains(response, error.Message);
    }

    [Fact]
    public void Case04_MutatedModel_OnErrorToOutbound_IsRejectedByTheSameInvariant()
    {
        var model = BuildFixture();
        AssertOnErrorDoesNotResume(model);
        var mutant = WithEdges(model, model.Edges.Append(new FlowEdge("mutation:error-resumes", "on-error/0", "outbound/0", FlowEdgeKinds.Sequence)));
        AssertIntegrity(mutant);
        var error = Assert.ThrowsAny<XunitException>(() => AssertOnErrorDoesNotResume(mutant));
        Assert.Contains("outbound/0", error.Message);
    }

    [Fact]
    public void Case07_MutatedModel_LoopBackToInbound_IsRejectedByTheSameInvariant()
    {
        var model = BuildFixture();
        AssertRetryStaysInBackend(model);
        var loopBack = Assert.Single(model.OfKind(FlowEdgeKinds.LoopBack));
        var mutant = WithEdges(model, model.Edges.Select(e => e == loopBack
            ? e with { Id = "mutation:retry-reenters-inbound", To = "inbound/0" } : e));
        AssertIntegrity(mutant);
        var error = Assert.ThrowsAny<XunitException>(() => AssertRetryStaysInBackend(mutant));
        Assert.Contains("inbound/0", error.Message);
    }

    private static void AssertExplicitResponsesTerminate(EffectivePolicyFlowModel model)
    {
        var responses = model.Elements.Where(e => e.Tag == "return-response").ToArray();
        Assert.NotEmpty(responses);
        Assert.Equal(FlowElementKinds.Terminal, model.Element(FlowIds.ExplicitResponseTerminal).Kind);
        foreach (var response in responses)
        {
            var edges = model.From(response.Id).ToArray();
            Assert.True(edges.Length == 1 && edges[0].Kind == FlowEdgeKinds.ExplicitResponse &&
                edges[0].To == FlowIds.ExplicitResponseTerminal,
                $"SPF-FR-05 {response.Id}: expected only explicit-response -> {FlowIds.ExplicitResponseTerminal}; actual: {Describe(edges)}");
            var reachable = PolicyFlowModelQueries.ReachableByControlFlow(model, response.Id);
            Assert.True(reachable.SetEquals([response.Id, FlowIds.ExplicitResponseTerminal]),
                $"SPF-FR-05 {response.Id}: expected only self and explicit terminal reachable; actual: {string.Join(", ", reachable)}");
        }
    }

    private static void AssertOnErrorDoesNotResume(EffectivePolicyFlowModel model)
    {
        var onError = model.Elements.Where(e => e.Stage == "on-error" || e.Id == FlowIds.Stage("on-error")).ToArray();
        Assert.NotEmpty(onError);
        Assert.Contains(model.Elements, e => e.Id == FlowIds.OnErrorEntry);
        foreach (var start in onError)
        {
            var leaked = PolicyFlowModelQueries.ReachableByControlFlow(model, start.Id)
                .Where(id => model.Element(id).Stage is "inbound" or "backend" or "outbound" ||
                    id is "stage:inbound" or "stage:backend" or "stage:outbound" or FlowIds.FinalResponseTerminal).ToArray();
            Assert.True(leaked.Length == 0,
                $"SPF-FR-03 {start.Id}: expected no normal-stage continuation from On-error; actual reachable: {string.Join(", ", leaked)}");
        }
    }

    private static void AssertRetryStaysInBackend(EffectivePolicyFlowModel model)
    {
        var loop = model.ByTag("retry");
        Assert.Equal(FlowElementKinds.Loop, loop.Kind);
        Assert.Equal("backend", loop.Stage);
        var test = model.Element($"{loop.Id}/test");
        Assert.Equal(FlowElementKinds.LoopTest, test.Kind);
        var first = model.ByTag("authentication-managed-identity");
        var loopBack = Assert.Single(model.OfKind(FlowEdgeKinds.LoopBack));
        Assert.True(loopBack.From == test.Id && loopBack.To == first.Id &&
            model.IsDescendantOf(loopBack.To, loop.Id) && model.Element(loopBack.To).Stage == "backend",
            $"SPF-FR-06 {loop.Id}: expected loop-back {test.Id} -> first body {first.Id} inside Backend; actual: {Describe([loopBack])}");
        var externalEntries = model.Edges.Where(e => PolicyFlowModelQueries.IsControlEdge(e) &&
            model.IsDescendantOf(e.To, loop.Id) && !model.IsDescendantOf(e.From, loop.Id)).ToArray();
        Assert.NotEmpty(externalEntries);
        Assert.All(externalEntries, e => Assert.Equal(first.Id, e.To));
        AssertEdge(model, model.ByTag("example-telemetry-probe").Id, first.Id, FlowEdgeKinds.Sequence);
        var bodyOrder = new[] {
            first.Id, $"{loop.Id}/fragment:backend-attempt-log#1/0",
            $"{loop.Id}/fragment:backend-attempt-log#1/1", model.ByTag("forward-request").Id, test.Id
        };
        for (var i = 0; i < bodyOrder.Length - 1; i++)
        {
            var continuation = Assert.Single(model.From(bodyOrder[i]), IsNormalEdge);
            Assert.True(continuation.Kind == FlowEdgeKinds.Sequence && continuation.To == bodyOrder[i + 1],
                $"SPF-FR-06 {bodyOrder[i]}: expected next body step {bodyOrder[i + 1]} before loop test; actual: {Describe([continuation])}");
        }
        Assert.Single(model.To(test.Id), e => IsNormalEdge(e) && e.From == bodyOrder[^2]);
        var attemptOnly = WithEdges(model, model.Edges.Where(e => IsNormalEdge(e) && e.Kind != FlowEdgeKinds.LoopBack));
        Assert.Contains(test.Id, PolicyFlowModelQueries.ReachableByControlFlow(attemptOnly, first.Id));
        var inbound = PolicyFlowModelQueries.ReachableByControlFlow(model, test.Id)
            .Where(id => model.Element(id).Stage == "inbound").ToArray();
        Assert.True(inbound.Length == 0,
            $"SPF-FR-06 {test.Id}: expected no reachable Inbound elements; actual: {string.Join(", ", inbound)}");
    }

    private static void AssertForwardRequest(EffectivePolicyFlowModel model, bool httpErrorsRaise)
    {
        var forward = model.ByTag("forward-request");
        var raises = model.From(forward.Id).Where(e => e.Kind == FlowEdgeKinds.RaisesError).ToArray();
        Assert.Equal(httpErrorsRaise ? 2 : 1, raises.Length);
        Assert.All(raises, e => Assert.Equal(FlowIds.OnErrorEntry, e.To));
        Assert.Contains(raises, e => e.Facts!.Any(f => f.RuleId == "forward-request.transport-failure"));
        Assert.Equal(httpErrorsRaise, raises.Any(e => e.Facts!.Any(f => f.RuleId == "forward-request.fail-on-error-status-code")));
        Assert.Equal(!httpErrorsRaise, forward.Facts.Any(f => f.RuleId == "forward-request.http-status-continues"));
        var normal = WithEdges(model, model.Edges.Where(IsNormalEdge));
        var reachable = PolicyFlowModelQueries.ReachableByControlFlow(normal, forward.Id);
        Assert.Contains("outbound/0", reachable);
        Assert.Contains(FlowIds.FinalResponseTerminal, reachable);
        Assert.DoesNotContain(FlowIds.OnErrorEntry, reachable);
        // With fail-on-error=true this continuation is for successful responses, not HTTP-error responses.
        Assert.Empty(forward.Exits.ExplicitResponseCodes);
    }

    private static void AssertRaisesError(EffectivePolicyFlowModel model, FlowElement step, bool expected)
    {
        var raises = model.From(step.Id).Where(e => e.Kind == FlowEdgeKinds.RaisesError).ToArray();
        Assert.True(raises.Length == (expected ? 1 : 0) && step.Exits.RaisesError == expected,
            $"SPF-FR-07 {step.Id} <{step.Tag}> [{string.Join(", ", step.Attributes.Select(a => $"{a.Name}={a.Value}"))}]: " +
            $"expected raises-error={expected}; actual exits flag={step.Exits.RaisesError}, edges: {Describe(raises)}");
        Assert.All(raises, e => Assert.Equal(FlowIds.OnErrorEntry, e.To));
        Assert.Empty(step.Exits.ExplicitResponseCodes);
    }

    private static FlowEdge AssertEdge(EffectivePolicyFlowModel model, string from, string to, string kind)
    {
        var edges = model.From(from).Where(e => e.Kind == kind && e.To == to).ToArray();
        Assert.True(edges.Length == 1,
            $"Expected exactly one {from} -[{kind}]-> {to}; actual outgoing: {Describe(model.From(from))}");
        return edges[0];
    }

    private static bool IsNormalEdge(FlowEdge edge) =>
        PolicyFlowModelQueries.IsControlEdge(edge) && edge.Kind is not
            (FlowEdgeKinds.RaisesError or FlowEdgeKinds.StageException or FlowEdgeKinds.Preflight or FlowEdgeKinds.ExplicitResponse);

    private static string Describe(IEnumerable<FlowEdge> edges) =>
        string.Join("; ", edges.Select(e => $"{e.From} -[{e.Kind}]-> {e.To}"));

    // Copy only the edge list; mutations never alter the original model or shared element instances.
    private static EffectivePolicyFlowModel WithEdges(EffectivePolicyFlowModel model, IEnumerable<FlowEdge> edges) => new()
    {
        SchemaVersion = model.SchemaVersion,
        ScopeId = model.ScopeId,
        ScopeKind = model.ScopeKind,
        Source = model.Source,
        Stages = model.Stages,
        Elements = model.Elements,
        Edges = edges.ToArray(),
        Variables = model.Variables,
        Diagnostics = model.Diagnostics,
    };
}
