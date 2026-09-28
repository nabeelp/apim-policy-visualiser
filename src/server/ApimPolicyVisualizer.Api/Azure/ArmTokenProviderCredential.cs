using Azure.Core;

namespace ApimPolicyVisualizer.Api.Azure;

/// <summary>
/// Adapts <see cref="IArmTokenProvider"/> to <see cref="TokenCredential"/> so Azure SDK clients (ArmClient)
/// reuse the single cached ARM token source instead of acquiring tokens independently.
/// </summary>
public sealed class ArmTokenProviderCredential(IArmTokenProvider tokenProvider, TimeProvider timeProvider) : TokenCredential
{
    // The provider already refreshes 5 minutes before real expiry, so a short advertised lifetime only
    // makes the SDK re-ask the provider (a cache hit) periodically rather than holding its own copy.
    private static readonly TimeSpan AdvertisedLifetime = TimeSpan.FromMinutes(10);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        return new AccessToken(token, timeProvider.GetUtcNow() + AdvertisedLifetime);
    }
}
