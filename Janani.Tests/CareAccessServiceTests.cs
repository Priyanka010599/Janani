using Janani.Data;
using Janani.Models;
using Janani.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Janani.Tests;

// Proves the specific property CareAccessService.cs's own header comment
// claims: per-caregiver opt-in access, no implicit household access.
// Written per external review, item 5 -- no test previously existed for
// this service at all.
public class CareAccessServiceTests
{
    private static AppDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Owner_CanAccessTheirOwnElder()
    {
        await using var db = NewDb();
        db.ElderProfiles.Add(new ElderProfile { Id = 1, UserId = 10, Name = "Lakshmi", Relation = "Mother" });
        await db.SaveChangesAsync();

        var access = new CareAccessService(db);
        var accessible = await access.GetAccessibleElderIdsAsync(10);

        Assert.Contains(1, accessible);
    }

    [Fact]
    public async Task UnrelatedUser_CannotAccessSomeoneElsesElder_NoImplicitHouseholdAccess()
    {
        await using var db = NewDb();
        // Two different owners, two different elders -- owning UserId 10's
        // own elder must never leak into UserId 99's accessible list, since
        // there is no SharedCareAccess row connecting them.
        db.ElderProfiles.Add(new ElderProfile { Id = 1, UserId = 10, Name = "Lakshmi", Relation = "Mother" });
        db.ElderProfiles.Add(new ElderProfile { Id = 2, UserId = 99, Name = "Ramesh", Relation = "Father" });
        await db.SaveChangesAsync();

        var access = new CareAccessService(db);
        var accessibleTo99 = await access.GetAccessibleElderIdsAsync(99);

        Assert.DoesNotContain(1, accessibleTo99);
        Assert.Contains(2, accessibleTo99); // sanity: 99 still sees their own
    }

    [Fact]
    public async Task UnrelatedUser_CannotAccessSomeoneElsesInfant()
    {
        await using var db = NewDb();
        db.InfantProfiles.Add(new InfantProfile { Id = 1, UserId = 10, Name = "Arjun", DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow) });
        await db.SaveChangesAsync();

        var access = new CareAccessService(db);
        var accessibleToOther = await access.GetAccessibleInfantIdsAsync(99);

        Assert.DoesNotContain(1, accessibleToOther);
    }

    [Fact]
    public async Task SharedCaregiver_CanAccessAfterExplicitGrant_ButOnlyThatOneElder()
    {
        await using var db = NewDb();
        db.ElderProfiles.Add(new ElderProfile { Id = 1, UserId = 10, Name = "Lakshmi", Relation = "Mother" });
        db.ElderProfiles.Add(new ElderProfile { Id = 2, UserId = 10, Name = "Suresh", Relation = "Father" }); // same owner, NOT shared
        db.SharedCareAccess.Add(new SharedCareAccess { ElderProfileId = 1, UserId = 20, GrantedByUserId = 10 });
        await db.SaveChangesAsync();

        var access = new CareAccessService(db);
        var accessibleTo20 = await access.GetAccessibleElderIdsAsync(20);

        Assert.Contains(1, accessibleTo20);
        Assert.DoesNotContain(2, accessibleTo20); // no implicit access to the owner's other elder
    }

    [Fact]
    public async Task NonOwner_CannotGrantAccessToAnElderTheyDontOwn()
    {
        await using var db = NewDb();
        db.ElderProfiles.Add(new ElderProfile { Id = 1, UserId = 10, Name = "Lakshmi", Relation = "Mother" });
        db.Users.Add(new User { Id = 30, Username = "someone_else", Email = "x@example.com", PasswordHash = "hash" });
        // UserId 20 is neither the owner (10) nor already shared -- an
        // attacker who somehow reached this code path with someone else's
        // elderId must not be able to grant themselves or a third party in.
        await db.SaveChangesAsync();

        var access = new CareAccessService(db);
        var error = await access.GrantElderAccessAsync(elderId: 1, granteeUsername: "someone_else", grantedByUserId: 20);

        Assert.NotNull(error); // rejected with an explanatory message, not silently ignored
        var stillAccessibleTo30 = await access.GetAccessibleElderIdsAsync(30);
        Assert.DoesNotContain(1, stillAccessibleTo30);
    }

    [Fact]
    public async Task NonOwner_CannotRevokeAnotherCaregiversShare()
    {
        await using var db = NewDb();
        db.ElderProfiles.Add(new ElderProfile { Id = 1, UserId = 10, Name = "Lakshmi", Relation = "Mother" });
        db.SharedCareAccess.Add(new SharedCareAccess { Id = 5, ElderProfileId = 1, UserId = 20, GrantedByUserId = 10 });
        await db.SaveChangesAsync();

        var access = new CareAccessService(db);
        // UserId 20 is the grantee, not the owner -- they shouldn't be able
        // to revoke their own (or anyone else's) share.
        await access.RevokeAsync(shareId: 5, requestingUserId: 20);

        var stillAccessible = await access.GetAccessibleElderIdsAsync(20);
        Assert.Contains(1, stillAccessible); // share survives the unauthorized revoke attempt
    }

    [Fact]
    public async Task Owner_CanRevokeAShareTheyGranted()
    {
        await using var db = NewDb();
        db.ElderProfiles.Add(new ElderProfile { Id = 1, UserId = 10, Name = "Lakshmi", Relation = "Mother" });
        db.SharedCareAccess.Add(new SharedCareAccess { Id = 5, ElderProfileId = 1, UserId = 20, GrantedByUserId = 10 });
        await db.SaveChangesAsync();

        var access = new CareAccessService(db);
        await access.RevokeAsync(shareId: 5, requestingUserId: 10);

        var accessibleTo20 = await access.GetAccessibleElderIdsAsync(20);
        Assert.DoesNotContain(1, accessibleTo20);
    }
}
