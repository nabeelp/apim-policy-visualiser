using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using ApimPolicyVisualizer.Api.Policy.Model;
using Xunit.Abstractions;
using static ApimPolicyVisualizer.Api.Tests.PolicyModelTestHelpers;

namespace ApimPolicyVisualizer.Api.Tests;

// Keep other xUnit collections from competing with the measured in-process builds.
[CollectionDefinition("Semantic flow performance", DisableParallelization = true)]
public sealed class SemanticFlowPerformanceCollection;

[Collection("Semantic flow performance")]
public class SemanticFlowPerformanceTests(ITestOutputHelper output)
{
    private const int AdditionalInboundCopies = 18;
    private const int WarmupBuilds = 3;
    private const int TimedBuilds = 20;

    [Fact]
    public void ScaledSyntheticFixture_FullPipelineBuild_P95DoesNotExceedOneSecond()
    {
        var xml = BuildLargePolicy();
        var bytes = Encoding.UTF8.GetByteCount(xml);
        var lines = xml.Count(c => c == '\n') + 1;
        // Fail before timing if fixture edits accidentally turn this into a tiny or oversized benchmark.
        Assert.InRange(bytes, 285 * 1024, 315 * 1024);
        Assert.InRange(lines, 2400, 2600);
        var document = XDocument.Parse(xml);
        Assert.Equal(new[] { "inbound", "backend", "outbound", "on-error" },
            document.Root!.Elements().Select(e => e.Name.LocalName));

        var baseline = BuildFixture();
        for (var i = 0; i < WarmupBuilds; i++)
            AssertComplete(Build(xml), baseline, xml, lines);

        var milliseconds = new double[TimedBuilds];
        for (var i = 0; i < TimedBuilds; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            // Includes source loading, fragment pairing, Roslyn analysis, model/rules/state and integrity work.
            // No pre-parsed input, model cache, ARM latency, or assertions inside the timed interval.
            var model = Build(xml);
            stopwatch.Stop();
            milliseconds[i] = stopwatch.Elapsed.TotalMilliseconds;
            AssertComplete(model, baseline, xml, lines);
        }

        var ordered = milliseconds.Order().ToArray();
        // Nearest-rank percentiles: p50 = sample 10, p95 = sample 19 of 20 sorted observations.
        var p50 = ordered[(int)Math.Ceiling(TimedBuilds * 0.50) - 1];
        var p95 = ordered[(int)Math.Ceiling(TimedBuilds * 0.95) - 1];
        output.WriteLine($"SPF-NF-01: {bytes} UTF-8 bytes, {lines} lines; {WarmupBuilds} warm-ups, {TimedBuilds} timed full-pipeline builds.");
        output.WriteLine($"Machine: {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}; {Environment.ProcessorCount} logical processors; {RuntimeInformation.ProcessArchitecture}.");
        output.WriteLine($"Samples (ms): {string.Join(", ", milliseconds.Select(ms => ms.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}");
        output.WriteLine($"Nearest-rank p50={p50:F2} ms; p95={p95:F2} ms; backend p95 budget=1000 ms (ARM excluded).");
        Assert.True(p95 <= 1000, $"SPF-NF-01 exceeded: p50={p50:F2} ms, p95={p95:F2} ms over {TimedBuilds} builds; budget=1000 ms.");
    }

    private static string BuildLargePolicy()
    {
        var fixture = FixtureXml().ReplaceLineEndings("\n");
        var start = fixture.IndexOf("<inbound>", StringComparison.Ordinal);
        var end = fixture.IndexOf("</inbound>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Synthetic fixture must have a complete inbound section.");
        var inbound = fixture[(start + "<inbound>".Length)..end];
        Assert.Contains("include-fragment: Begin", inbound);
        // Preserve whole sibling regions (including nested ones), not whole <inbound> sections.
        // Three comment newlines/copy keep this near 2,500 lines; inert padding brings it near 300 KiB.
        var padding = new string('x', 9400);
        var copies = new StringBuilder();
        for (var i = 0; i < AdditionalInboundCopies; i++)
            copies.Append("\n<!-- Synthetic copy ").Append(i + 1).Append('\n').Append(padding).Append("\n-->").Append(inbound);
        return fixture[..end] + copies + fixture[end..];
    }

    private static void AssertComplete(EffectivePolicyFlowModel model, EffectivePolicyFlowModel baseline, string xml, int lines)
    {
        AssertIntegrity(model);
        Assert.Equal(xml, model.Source.Text);
        Assert.Equal(lines, model.Source.LineCount);
        Assert.DoesNotContain(model.Diagnostics, d => d.Severity == FlowDiagnosticSeverities.Error ||
            d.Code.Contains("fragment", StringComparison.Ordinal) || d.Code == "expression-parse-error");
        Assert.Equal(baseline.Elements.Count + AdditionalInboundCopies * baseline.Elements.Count(e => e.Stage == "inbound"),
            model.Elements.Count);
        foreach (var kind in baseline.Elements.Select(e => e.Kind).Distinct())
        {
            Assert.Equal(baseline.Elements.Count(e => e.Kind == kind) +
                AdditionalInboundCopies * baseline.Elements.Count(e => e.Stage == "inbound" && e.Kind == kind),
                model.Elements.Count(e => e.Kind == kind));
        }
        foreach (var kind in new[] { FlowEdgeKinds.Branch, FlowEdgeKinds.Bypass, FlowEdgeKinds.NoMatch, FlowEdgeKinds.ExplicitResponse })
        {
            Assert.Equal(baseline.OfKind(kind).Count() +
                AdditionalInboundCopies * baseline.OfKind(kind).Count(e => baseline.Element(e.From).Stage == "inbound"),
                model.OfKind(kind).Count());
        }
        Assert.Equal(baseline.OfKind(FlowEdgeKinds.LoopBack).Count(), model.OfKind(FlowEdgeKinds.LoopBack).Count());
        Assert.Equal(3, model.OfKind(FlowEdgeKinds.StageException).Count());
        Assert.Equal(2, model.OfKind(FlowEdgeKinds.Preflight).Count());
        Assert.Equal(AdditionalInboundCopies + 1, model.OfKind(FlowEdgeKinds.DataDependency).Count());
        Assert.Equal(AdditionalInboundCopies + 1, model.Elements.Count(e => e.Fragment?.Name == "model-resolution"));
    }
}
