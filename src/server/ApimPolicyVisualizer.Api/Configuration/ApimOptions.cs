namespace ApimPolicyVisualizer.Api.Configuration;

/// <summary>
/// Identifies the target Azure API Management service. Contains identifiers only — never credentials.
/// </summary>
public sealed class ApimOptions
{
    public const string SectionName = "Apim";
    public const string DefaultServiceName = "apim-zlyway6g7icoy";

    /// <summary>Optional; when unset it is auto-resolved via Azure Resource Graph by service name.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Optional; when unset it is auto-resolved via Azure Resource Graph by service name.</summary>
    public string? ResourceGroupName { get; set; }

    public string ServiceName { get; set; } = DefaultServiceName;

    /// <summary>Treats blank values as unset so empty appsettings entries fall back to defaults.</summary>
    public void Normalize()
    {
        SubscriptionId = string.IsNullOrWhiteSpace(SubscriptionId) ? null : SubscriptionId.Trim();
        ResourceGroupName = string.IsNullOrWhiteSpace(ResourceGroupName) ? null : ResourceGroupName.Trim();
        ServiceName = string.IsNullOrWhiteSpace(ServiceName) ? DefaultServiceName : ServiceName.Trim();
    }
}
