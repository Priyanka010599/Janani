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

using System.Security.Cryptography;
using System.Text;
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

// Comparing a presented secret with == returns as soon as the first byte
// differs, so response time leaks how much of a guess was correct — enough to
// recover a token byte by byte over many requests. FixedTimeEquals always
// walks the whole buffer. Length is compared separately first because the
// primitive requires equal-length spans; the length isn't the secret.
internal static class TokenComparer
{
    public static bool Matches(string expected, string? presented)
    {
        if (string.IsNullOrEmpty(presented)) return false;
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(presented);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}

public class GlobalElderDeviceAuthenticator(
    IHostEnvironment environment,
    ILogger<GlobalElderDeviceAuthenticator> logger) : IElderDeviceAuthenticator
{
    public Task<bool> AuthorizeAsync(int elderId, string? presentedToken, CancellationToken ct)
    {
        var expected = Environment.GetEnvironmentVariable("DEVICE_INGEST_TOKEN")?.Trim();

        if (string.IsNullOrEmpty(expected))
        {
            // Unconfigured used to mean "allow everyone", unconditionally.
            // That's a deliberate convenience for the edge-device demo
            // (edge-device-sim/ POSTs with no --token), but it was also
            // indistinguishable from a production misconfiguration, and an
            // open ingest endpoint lets anyone write to a real elder's
            // clinical record and fire Critical alerts at their caregivers.
            // The open path is now scoped to Development and announces itself
            // in the log rather than failing open silently.
            if (!environment.IsDevelopment())
            {
                logger.LogError(
                    "Rejected elder vitals ingest for {ElderId}: DEVICE_INGEST_TOKEN is not configured.", elderId);
                return Task.FromResult(false);
            }

            logger.LogWarning(
                "DEVICE_INGEST_TOKEN unset — elder vitals ingest is UNAUTHENTICATED (Development only).");
            return Task.FromResult(true);
        }

        return Task.FromResult(TokenComparer.Matches(expected, presentedToken));
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
        return !string.IsNullOrEmpty(expected) && TokenComparer.Matches(expected, presentedToken);
    }
}
