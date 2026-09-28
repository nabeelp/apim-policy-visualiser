using System.Text.Json;

namespace ApimPolicyVisualizer.Api.Tests;

/// <summary>
/// Keeps the frontend's backend-generated contract fixture (src/web/src/flow/__fixtures__/backendExampleModel.json)
/// identical to what the API serializes for the synthetic fixture, so frontend projection/rendering tests run against
/// the real model shape. Regenerate with UPDATE_CONTRACT_FIXTURE=1.
/// </summary>
public sealed class FrontendContractFixtureTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string FrontendFixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "web")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "web", "src", "flow", "__fixtures__", "backendExampleModel.json");
    }

    [Fact]
    public void FrontendContractFixtureMatchesSerializedSyntheticModel()
    {
        var model = ApimPolicyVisualizer.Api.Policy.Model.PolicyFlowModelBuilder.CreateDefault()
            .BuildFromXml(PolicyModelTestHelpers.FixtureXml().ReplaceLineEndings("\n"), "apis/example", "api");
        var json = JsonSerializer.Serialize(model, WebJson).ReplaceLineEndings("\n") + "\n";

        var path = FrontendFixturePath();
        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACT_FIXTURE") == "1")
        {
            File.WriteAllText(path, json);
        }

        Assert.True(File.Exists(path), $"Missing {path}; run with UPDATE_CONTRACT_FIXTURE=1.");
        Assert.True(
            File.ReadAllText(path).ReplaceLineEndings("\n") == json,
            "backendExampleModel.json is stale; regenerate with UPDATE_CONTRACT_FIXTURE=1 and re-run frontend tests.");
    }
}
