using ApimPolicyVisualizer.Api.Policy.Model;
using static ApimPolicyVisualizer.Api.Tests.PolicyModelTestHelpers;

namespace ApimPolicyVisualizer.Api.Tests;

public class ApimSemanticRulesTests
{
    private const string FullPolicy = """
        <policies>
          <inbound><base /><validate-jwt header-name="Authorization" failed-validation-httpcode="401" /></inbound>
          <backend><forward-request /></backend>
          <outbound><set-header name="x" exists-action="delete" /></outbound>
          <on-error>
            <set-header name="x-error" exists-action="override"><value>1</value></set-header>
            <choose>
              <when condition="@(context.Response.StatusCode == 429)"><return-response><set-status code="429" reason="Too Many Requests" /></return-response></when>
            </choose>
            <send-request mode="new" response-variable-name="notify" />
          </on-error>
        </policies>
        """;

    [Fact]
    public void AllFourSections_ThreeStageExceptionEdges_OnErrorIsAnExceptionLaneThatNeverResumesOutbound()
    {
        var model = Build(FullPolicy);
        AssertIntegrity(model);

        var stageExceptions = model.OfKind(FlowEdgeKinds.StageException).ToList();
        Assert.Equal(3, stageExceptions.Count);
        Assert.Equal(["stage:inbound", "stage:backend", "stage:outbound"], stageExceptions.Select(e => e.From));
        Assert.All(stageExceptions, e => Assert.Equal(FlowIds.OnErrorEntry, e.To));
        Assert.All(stageExceptions, e => Assert.Contains(e.Facts!, f => f.RuleId == ApimSemanticRules.StageException));

        var byId = model.Elements.ToDictionary(e => e.Id);
        Assert.DoesNotContain(model.Edges, e => byId[e.From].Stage == "on-error" && byId[e.To].Stage == "outbound");
        Assert.DoesNotContain(model.Edges, e => byId[e.From].Stage == "on-error" && byId[e.To].Stage is "inbound" or "backend");

        var entry = model.Element(FlowIds.OnErrorEntry);
        Assert.Equal(FlowIds.Stage("on-error"), entry.ParentId);
        Assert.Equal(0, entry.Order);

        // Non-terminating On-error flow ends at the error-response terminal; the nested return-response reaches the explicit terminal.
        Assert.Single(model.To(FlowIds.ErrorResponseTerminal), e => e.From == "on-error/2");
        var returnStep = model.ByTag("return-response");
        Assert.Equal("on-error", returnStep.Stage);
        Assert.Single(model.From(returnStep.Id), e => e.Kind == FlowEdgeKinds.ExplicitResponse && e.To == FlowIds.ExplicitResponseTerminal);

        // A failure while running On-error goes to the gateway default response, never back into On-error.
        var notify = model.ByTag("send-request");
        var raise = Assert.Single(model.From(notify.Id), e => e.Kind == FlowEdgeKinds.RaisesError);
        Assert.Equal(FlowIds.GatewayDefaultErrorTerminal, raise.To);

        Assert.Single(model.To(FlowIds.FinalResponseTerminal), e => e.From == "outbound/0");
        Assert.Equal("Final backend response to caller", model.Element(FlowIds.FinalResponseTerminal).Label);
        Assert.Equal("Error response to caller", model.Element(FlowIds.ErrorResponseTerminal).Label);
        Assert.All(new[] { "inbound", "backend", "outbound", "on-error" }, s => Assert.True(model.Stages.Single(x => x.Name == s).Present));
    }

    [Fact]
    public void NoOnErrorSection_RoutesStageFailuresToGatewayDefaultTerminal()
    {
        var model = Build(PolicyXml(inbound: "<rate-limit calls=\"1\" renewal-period=\"1\" />"));
        AssertIntegrity(model);

        Assert.DoesNotContain(model.Elements, e => e.Id == FlowIds.OnErrorEntry);
        Assert.DoesNotContain(model.Elements, e => e.Id == FlowIds.ErrorResponseTerminal);
        var stageExceptions = model.OfKind(FlowEdgeKinds.StageException).ToList();
        Assert.Equal(3, stageExceptions.Count);
        Assert.All(stageExceptions, e => Assert.Equal(FlowIds.GatewayDefaultErrorTerminal, e.To));
        Assert.Single(model.From(model.ByTag("rate-limit").Id), e => e.Kind == FlowEdgeKinds.RaisesError && e.To == FlowIds.GatewayDefaultErrorTerminal);
        var terminal = model.Element(FlowIds.GatewayDefaultErrorTerminal);
        Assert.Equal("Gateway default error response", terminal.Label);
        Assert.Contains(terminal.Facts, f => f.RuleId == ApimSemanticRules.GatewayDefaultError);
        Assert.False(model.Stages.Single(s => s.Name == "on-error").Present);
        Assert.DoesNotContain(model.Elements, e => e.ParentId == FlowIds.Stage("on-error"));
    }

    [Fact]
    public void AbsentStage_IsPresentFalse_AndSkippedBySequence()
    {
        var model = Build("<policies><inbound><base /></inbound><outbound><base /></outbound></policies>");

        Assert.False(model.Stages.Single(s => s.Name == "backend").Present);
        Assert.DoesNotContain(model.Elements, e => e.ParentId == FlowIds.Stage("backend"));
        Assert.Single(model.From("inbound/0"), e => e.To == "outbound/0" && e.Kind == FlowEdgeKinds.Sequence);
        Assert.Equal(2, model.OfKind(FlowEdgeKinds.StageException).Count());
    }

    [Fact]
    public void ValidateJwt_RaisesError_AndFailedValidationHttpCodeIsAFactNotAnExplicitResponse()
    {
        var model = Build(FullPolicy);
        var jwt = model.ByTag("validate-jwt");

        var raise = Assert.Single(model.From(jwt.Id), e => e.Kind == FlowEdgeKinds.RaisesError);
        Assert.Equal(FlowIds.OnErrorEntry, raise.To);
        Assert.DoesNotContain(model.From(jwt.Id), e => e.Kind == FlowEdgeKinds.ExplicitResponse);
        Assert.Empty(jwt.Exits.ExplicitResponseCodes);
        Assert.True(jwt.Exits.RaisesError);
        Assert.Contains(jwt.Facts, f => f.RuleId == "validate-jwt.failed-validation-httpcode" && f.Text.Contains("401", StringComparison.Ordinal));
        Assert.True(model.Element(FlowIds.Stage("inbound")).Exits.RaisesError);
    }

    [Fact]
    public void ForwardRequest_TransportFailureRaises_HttpStatusContinuesUnlessFailOnErrorStatusCode()
    {
        var plain = Build(PolicyXml(backend: "<forward-request />", onError: "<base />"));
        var forward = plain.ByTag("forward-request");
        var raise = Assert.Single(plain.From(forward.Id), e => e.Kind == FlowEdgeKinds.RaisesError);
        Assert.Contains("transport", raise.Label, StringComparison.Ordinal);
        Assert.Contains(forward.Facts, f => f.RuleId == "forward-request.http-status-continues");
        Assert.Contains(forward.Facts, f => f.RuleId == "forward-request.primary-call");
        Assert.Single(plain.From(forward.Id), e => e.Kind == FlowEdgeKinds.Sequence && e.To == "outbound/0");

        var failing = Build(PolicyXml(backend: "<forward-request fail-on-error-status-code=\"true\" />", onError: "<base />"));
        var failingForward = failing.ByTag("forward-request");
        var raises = failing.From(failingForward.Id).Where(e => e.Kind == FlowEdgeKinds.RaisesError).ToList();
        Assert.Equal(2, raises.Count);
        Assert.Contains(raises, e => e.Label!.Contains("HTTP error status", StringComparison.Ordinal));
        Assert.DoesNotContain(failingForward.Facts, f => f.RuleId == "forward-request.http-status-continues");
    }

    [Theory]
    [InlineData("<send-request mode=\"new\" response-variable-name=\"r\" ignore-error=\"true\" />", 0)]
    [InlineData("<send-request mode=\"new\" response-variable-name=\"r\" ignore-error=\"false\" />", 1)]
    [InlineData("<send-request mode=\"new\" response-variable-name=\"r\" />", 1)]
    [InlineData("<validate-content unspecified-content-type-action=\"detect\" max-size=\"100\" size-exceeded-action=\"ignore\" />", 0)]
    [InlineData("<validate-content unspecified-content-type-action=\"prevent\" max-size=\"100\" size-exceeded-action=\"ignore\" />", 1)]
    [InlineData("<validate-parameters specified-parameter-action=\"detect\" unspecified-parameter-action=\"ignore\"><headers specified-parameter-action=\"detect\"><parameter name=\"a\" action=\"prevent\" /></headers></validate-parameters>", 1)]
    [InlineData("<validate-status-code unspecified-status-code-action=\"detect\" />", 0)]
    [InlineData("<validate-headers specified-header-action=\"ignore\" unspecified-header-action=\"prevent\" />", 1)]
    [InlineData("<check-header name=\"x\" failed-check-httpcode=\"401\" failed-check-error-message=\"no\" ignore-case=\"true\" />", 1)]
    [InlineData("<set-backend-service backend-id=\"b\" />", 0)]
    [InlineData("<cache-lookup-value key=\"k\" variable-name=\"v\" />", 0)]
    public void RaisesErrorExits_FollowRuleTablePredicates(string markup, int expectedRaises)
    {
        var model = Build(PolicyXml(inbound: markup, onError: "<base />"));
        var step = model.Elements.Single(e => e.ParentId == FlowIds.Stage("inbound"));

        Assert.Equal(expectedRaises, model.From(step.Id).Count(e => e.Kind == FlowEdgeKinds.RaisesError));
        Assert.Equal(expectedRaises > 0, step.Exits.RaisesError);
        Assert.DoesNotContain(model.From(step.Id), e => e.Kind == FlowEdgeKinds.ExplicitResponse);
        Assert.Single(model.From(step.Id), e => e.Kind == FlowEdgeKinds.Sequence);
        Assert.Contains(step.Facts, f => f.Provenance == FlowProvenance.ApimRule);
    }

    [Fact]
    public void ExpressionValuedAction_IsConfigurationDependentRaise()
    {
        var model = Build(PolicyXml(inbound: "<validate-content unspecified-content-type-action=\"@(&quot;prevent&quot;)\" max-size=\"1\" size-exceeded-action=\"ignore\" />"));
        var step = model.ByTag("validate-content");

        Assert.Single(model.From(step.Id), e => e.Kind == FlowEdgeKinds.RaisesError);
        Assert.Contains(FlowBadges.ConfigurationDependent, step.Badges);
    }

    [Fact]
    public void RoutingAndCacheState_AreFactsNotCallsOrEarlyResponses()
    {
        var model = Build(PolicyXml(inbound: "<set-backend-service backend-id=\"b\" /><cache-lookup-value key=\"k\" variable-name=\"v\" />"));

        Assert.Contains(model.ByTag("set-backend-service").Facts, f => f.RuleId == "set-backend-service.routing-state");
        var lookup = model.ByTag("cache-lookup-value");
        Assert.Contains(lookup.Facts, f => f.RuleId == "cache-lookup-value.state-access" && f.Text.Contains("not an early response", StringComparison.Ordinal));
        Assert.DoesNotContain(model.From(lookup.Id), e => e.Kind == FlowEdgeKinds.ExplicitResponse);
    }

    [Fact]
    public void Cors_AddsConfigurationDependentPreflightEntry_ThatBypassesTheOrdinaryPath()
    {
        var model = Build(PolicyXml(inbound: "<base /><cors><allowed-origins><origin>*</origin></allowed-origins></cors><set-header name=\"after\" exists-action=\"delete\" />"));
        AssertIntegrity(model);

        var entry = model.Element(FlowIds.PreflightEntry);
        Assert.Equal(FlowElementKinds.Entry, entry.Kind);
        Assert.Equal("CORS preflight request (if no matching OPTIONS operation)", entry.Label);
        Assert.Contains(FlowBadges.ConfigurationDependent, entry.Badges);
        Assert.Contains(entry.Facts, f => f.RuleId == ApimSemanticRules.CorsPreflight && f.Text.Contains("OPTIONS operation", StringComparison.Ordinal));

        var cors = model.ByTag("cors");
        var preflight = model.OfKind(FlowEdgeKinds.Preflight).ToList();
        Assert.Equal(2, preflight.Count);
        Assert.Contains(preflight, e => e.From == FlowIds.PreflightEntry && e.To == cors.Id);
        Assert.Contains(preflight, e => e.From == cors.Id && e.To == FlowIds.PreflightResponseTerminal);
        Assert.Equal("Preflight response to caller", model.Element(FlowIds.PreflightResponseTerminal).Label);

        // The ordinary request path reaches cors in its source position through sequence edges, and never uses preflight edges.
        Assert.Single(model.To(cors.Id), e => e.Kind == FlowEdgeKinds.Sequence && e.From == "inbound/0");
        Assert.DoesNotContain(model.From(FlowIds.RequestEntry), e => e.Kind == FlowEdgeKinds.Preflight);
        var ordinary = ReachableExcluding(model, FlowIds.RequestEntry, FlowEdgeKinds.Preflight);
        Assert.Contains(cors.Id, ordinary);
        Assert.DoesNotContain(FlowIds.PreflightResponseTerminal, ordinary);
    }

    [Fact]
    public void WithoutCors_NoPreflightElementsExist()
    {
        var model = Build(PolicyXml());
        Assert.DoesNotContain(model.Elements, e => e.Id is FlowIds.PreflightEntry or FlowIds.PreflightResponseTerminal);
        Assert.Empty(model.OfKind(FlowEdgeKinds.Preflight));
    }

    [Fact]
    public void EveryApimRuleFact_CitesARuleIdFromTheTable()
    {
        var model = BuildFixture();
        var facts = model.Elements.SelectMany(e => e.Facts).Concat(model.Edges.SelectMany(e => e.Facts ?? [])).ToList();
        var ruleFacts = facts.Where(f => f.Provenance == FlowProvenance.ApimRule).ToList();

        Assert.NotEmpty(ruleFacts);
        Assert.All(ruleFacts, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.RuleId));
            Assert.Contains(f.RuleId!, ApimSemanticRules.RuleIds);
        });
        Assert.All(facts.Where(f => f.Provenance != FlowProvenance.ApimRule), f => Assert.Null(f.RuleId));
    }

    [Fact]
    public void RuleTable_IsVersioned_WithUniqueDescribedRules()
    {
        Assert.False(string.IsNullOrWhiteSpace(ApimSemanticRules.Version));
        Assert.Equal(ApimSemanticRules.Rules.Count, ApimSemanticRules.RuleIds.Count);
        Assert.All(ApimSemanticRules.Rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
        foreach (var tag in new[] { "validate-jwt", "validate-azure-ad-token", "check-header", "ip-filter", "rate-limit", "rate-limit-by-key", "quota", "quota-by-key", "limit-concurrency", "llm-token-limit", "azure-openai-token-limit" })
        {
            Assert.Contains($"{tag}.raises-on-failure", ApimSemanticRules.RuleIds);
        }
    }

    [Fact]
    public void Fixture_RetryForwardRequest_RaisesToOnError_AndGroupsAggregateRaises()
    {
        var model = BuildFixture();
        var forward = model.ByTag("forward-request");

        Assert.Single(model.From(forward.Id), e => e.Kind == FlowEdgeKinds.RaisesError && e.To == FlowIds.OnErrorEntry);
        Assert.True(model.Element("backend/0").Exits.RaisesError);
        Assert.True(model.Element("inbound/fragment:caller-authentication#1").Exits.RaisesError);
        Assert.False(model.Element("inbound/fragment:model-resolution#1").Exits.RaisesError);
        Assert.Single(model.To(FlowIds.ErrorResponseTerminal));
        Assert.Contains(model.To(FlowIds.ExplicitResponseTerminal), e => model.Element(e.From).Stage == "on-error");
    }

    private static HashSet<string> ReachableExcluding(EffectivePolicyFlowModel model, string start, string excludedKind)
    {
        var seen = new HashSet<string> { start };
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in model.Edges.Where(e => e.From == current && e.Kind != excludedKind && PolicyFlowModelQueries.IsControlEdge(e)))
            {
                if (seen.Add(edge.To))
                {
                    queue.Enqueue(edge.To);
                }
            }
        }

        return seen;
    }
}
