using Janani.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Janani.Tests;

public class GlobalElderDeviceAuthenticatorTests
{
    private const string EnvVar = "DEVICE_INGEST_TOKEN";

    // Minimal stand-in for IHostEnvironment — only EnvironmentName is read
    // (via the IsDevelopment() extension), so the rest stays defaulted.
    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Janani.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // Literals, not Environments.Development/Production — those are static
    // readonly fields, not consts, so they can't be default parameter values.
    private const string Development = "Development";
    private const string Production = "Production";

    private static async Task<bool> AuthorizeWithEnvAsync(
        string? configuredToken, string? presentedToken, string environmentName = Development)
    {
        var original = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, configuredToken);
            var authenticator = new GlobalElderDeviceAuthenticator(
                new StubEnvironment(environmentName),
                NullLogger<GlobalElderDeviceAuthenticator>.Instance);
            return await authenticator.AuthorizeAsync(elderId: 1, presentedToken, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, original);
        }
    }

    // The unauthenticated path is a deliberate local-demo convenience
    // (edge-device-sim/ POSTs with no token) — but only in Development.
    [Fact]
    public async Task NoTokenConfigured_InDevelopment_AuthorizesRegardlessOfPresentedToken()
    {
        Assert.True(await AuthorizeWithEnvAsync(configuredToken: null, presentedToken: null));
        Assert.True(await AuthorizeWithEnvAsync(configuredToken: null, presentedToken: "anything"));
    }

    // Outside Development an unset token is a misconfiguration, not an
    // invitation — an open ingest endpoint lets anyone write to a real
    // elder's clinical record and fire Critical alerts at their caregivers.
    [Fact]
    public async Task NoTokenConfigured_OutsideDevelopment_Denies()
    {
        Assert.False(await AuthorizeWithEnvAsync(
            configuredToken: null, presentedToken: null, environmentName: Production));
        Assert.False(await AuthorizeWithEnvAsync(
            configuredToken: null, presentedToken: "anything", environmentName: Production));
    }

    [Fact]
    public async Task TokenConfigured_MatchingPresentedToken_Authorizes()
    {
        Assert.True(await AuthorizeWithEnvAsync(configuredToken: "secret", presentedToken: "secret"));
        Assert.True(await AuthorizeWithEnvAsync(
            configuredToken: "secret", presentedToken: "secret", environmentName: Production));
    }

    [Fact]
    public async Task TokenConfigured_MismatchedPresentedToken_Denies()
    {
        Assert.False(await AuthorizeWithEnvAsync(configuredToken: "secret", presentedToken: "wrong"));
    }

    // A different length takes the early-out branch rather than FixedTimeEquals.
    [Fact]
    public async Task TokenConfigured_DifferentLengthPresentedToken_Denies()
    {
        Assert.False(await AuthorizeWithEnvAsync(configuredToken: "secret", presentedToken: "secretsecret"));
    }

    [Fact]
    public async Task TokenConfigured_MissingPresentedToken_Denies()
    {
        Assert.False(await AuthorizeWithEnvAsync(configuredToken: "secret", presentedToken: null));
    }
}
