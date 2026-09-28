using ApimPolicyVisualizer.Api.Policy.Model;
using ApimPolicyVisualizer.Api.Policy.Source;

namespace ApimPolicyVisualizer.Api.Tests;

public class FragmentRegionBuilderTests
{
    private static (PolicySourceDocument Document, FragmentRegionSet Regions) Build(string xml)
    {
        var document = new PolicySourceLoader().Load(xml);
        return (document, new FragmentRegionBuilder().Build(document));
    }

    private static string Begin(string name) => $"<!--include-fragment: Begin {name} policy fragment scope-->";

    private static string End(string name) => $"<!--include-fragment: End {name} policy fragment scope-->";

    [Fact]
    public void FragmentInOutboundAndOnError_YieldsTwoDistinctOccurrences()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(
            outbound: $"{Begin("set-response-headers")}<set-header name=\"a\" exists-action=\"delete\" />{End("set-response-headers")}",
            onError: $"{Begin("set-response-headers")}<set-header name=\"a\" exists-action=\"delete\" />{End("set-response-headers")}"));

        Assert.Equal(2, set.Regions.Count);
        Assert.Equal(["set-response-headers#1", "set-response-headers#2"], set.Regions.Select(r => r.OccurrenceId));
        Assert.Equal([1, 2], set.Regions.Select(r => r.Index));
        Assert.All(set.Regions, r => Assert.Equal(2, r.Count));
        Assert.Equal(["outbound", "on-error"], set.Regions.Select(r => r.Stage));
        Assert.Empty(set.Diagnostics);
    }

    [Fact]
    public void Regions_AreFoundAtNestingLevel_AndReportParentRegion()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(
            inbound:
                $"{Begin("outer")}<choose><when condition=\"@(true)\">{Begin("inside-when")}<base />" +
                $"{Begin("deepest")}<base />{End("deepest")}{End("inside-when")}</when></choose>{End("outer")}",
            backend: $"<retry condition=\"@(false)\" count=\"1\" interval=\"1\">{Begin("in-retry")}<forward-request />{End("in-retry")}</retry>"));

        var outer = set.Regions.Single(r => r.Name == "outer");
        var insideWhen = set.Regions.Single(r => r.Name == "inside-when");
        var deepest = set.Regions.Single(r => r.Name == "deepest");
        var inRetry = set.Regions.Single(r => r.Name == "in-retry");

        Assert.Equal("inbound", outer.Parent.Name);
        Assert.Null(outer.ParentRegion);
        Assert.Equal("when", insideWhen.Parent.Name);
        Assert.Same(outer, insideWhen.ParentRegion);
        Assert.Equal("when", deepest.Parent.Name);
        Assert.Same(insideWhen, deepest.ParentRegion);
        Assert.Equal("retry", inRetry.Parent.Name);
        Assert.Equal("backend", inRetry.Stage);
        Assert.Null(inRetry.ParentRegion);
        Assert.Equal("inbound", deepest.Stage);
    }

    [Fact]
    public void Label_UsesPrecedingStepComment_WithoutStepPrefix()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(
            inbound: $"<!-- Step 3: Validate model -->{Begin("model-check")}<!-- Fragment: Ignored title -->{End("model-check")}"));

        var region = Assert.Single(set.Regions);
        Assert.Equal("Validate model", region.Label);
        Assert.Equal(FlowProvenance.Comment, region.LabelProvenance);
    }

    [Theory]
    [InlineData("Step 4-6: Route request", "Route request")]
    [InlineData("Step 12. Retry backend", "Retry backend")]
    [InlineData("Step 4.1: Strip forwarded headers", "Strip forwarded headers")]
    [InlineData("Step 2.1-2.3: Load settings", "Load settings")]
    [InlineData("Resolve model", "Resolve model")]
    public void Label_StepPrefixVariants_AreRemoved(string comment, string expected)
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(inbound: $"<!-- {comment} -->{Begin("x")}<base />{End("x")}"));
        Assert.Equal(expected, Assert.Single(set.Regions).Label);
    }

    [Fact]
    public void Label_WithoutPrecedingComment_UsesFragmentHeader_AndKeepsDescription()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(
            inbound: $"<base />{Begin("model-check")}<!--\n    Fragment: Model check\n    Purpose: rejects unknown models.\n-->{End("model-check")}"));

        var region = Assert.Single(set.Regions);
        Assert.Equal("Model check", region.Label);
        Assert.Equal(FlowProvenance.Comment, region.LabelProvenance);
        Assert.Equal("Fragment: Model check\nPurpose: rejects unknown models.", region.Description);
    }

    [Fact]
    public void Label_FallsBackToHumanisedName()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(inbound: $"<base />{Begin("set-response-headers")}<base />{End("set-response-headers")}"));

        var region = Assert.Single(set.Regions);
        Assert.Equal("Set response headers", region.Label);
        Assert.Equal(FlowProvenance.FragmentName, region.LabelProvenance);
        Assert.Null(region.Description);
    }

    [Fact]
    public void MultiLinePrecedingComment_IsNotUsedAsLabel()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(inbound: $"<!-- first line\n second line -->{Begin("a-b")}<base />{End("a-b")}"));
        Assert.Equal("A b", Assert.Single(set.Regions).Label);
    }

    [Fact]
    public void Markers_AreWhitespaceTolerant()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(
            inbound: "<!--  include-fragment:Begin   spaced-name   policy  fragment scope  --><base /><!--include-fragment : end spaced-name policy fragment scope-->"));
        Assert.Equal("spaced-name#1", Assert.Single(set.Regions).OccurrenceId);
    }

    [Fact]
    public void UnmatchedBegin_ProducesWarning_AndNoRegion()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(inbound: $"{Begin("lonely")}<base />"));

        Assert.Empty(set.Regions);
        var diagnostic = Assert.Single(set.Diagnostics);
        Assert.Equal("fragment-marker-unmatched", diagnostic.Code);
        Assert.Equal(FlowDiagnosticSeverities.Warning, diagnostic.Severity);
        Assert.NotNull(diagnostic.Span);
    }

    [Fact]
    public void UnmatchedEnd_ProducesWarning()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(inbound: $"<base />{End("orphan")}"));
        Assert.Empty(set.Regions);
        Assert.Equal("fragment-marker-unmatched", Assert.Single(set.Diagnostics).Code);
    }

    [Fact]
    public void CrossedMarkers_ProduceWarnings_AndLeaveElementsUngrouped()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(
            inbound: $"{Begin("a")}<base />{Begin("b")}<base />{End("a")}<base />{End("b")}"));

        Assert.Empty(set.Regions);
        Assert.NotEmpty(set.Diagnostics);
        Assert.All(set.Diagnostics, d => Assert.Equal("fragment-marker-crossed", d.Code));
        Assert.All(set.Diagnostics, d => Assert.NotNull(d.Span));
    }

    [Fact]
    public void CrossParentMarkers_ProduceWarning()
    {
        var (_, set) = Build(PolicyModelTestHelpers.PolicyXml(
            inbound: $"{Begin("split")}<choose><when condition=\"@(true)\"><base />{End("split")}</when></choose>"));

        Assert.Empty(set.Regions);
        Assert.Equal("fragment-marker-cross-parent", Assert.Single(set.Diagnostics).Code);
    }

    [Fact]
    public void CommentedOutIncludeFragment_CreatesNoRegion_AndNoElement()
    {
        var (document, set) = Build(PolicyModelTestHelpers.PolicyXml(inbound: "<!-- <include-fragment fragment-id=\"legacy\" /> --><base />"));

        Assert.Empty(set.Regions);
        Assert.Empty(set.Diagnostics);
        Assert.DoesNotContain(document.Root.DescendantsAndSelf(), e => e.Name == "include-fragment");
    }

    [Fact]
    public void MarkerComments_AreRecognisedAsMarkers_AndLookupByBeginWorks()
    {
        var (document, set) = Build(PolicyModelTestHelpers.PolicyXml(inbound: $"{Begin("x")}<base />{End("x")}"));
        var inbound = document.Root.FirstElement("inbound")!;
        var begin = (SourceComment)inbound.Nodes[0];
        var end = (SourceComment)inbound.Nodes[2];

        Assert.True(set.IsMarker(begin));
        Assert.True(set.IsMarker(end));
        Assert.Same(set.Regions[0], set.RegionStartingAt(begin));
        Assert.Null(set.RegionStartingAt(end));
        Assert.Equal(new SourceSpan(begin.Span.StartLine, begin.Span.StartColumn, end.Span.EndLine, end.Span.EndColumn), set.Regions[0].Span);
    }

    [Fact]
    public void SyntheticFixture_HasTenOrMoreInboundRegions_AndRepeatedOccurrence()
    {
        var (_, set) = Build(PolicyModelTestHelpers.FixtureXml());

        Assert.True(set.Regions.Count(r => r.Stage == "inbound") >= 10);
        Assert.Empty(set.Diagnostics);
        var repeated = set.Regions.Where(r => r.Name == "set-response-headers").ToList();
        Assert.Equal(["outbound", "on-error"], repeated.Select(r => r.Stage));
        Assert.Equal(2, set.Regions.Count(r => r.Stage == "backend" && r.Parent.Name == "retry"));
        Assert.Equal("Validate model", set.Regions.Single(r => r.Name == "model-resolution").Label);
        Assert.Equal("Backend authentication", set.Regions.Single(r => r.Name == "backend-auth").Label);
        Assert.Equal("Backend attempt log", set.Regions.Single(r => r.Name == "backend-attempt-log").Label);
    }
}
