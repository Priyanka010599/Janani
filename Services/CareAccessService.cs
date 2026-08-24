// Services/CareAccessService.cs
// Central place for "who can see/act on this elder or infant profile" —
// every page that lists or queries elder/infant data goes through
// GetAccessibleElderIdsAsync/GetAccessibleInfantIdsAsync instead of a raw
// `Where(e => e.UserId == userId)`, so a shared caregiver shows up
// everywhere the owner does (Elder/Infant Care, Family Care, Emergency,
// the caregiver Q&A agent, exports) without each page re-implementing the
// owner-or-shared check. Granting/revoking only ever goes through the
// profile's actual owner (ElderProfile.UserId/InfantProfile.UserId) —
// sharing is additive access, not transferable ownership.

using Janani.Data;
using Janani.Models;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public record ShareInfo(int ShareId, int UserId, string Username, DateTime GrantedAt);

public class CareAccessService(AppDbContext db)
{
    public async Task<List<int>> GetAccessibleElderIdsAsync(int userId, CancellationToken ct = default)
    {
        var owned = await db.ElderProfiles.AsNoTracking()
            .Where(e => e.UserId == userId).Select(e => e.Id).ToListAsync(ct);
        var shared = await db.SharedCareAccess.AsNoTracking()
            .Where(s => s.UserId == userId && s.ElderProfileId != null)
            .Select(s => s.ElderProfileId!.Value).ToListAsync(ct);
        return owned.Union(shared).ToList();
    }

    public async Task<List<int>> GetAccessibleInfantIdsAsync(int userId, CancellationToken ct = default)
    {
        var owned = await db.InfantProfiles.AsNoTracking()
            .Where(i => i.UserId == userId).Select(i => i.Id).ToListAsync(ct);
        var shared = await db.SharedCareAccess.AsNoTracking()
            .Where(s => s.UserId == userId && s.InfantProfileId != null)
            .Select(s => s.InfantProfileId!.Value).ToListAsync(ct);
        return owned.Union(shared).ToList();
    }

    // Everyone who should hear about an alert on this elder/infant — the
    // owner plus every caregiver they've shared access with.
    public async Task<List<int>> GetCaregiverUserIdsForElderAsync(int elderId, CancellationToken ct = default)
    {
        var owner = await db.ElderProfiles.AsNoTracking()
            .Where(e => e.Id == elderId).Select(e => (int?)e.UserId).FirstOrDefaultAsync(ct);
        var shared = await db.SharedCareAccess.AsNoTracking()
            .Where(s => s.ElderProfileId == elderId).Select(s => s.UserId).ToListAsync(ct);
        return owner is int ownerId ? shared.Append(ownerId).Distinct().ToList() : shared;
    }

    public async Task<List<int>> GetCaregiverUserIdsForInfantAsync(int infantId, CancellationToken ct = default)
    {
        var owner = await db.InfantProfiles.AsNoTracking()
            .Where(i => i.Id == infantId).Select(i => (int?)i.UserId).FirstOrDefaultAsync(ct);
        var shared = await db.SharedCareAccess.AsNoTracking()
            .Where(s => s.InfantProfileId == infantId).Select(s => s.UserId).ToListAsync(ct);
        return owner is int ownerId ? shared.Append(ownerId).Distinct().ToList() : shared;
    }

    public async Task<string?> GrantElderAccessAsync(int elderId, string granteeUsername, int grantedByUserId, CancellationToken ct = default)
    {
        var isOwner = await db.ElderProfiles.AnyAsync(e => e.Id == elderId && e.UserId == grantedByUserId, ct);
        if (!isOwner) return "Only the primary caregiver can share access.";

        var grantee = await db.Users.FirstOrDefaultAsync(u => u.Username == granteeUsername.Trim(), ct);
        if (grantee == null) return $"No Janani account found with the username \"{granteeUsername}\".";
        if (grantee.Id == grantedByUserId) return "That's already you.";

        var exists = await db.SharedCareAccess.AnyAsync(s => s.ElderProfileId == elderId && s.UserId == grantee.Id, ct);
        if (exists) return $"{grantee.Username} already has access.";

        db.SharedCareAccess.Add(new SharedCareAccess { ElderProfileId = elderId, UserId = grantee.Id, GrantedByUserId = grantedByUserId });
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> GrantInfantAccessAsync(int infantId, string granteeUsername, int grantedByUserId, CancellationToken ct = default)
    {
        var isOwner = await db.InfantProfiles.AnyAsync(i => i.Id == infantId && i.UserId == grantedByUserId, ct);
        if (!isOwner) return "Only the primary caregiver can share access.";

        var grantee = await db.Users.FirstOrDefaultAsync(u => u.Username == granteeUsername.Trim(), ct);
        if (grantee == null) return $"No Janani account found with the username \"{granteeUsername}\".";
        if (grantee.Id == grantedByUserId) return "That's already you.";

        var exists = await db.SharedCareAccess.AnyAsync(s => s.InfantProfileId == infantId && s.UserId == grantee.Id, ct);
        if (exists) return $"{grantee.Username} already has access.";

        db.SharedCareAccess.Add(new SharedCareAccess { InfantProfileId = infantId, UserId = grantee.Id, GrantedByUserId = grantedByUserId });
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task RevokeAsync(int shareId, int requestingUserId, CancellationToken ct = default)
    {
        var share = await db.SharedCareAccess.FirstOrDefaultAsync(s => s.Id == shareId, ct);
        if (share == null) return;

        var isOwner = share.ElderProfileId is int elderId
            ? await db.ElderProfiles.AnyAsync(e => e.Id == elderId && e.UserId == requestingUserId, ct)
            : share.InfantProfileId is int infantId
                ? await db.InfantProfiles.AnyAsync(i => i.Id == infantId && i.UserId == requestingUserId, ct)
                : false;
        if (!isOwner) return;

        db.SharedCareAccess.Remove(share);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ShareInfo>> GetSharesForElderAsync(int elderId, CancellationToken ct = default) =>
        await db.SharedCareAccess.AsNoTracking()
            .Where(s => s.ElderProfileId == elderId)
            .Join(db.Users, s => s.UserId, u => u.Id, (s, u) => new ShareInfo(s.Id, u.Id, u.Username, s.GrantedAt))
            .ToListAsync(ct);

    public async Task<List<ShareInfo>> GetSharesForInfantAsync(int infantId, CancellationToken ct = default) =>
        await db.SharedCareAccess.AsNoTracking()
            .Where(s => s.InfantProfileId == infantId)
            .Join(db.Users, s => s.UserId, u => u.Id, (s, u) => new ShareInfo(s.Id, u.Id, u.Username, s.GrantedAt))
            .ToListAsync(ct);
}
