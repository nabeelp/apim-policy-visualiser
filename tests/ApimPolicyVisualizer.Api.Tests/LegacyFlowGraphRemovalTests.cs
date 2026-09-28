using ApimPolicyVisualizer.Api.Policy;
using ApimPolicyVisualizer.Api.Policy.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ApimPolicyVisualizer.Api.Tests;

/// <summary>SPF-7: the legacy POL-2/POL-3 flow graph (parsers and PolicyFlowGraph shape) is gone.</summary>
public class LegacyFlowGraphRemovalTests
{
    private static readonly string[] LegacyTypeNames =
    [
        "PolicyFlowParser", "IPolicyFlowParser", "PolicyControlFlowParser", "IPolicyControlFlowParser",
        "PolicyFlowGraph", "PolicyFlowNode", "PolicyFlowEdge", "PolicySectionMetadata", "PolicyNodeKinds", "PolicyEdgeKinds",
        "EffectivePolicyFlowResponse",
    ];

    [Fact]
    public void LegacyTypes_AreAbsentFromTheApiAssembly()
    {
        var names = typeof(PolicyFlowController).Assembly.GetTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var legacy in LegacyTypeNames)
        {
            Assert.DoesNotContain(legacy, names);
        }
    }

    [Fact]
    public void LegacyParserTestSuites_AreAbsent()
    {
        var names = typeof(LegacyFlowGraphRemovalTests).Assembly.GetTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("PolicyFlowParserTests", names);
        Assert.DoesNotContain("PolicyControlFlowParserTests", names);
    }

    [Fact]
    public void Controller_ReturnsTheSchemaVersion2Model()
    {
        var method = typeof(PolicyFlowController).GetMethod(nameof(PolicyFlowController.GetEffectiveFlow))!;
        var produces = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute), false)
            .Cast<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>()
            .Single(a => a.StatusCode == 200);
        Assert.Equal(typeof(EffectivePolicyFlowModel), produces.Type);
        Assert.Equal(2, new EffectivePolicyFlowModel
        {
            ScopeId = "s", ScopeKind = "k", Source = new FlowSource("", 1), Stages = [], Elements = [], Edges = [], Variables = [], Diagnostics = [],
        }.SchemaVersion);
    }

    [Fact]
    public void NewPipelineServices_AreRegistered()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<ApimPolicyVisualizer.Api.Policy.Source.IPolicySourceLoader>());
        Assert.NotNull(scope.ServiceProvider.GetService<ApimPolicyVisualizer.Api.Policy.Source.IFragmentRegionBuilder>());
        Assert.NotNull(scope.ServiceProvider.GetService<ApimPolicyVisualizer.Api.Policy.Expressions.IPolicyExpressionAnalyzer>());
        Assert.NotNull(scope.ServiceProvider.GetService<IPolicyFlowModelBuilder>());
    }
}
