using ApimPolicyVisualizer.Api.Policy.Model;

namespace ApimPolicyVisualizer.Api.Tests;

/// <summary>Shared helpers for schemaVersion 2 model tests. Fixtures are synthetic; never live policy content.</summary>
internal static class PolicyModelTestHelpers
{
    public static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ai-gateway-synthetic.xml");

    public static string FixtureXml() => File.ReadAllText(FixturePath);

    public static EffectivePolicyFlowModel Build(string xml) => PolicyFlowModelBuilder.CreateDefault().BuildFromXml(xml);

    public static EffectivePolicyFlowModel BuildFixture() => Build(FixtureXml());

    /// <summary>Wraps section content into a full policy document.</summary>
    public static string PolicyXml(string inbound = "<base />", string? backend = "<forward-request />", string? outbound = "<base />", string? onError = null)
    {
        var xml = $"<policies><inbound>{inbound}</inbound>";
        if (backend is not null)
        {
            xml += $"<backend>{backend}</backend>";
        }

        if (outbound is not null)
        {
            xml += $"<outbound>{outbound}</outbound>";
        }

        if (onError is not null)
        {
            xml += $"<on-error>{onError}</on-error>";
        }

        return xml + "</policies>";
    }

    public static FlowElement Element(this EffectivePolicyFlowModel model, string id) =>
        model.Elements.SingleOrDefault(e => e.Id == id) ?? throw new Xunit.Sdk.XunitException($"No element '{id}'. IDs: {string.Join(", ", model.Elements.Select(e => e.Id))}");

    public static FlowElement ByTag(this EffectivePolicyFlowModel model, string tag) =>
        Assert.Single(model.Elements, e => e.Tag == tag);

    public static IEnumerable<FlowEdge> From(this EffectivePolicyFlowModel model, string id) => model.Edges.Where(e => e.From == id);

    public static IEnumerable<FlowEdge> To(this EffectivePolicyFlowModel model, string id) => model.Edges.Where(e => e.To == id);

    public static IEnumerable<FlowEdge> OfKind(this EffectivePolicyFlowModel model, string kind) => model.Edges.Where(e => e.Kind == kind);

    public static bool IsDescendantOf(this EffectivePolicyFlowModel model, string id, string ancestorId)
    {
        var byId = model.Elements.ToDictionary(e => e.Id);
        var current = byId[id].ParentId;
        while (current is not null)
        {
            if (current == ancestorId)
            {
                return true;
            }

            current = byId[current].ParentId;
        }

        return false;
    }

    /// <summary>Unique IDs; every parentId and edge endpoint resolves; no integrity diagnostics.</summary>
    public static void AssertIntegrity(EffectivePolicyFlowModel model)
    {
        var ids = model.Elements.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        var set = ids.ToHashSet(StringComparer.Ordinal);
        Assert.All(model.Elements, e => Assert.True(e.ParentId is null || set.Contains(e.ParentId), $"parent of {e.Id} missing"));
        Assert.All(model.Edges, e => Assert.True(set.Contains(e.From) && set.Contains(e.To), $"edge {e.Id} dangling"));
        Assert.Equal(model.Edges.Count, model.Edges.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(model.Diagnostics, d => d.Code == "model-integrity");
        Assert.All(model.Elements.SelectMany(e => e.Facts), f => Assert.False(string.IsNullOrEmpty(f.Provenance)));
    }
}
