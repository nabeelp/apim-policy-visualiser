using ApimPolicyVisualizer.Api.Azure;
using ApimPolicyVisualizer.Api.Configuration;
using ApimPolicyVisualizer.Api.Policy;
using ApimPolicyVisualizer.Api.Scopes;
using Azure.ResourceManager;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ApimOptions>()
    .Bind(builder.Configuration.GetSection(ApimOptions.SectionName))
    .PostConfigure(options => options.Normalize());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddSingleton<IArmTokenProvider, ArmTokenProvider>();

builder.Services.AddSingleton(sp => new ArmClient(
    new ArmTokenProviderCredential(sp.GetRequiredService<IArmTokenProvider>(), sp.GetRequiredService<TimeProvider>())));
builder.Services.AddSingleton<IApimCatalogClient, ArmApimCatalogClient>();
builder.Services.AddHttpClient(ResourceGraphApimServiceLocator.HttpClientName);
builder.Services.AddSingleton<IApimServiceLocator, ResourceGraphApimServiceLocator>();
builder.Services.AddSingleton<IScopeDiscoveryService, ScopeDiscoveryService>();
builder.Services.AddHttpClient(EffectivePolicyClient.HttpClientName);
builder.Services.AddSingleton<IEffectivePolicyClient, EffectivePolicyClient>();
builder.Services.AddPolicyFlowModel();

builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapGet("/healthz", (IOptions<ApimOptions> apimOptions) =>
{
    var apim = apimOptions.Value;
    return Results.Ok(new
    {
        status = "Healthy",
        apim = new
        {
            subscriptionId = apim.SubscriptionId,
            resourceGroupName = apim.ResourceGroupName,
            serviceName = apim.ServiceName,
        },
    });
});

app.MapControllers();

app.Run();

public partial class Program;
