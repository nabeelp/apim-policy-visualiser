using Azure.Core;

namespace ApimPolicyVisualizer.Api.Azure;

/// <summary>
/// Supplies bearer tokens for Azure Resource Manager calls.
/// </summary>
public interface IArmTokenProvider
{
    ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Acquires ARM bearer tokens from a <see cref="TokenCredential"/> (DefaultAzureCredential in production)
/// and caches them in memory until they are within <see cref="RefreshWindow"/> of expiry.
/// Token values are never logged or persisted.
/// </summary>
public sealed class ArmTokenProvider : IArmTokenProvider
{
    public const string ArmScope = "https://management.azure.com/.default";
    public static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);

    private readonly TokenCredential _credential;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ArmTokenProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile CachedToken? _cachedToken;

    public ArmTokenProvider(TokenCredential credential, TimeProvider timeProvider, ILogger<ArmTokenProvider> logger)
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetValidCachedToken(out var cached))
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetValidCachedToken(out cached))
            {
                return cached;
            }

            _logger.LogDebug("Acquiring Azure Resource Manager access token for scope {Scope}.", ArmScope);

            AccessToken token;
            try
            {
                token = await _credential
                    .GetTokenAsync(new TokenRequestContext([ArmScope]), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Only the exception type is logged so no credential detail can leak into log output.
                _logger.LogError(
                    "Failed to acquire Azure Resource Manager access token ({ExceptionType}). Ensure 'az login' has been run or another DefaultAzureCredential source is available.",
                    ex.GetType().Name);
                throw;
            }

            _cachedToken = new CachedToken(token.Token, token.ExpiresOn);
            _logger.LogInformation(
                "Acquired Azure Resource Manager access token; expires at {ExpiresOn:O}.",
                token.ExpiresOn);

            return token.Token;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool TryGetValidCachedToken(out string token)
    {
        if (_cachedToken is { } value && _timeProvider.GetUtcNow() < value.ExpiresOn - RefreshWindow)
        {
            token = value.Token;
            return true;
        }

        token = string.Empty;
        return false;
    }

    // Plain class (not a record) so the token is never exposed through a generated ToString().
    private sealed class CachedToken(string token, DateTimeOffset expiresOn)
    {
        public string Token { get; } = token;

        public DateTimeOffset ExpiresOn { get; } = expiresOn;
    }
}
