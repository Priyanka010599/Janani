// Services/DeviceAuth.cs
// Shared shape for the two device-ingest auth checks (elder vitals, pregnancy
// vitals) that were previously inline, near-identical-looking code in
// Program.cs. Deliberately NOT one interface with two interchangeable
// implementations: the two schemes are different trust models (a single
// global shared secret vs. a per-owner secret proven against the DB), so
// each gets its own interface rather than pretending they're swappable.
//
// GlobalElderDeviceAuthenticator's AuthorizeAsync ignores elderId entirely —
// that's the pre-existing behavior carried over as-is, not an oversight here.
// The elder scheme was never scoped per-elder (any holder of
// DEVICE_INGEST_TOKEN can post for any elderId); closing that gap would mean
// adding a per-elder pairing token (a schema change), which was explicitly
// deferred as a separate follow-up rather than folded into this abstraction.

using Janani.Data;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public interface IElderDeviceAuthenticator
{
    Task<bool> AuthorizeAsync(int elderId, string? presentedToken, CancellationToken ct);
}

public interface IPregnancyDeviceAuthenticator
{
    Task<bool> AuthorizeAsync(int userId, string? presentedToken, CancellationToken ct);
}

public class GlobalElderDeviceAuthenticator : IElderDeviceAuthenticator
{
    public Task<bool> AuthorizeAsync(int elderId, string? presentedToken, CancellationToken ct)
    {
        var expected = Environment.GetEnvironmentVariable("DEVICE_INGEST_TOKEN")?.Trim();
        // No token configured => auth check is a no-op, matching the
        // original inline behavior (opt-in, not required in every environment).
        if (string.IsNullOrEmpty(expected)) return Task.FromResult(true);
        return Task.FromResult(presentedToken == expected);
    }
}

public class PerUserPregnancyDeviceAuthenticator(AppDbContext db) : IPregnancyDeviceAuthenticator
{
    public async Task<bool> AuthorizeAsync(int userId, string? presentedToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(presentedToken)) return false;
        var expected = await db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.DevicePairingToken)
            .FirstOrDefaultAsync(ct);
        return !string.IsNullOrEmpty(expected) && expected == presentedToken;
    }
}
