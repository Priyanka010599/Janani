using Janani.Services;
using Xunit;

namespace Janani.Tests;

public class GlobalElderDeviceAuthenticatorTests
{
    private const string EnvVar = "DEVICE_INGEST_TOKEN";

    private static async Task<bool> AuthorizeWithEnvAsync(string? configuredToken, string? presentedToken)
    {
        var original = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, configuredToken);
            var authenticator = new GlobalElderDeviceAuthenticator();
            return await authenticator.AuthorizeAsync(elderId: 1, presentedToken, CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, original);
        }
    }

    [Fact]
    public async Task NoTokenConfigured_AuthorizesRegardlessOfPresentedToken()
    {
        Assert.True(await AuthorizeWithEnvAsync(configuredToken: null, presentedToken: null));
        Assert.True(await AuthorizeWithEnvAsync(configuredToken: null, presentedToken: "anything"));
    }

    [Fact]
    public async Task TokenConfigured_MatchingPresentedToken_Authorizes()
    {
        Assert.True(await AuthorizeWithEnvAsync(configuredToken: "secret", presentedToken: "secret"));
    }

    [Fact]
    public async Task TokenConfigured_MismatchedPresentedToken_Denies()
    {
        Assert.False(await AuthorizeWithEnvAsync(configuredToken: "secret", presentedToken: "wrong"));
    }

    [Fact]
    public async Task TokenConfigured_MissingPresentedToken_Denies()
    {
        Assert.False(await AuthorizeWithEnvAsync(configuredToken: "secret", presentedToken: null));
    }
}
