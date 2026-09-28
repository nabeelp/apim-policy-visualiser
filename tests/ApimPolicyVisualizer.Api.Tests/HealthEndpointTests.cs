using System.Net;
using System.Net.Http.Json;
using ApimPolicyVisualizer.Api.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ApimPolicyVisualizer.Api.Tests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed record HealthResponse(string Status, ApimSummary Apim);

    private sealed record ApimSummary(string? SubscriptionId, string? ResourceGroupName, string ServiceName);

    [Fact]
    public async Task Healthz_Returns200_WithDefaultServiceName()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
        Assert.Equal(ApimOptions.DefaultServiceName, body.Apim.ServiceName);
        Assert.Equal("apim-zlyway6g7icoy", body.Apim.ServiceName);
    }

    [Fact]
    public async Task Healthz_ReflectsExplicitConfiguration()
    {
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Apim:SubscriptionId", "00000000-0000-0000-0000-000000000001");
            builder.UseSetting("Apim:ResourceGroupName", "rg-test");
            builder.UseSetting("Apim:ServiceName", "apim-custom");
        });
        using var client = configured.CreateClient();

        var body = await client.GetFromJsonAsync<HealthResponse>("/healthz");

        Assert.NotNull(body);
        Assert.Equal("00000000-0000-0000-0000-000000000001", body.Apim.SubscriptionId);
        Assert.Equal("rg-test", body.Apim.ResourceGroupName);
        Assert.Equal("apim-custom", body.Apim.ServiceName);
    }

    [Fact]
    public void ApimOptions_DefaultsServiceName_WhenUnset()
    {
        Assert.Equal("apim-zlyway6g7icoy", new ApimOptions().ServiceName);
    }
}
