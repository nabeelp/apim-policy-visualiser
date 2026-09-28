using System.Collections.Concurrent;
using System.Diagnostics;
using ApimPolicyVisualizer.Api.Azure;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApimPolicyVisualizer.Api.Tests;

public class ArmTokenProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RequestsTokenForArmScope()
    {
        var credential = new FakeCredential(new AccessToken("token-1", Start.AddHours(1)));
        var provider = CreateProvider(credential, new ManualTimeProvider(Start));

        var token = await provider.GetTokenAsync();

        Assert.Equal("token-1", token);
        Assert.Equal(["https://management.azure.com/.default"], credential.RequestedScopes.Single());
    }

    [Fact]
    public async Task ReusesCachedTokenUntilWithinFiveMinutesOfExpiry()
    {
        var credential = new FakeCredential(
            new AccessToken("token-1", Start.AddMinutes(60)),
            new AccessToken("token-2", Start.AddMinutes(120)));
        var clock = new ManualTimeProvider(Start);
        var provider = CreateProvider(credential, clock);

        Assert.Equal("token-1", await provider.GetTokenAsync());

        clock.Now = Start.AddMinutes(30);
        Assert.Equal("token-1", await provider.GetTokenAsync());

        // One tick before the 5-minute refresh window starts: still cached.
        clock.Now = Start.AddMinutes(55).AddTicks(-1);
        Assert.Equal("token-1", await provider.GetTokenAsync());
        Assert.Equal(1, credential.CallCount);
    }

    [Fact]
    public async Task RefreshesTokenOnceWithinFiveMinutesOfExpiry()
    {
        var credential = new FakeCredential(
            new AccessToken("token-1", Start.AddMinutes(60)),
            new AccessToken("token-2", Start.AddMinutes(120)));
        var clock = new ManualTimeProvider(Start);
        var provider = CreateProvider(credential, clock);

        Assert.Equal("token-1", await provider.GetTokenAsync());

        clock.Now = Start.AddMinutes(55);
        Assert.Equal("token-2", await provider.GetTokenAsync());
        Assert.Equal(2, credential.CallCount);

        clock.Now = Start.AddMinutes(90);
        Assert.Equal("token-2", await provider.GetTokenAsync());
        Assert.Equal(2, credential.CallCount);
    }

    [Fact]
    public async Task RefreshesExpiredToken()
    {
        var credential = new FakeCredential(
            new AccessToken("token-1", Start.AddMinutes(60)),
            new AccessToken("token-2", Start.AddMinutes(180)));
        var clock = new ManualTimeProvider(Start);
        var provider = CreateProvider(credential, clock);

        await provider.GetTokenAsync();
        clock.Now = Start.AddMinutes(61);

        Assert.Equal("token-2", await provider.GetTokenAsync());
        Assert.Equal(2, credential.CallCount);
    }

    [Fact]
    public async Task ConcurrentCallersShareSingleAcquisition()
    {
        var credential = new FakeCredential(new AccessToken("token-1", Start.AddHours(1)))
        {
            Delay = TimeSpan.FromMilliseconds(50),
        };
        var provider = CreateProvider(credential, new ManualTimeProvider(Start));

        var tokens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => provider.GetTokenAsync().AsTask()));

        Assert.All(tokens, t => Assert.Equal("token-1", t));
        Assert.Equal(1, credential.CallCount);
    }

    [Fact]
    public async Task DoesNotWriteTokenValueToLogs()
    {
        const string secretToken = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.super-secret-token-value";
        var credential = new FakeCredential(
            new AccessToken(secretToken, Start.AddMinutes(60)),
            new AccessToken(secretToken + "-refreshed", Start.AddMinutes(120)));
        var clock = new ManualTimeProvider(Start);
        var logger = new CapturingLogger<ArmTokenProvider>();
        var provider = new ArmTokenProvider(credential, clock, logger);

        await provider.GetTokenAsync();
        clock.Now = Start.AddMinutes(56);
        await provider.GetTokenAsync();

        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry => Assert.DoesNotContain("super-secret-token-value", entry));
    }

    [Fact]
    public async Task DoesNotWriteCredentialFailureDetailsToLogs()
    {
        const string sensitiveDetail = "client-secret-abc123";
        var credential = new FakeCredential { Failure = new AuthenticationFailedException($"failure {sensitiveDetail}") };
        var logger = new CapturingLogger<ArmTokenProvider>();
        var provider = new ArmTokenProvider(credential, new ManualTimeProvider(Start), logger);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => provider.GetTokenAsync().AsTask());

        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(sensitiveDetail, entry));
    }

    [Fact]
    public void ApplicationRegistersProviderBackedByDefaultAzureCredential()
    {
        using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();

        Assert.IsType<DefaultAzureCredential>(factory.Services.GetRequiredService<TokenCredential>());
        Assert.IsType<ArmTokenProvider>(factory.Services.GetRequiredService<IArmTokenProvider>());
    }

    [SkippableFact]
    public async Task Integration_AcquiresTokenFromAzCliLoginContext()
    {
        Skip.IfNot(IsAzCliLoggedIn(), "Azure CLI is not installed or not logged in; integration check is inconclusive.");

        var provider = new ArmTokenProvider(
            new DefaultAzureCredential(),
            TimeProvider.System,
            NullLogger<ArmTokenProvider>.Instance);

        string token;
        try
        {
            token = await provider.GetTokenAsync();
        }
        catch (AuthenticationFailedException ex)
        {
            throw new SkipException($"DefaultAzureCredential could not authenticate in this environment: {ex.GetType().Name}");
        }

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(token, await provider.GetTokenAsync());
    }

    private static ArmTokenProvider CreateProvider(TokenCredential credential, TimeProvider clock) =>
        new(credential, clock, NullLogger<ArmTokenProvider>.Instance);

    private static bool IsAzCliLoggedIn()
    {
        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd.exe", "/c az account show --output none")
                : new ProcessStartInfo("az", "account show --output none");
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeCredential(params AccessToken[] tokens) : TokenCredential
    {
        private readonly Queue<AccessToken> _tokens = new(tokens);
        private int _callCount;

        public ConcurrentBag<string[]> RequestedScopes { get; } = [];

        public int CallCount => _callCount;

        public TimeSpan Delay { get; init; }

        public Exception? Failure { get; init; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            RequestedScopes.Add(requestContext.Scopes);

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            lock (_tokens)
            {
                return _tokens.Dequeue();
            }
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
        }
    }
}
