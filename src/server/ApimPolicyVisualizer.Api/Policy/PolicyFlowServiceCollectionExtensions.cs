using ApimPolicyVisualizer.Api.Policy.Expressions;
using ApimPolicyVisualizer.Api.Policy.Model;
using ApimPolicyVisualizer.Api.Policy.Source;

namespace ApimPolicyVisualizer.Api.Policy;

public static class PolicyFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the stateless schemaVersion 2 pipeline: source loader, fragment regions, syntax-only expression analyzer,
    /// APIM rule table, state analyzer and model builder. All are request-agnostic and hold no policy content.
    /// </summary>
    public static IServiceCollection AddPolicyFlowModel(this IServiceCollection services)
    {
        services.AddSingleton<IPolicySourceLoader, PolicySourceLoader>();
        services.AddSingleton<IFragmentRegionBuilder, FragmentRegionBuilder>();
        services.AddSingleton<IPolicyExpressionAnalyzer, PolicyExpressionAnalyzer>();
        services.AddSingleton<ApimSemanticRules>();
        services.AddSingleton<PolicyStateAnalyzer>();
        services.AddSingleton<IPolicyFlowModelBuilder, PolicyFlowModelBuilder>();
        return services;
    }
}
