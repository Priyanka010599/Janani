using Janani.Data;
using Janani.Models;
using Janani.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Janani.Tests;

public class PerUserPregnancyDeviceAuthenticatorTests
{
    // A fresh, uniquely-named InMemory database per test — sharing one
    // instance across tests (e.g. by database name) would leak UserProfile
    // rows between them since InMemory databases persist for the life of
    // the name, not the DbContext instance.
    private static AppDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task MatchingToken_Authorizes()
    {
        await using var db = NewDb();
        db.UserProfiles.Add(new UserProfile { UserId = 1, DevicePairingToken = "correct-token" });
        await db.SaveChangesAsync();

        var authenticator = new PerUserPregnancyDeviceAuthenticator(db);
        Assert.True(await authenticator.AuthorizeAsync(1, "correct-token", CancellationToken.None));
    }

    [Fact]
    public async Task MismatchedToken_Denies()
    {
        await using var db = NewDb();
        db.UserProfiles.Add(new UserProfile { UserId = 1, DevicePairingToken = "correct-token" });
        await db.SaveChangesAsync();

        var authenticator = new PerUserPregnancyDeviceAuthenticator(db);
        Assert.False(await authenticator.AuthorizeAsync(1, "wrong-token", CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingOrEmptyPresentedToken_Denies(string? presentedToken)
    {
        await using var db = NewDb();
        db.UserProfiles.Add(new UserProfile { UserId = 1, DevicePairingToken = "correct-token" });
        await db.SaveChangesAsync();

        var authenticator = new PerUserPregnancyDeviceAuthenticator(db);
        Assert.False(await authenticator.AuthorizeAsync(1, presentedToken, CancellationToken.None));
    }

    [Fact]
    public async Task NoMatchingUserProfile_Denies()
    {
        await using var db = NewDb();
        // No UserProfile row for UserId 1 at all.

        var authenticator = new PerUserPregnancyDeviceAuthenticator(db);
        Assert.False(await authenticator.AuthorizeAsync(1, "any-token", CancellationToken.None));
    }
}
