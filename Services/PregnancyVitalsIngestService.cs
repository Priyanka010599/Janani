// Services/PregnancyVitalsIngestService.cs
// Pregnancy-side counterpart to VitalsIngestService (elder) — extracted from
// Vitals.razor's ProcessVitalsReadingAsync/NotifyAlertAsync the same way, so
// the Blazor UI and the device-ingest endpoint share one pipeline. Deliberately
// its own type rather than a generic version of VitalsIngestService: pregnancy
// has no CareAccess-style shared-caregiver fan-out (one user, one push) and
// its own Alert/threshold-checker/explain-agent types.

using Janani.Data;
using Janani.Models;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public record PregnancyVitalsIngestResult(bool Duplicate, bool AlertRaised, AlertSeverity? Severity, PregnancyAlert? Alert);

public class PregnancyVitalsIngestService(
    AppDbContext db,
    BloomAgentRegistry agents,
    PushNotificationService pushNotifications,
    IEmailNotificationService emailNotifications,
    IServiceScopeFactory scopeFactory,
    ILogger<PregnancyVitalsIngestService> logger)
{
    // English/Hindi/Telugu, matching Vitals.razor's own Strings["PushTitle"] —
    // duplicated rather than shared since a Razor page's Strings dict isn't
    // reachable from here, and this is the only string this service needs.
    private static readonly Dictionary<AppLanguage, string> PushTitleByLanguage = new()
    {
        [AppLanguage.English] = "Your vitals need attention",
        [AppLanguage.Hindi] = "आपके वाइटल्स पर ध्यान देने की ज़रूरत है",
        [AppLanguage.Telugu] = "మీ వైటల్స్‌పై దృష్టి అవసరం",
    };

    public async Task<PregnancyVitalsIngestResult> IngestAsync(
        int userId, string userName, int pregnancyWeek, PregnancyVitalsReading reading,
        AppLanguage language, string? doctorName, string? doctorEmail, CancellationToken ct = default)
    {
        // Idempotency: a retried/duplicate upload of the same device reading
        // (same DeviceReadingId) must never be saved or re-alerted on.
        if (!string.IsNullOrEmpty(reading.DeviceReadingId) &&
            await db.PregnancyVitalsReadings.AsNoTracking().AnyAsync(v => v.DeviceReadingId == reading.DeviceReadingId, ct))
        {
            return new PregnancyVitalsIngestResult(Duplicate: true, AlertRaised: false, Severity: null, Alert: null);
        }

        db.PregnancyVitalsReadings.Add(reading);
        await db.SaveChangesAsync(ct);

        // Deterministic check first (no network call for the normal case);
        // the agent is only invoked once a concern already exists, to
        // explain it — see PregnancyVitalsThresholdChecker for why.
        var concerns = PregnancyVitalsThresholdChecker.Check(reading);
        if (concerns.Count == 0)
        {
            return new PregnancyVitalsIngestResult(Duplicate: false, AlertRaised: false, Severity: null, Alert: null);
        }

        PregnancyAlert activeAlert;
        try
        {
            var severity = concerns.Any(c => c.Severity == AlertSeverity.Critical)
                ? AlertSeverity.Critical
                : AlertSeverity.Warning;
            var (explanation, suggestedAction) = await agents.PregnancyVitalsMonitor.ExplainAsync(userName, pregnancyWeek, concerns, severity, language, ct);

            var alert = new PregnancyAlert
            {
                UserId = userId,
                PregnancyVitalsReadingId = reading.Id,
                Severity = severity,
                Message = $"{explanation} {suggestedAction}"
            };
            db.PregnancyAlerts.Add(alert);
            await db.SaveChangesAsync(ct);
            activeAlert = alert;
        }
        catch (Exception)
        {
            // The vitals reading itself is already saved — an explanation
            // failure shouldn't hide that a concern was flagged at all, or
            // that it happened in the DB (same fix as the elder pipeline).
            activeAlert = new PregnancyAlert
            {
                UserId = userId,
                PregnancyVitalsReadingId = reading.Id,
                Severity = concerns.Any(c => c.Severity == AlertSeverity.Critical) ? AlertSeverity.Critical : AlertSeverity.Warning,
                Message = string.Join("; ", concerns.Select(c => c.Description))
            };
            db.PregnancyAlerts.Add(activeAlert);
            await db.SaveChangesAsync(ct);
        }

        await NotifyAlertAsync(userId, userName, doctorName, doctorEmail, activeAlert, language, ct);
        return new PregnancyVitalsIngestResult(Duplicate: false, AlertRaised: true, Severity: activeAlert.Severity, Alert: activeAlert);
    }

    private async Task NotifyAlertAsync(int userId, string userName, string? doctorName, string? doctorEmail, PregnancyAlert alert, AppLanguage language, CancellationToken ct)
    {
        var prefix = alert.Severity == AlertSeverity.Critical ? "🚨" : "⚠️";
        var pushTitle = PushTitleByLanguage.GetValueOrDefault(language, PushTitleByLanguage[AppLanguage.English]);
        await pushNotifications.NotifyUserViaPubSubAsync(userId, $"{prefix} {pushTitle}", alert.Message, "vitals-checker", NotificationScope.MaternalSafety, ct: ct);

        // Only Critical escalates to the doctor by email — Warning stays
        // in-app/push only, so the doctor isn't paged for routine flags.
        if (alert.Severity == AlertSeverity.Critical && !string.IsNullOrWhiteSpace(doctorEmail))
        {
            var subject = $"Urgent: {userName} may need attention (via Janani)";
            var body = $"{alert.Message}\n\nThis is an automated alert from Janani, a pregnancy and family " +
                       $"caregiving app, sent on behalf of {userName} because a critical concern was flagged.";
            var doctorLabel = string.IsNullOrWhiteSpace(doctorName) ? "Doctor" : doctorName;
            // Detached from the request: SendDoctorAlertAsync's retry backoff
            // (up to ~6s) would otherwise sit inline in the ingest response
            // path (this is awaited from POST /api/pregnancy/vitals). Uses
            // CancellationToken.None and a fresh DI scope (not the request's
            // AppDbContext, which is disposed once the response completes)
            // since this outlives the request that triggered it.
            SendDoctorAlertInBackground(userId, doctorEmail!, doctorLabel, subject, body);

            // Critical only: email her too, alongside the push — same
            // redundant-channel principle as the doctor's own email/push-
            // fallback pair. No caregiver fan-out here (pregnancy is one
            // user, one push), so this is just her own account email.
            SendSelfAlertEmailInBackground(userId, subject, body);
        }
    }

    private void SendSelfAlertEmailInBackground(int userId, string subject, string body)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scopedEmail = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
                var user = await scopedDb.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.Email, u.Username })
                    .FirstOrDefaultAsync();
                if (user != null && !string.IsNullOrWhiteSpace(user.Email))
                    await scopedEmail.SendDoctorAlertAsync(user.Email, user.Username, subject, body, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Self alert email failed for user {UserId}", userId);
            }
        });
    }

    private void SendDoctorAlertInBackground(int userId, string doctorEmail, string doctorLabel, string subject, string body)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var doctorEmailSent = await emailNotifications.SendDoctorAlertAsync(doctorEmail, doctorLabel, subject, body, CancellationToken.None);
                if (!doctorEmailSent)
                {
                    using var scope = scopeFactory.CreateScope();
                    var scopedPush = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
                    await scopedPush.NotifyUserViaPubSubAsync(userId, $"⚠️ Couldn't email {doctorLabel}",
                        $"The automatic alert email to {doctorLabel} failed to send — please contact them directly.",
                        "vitals-checker", NotificationScope.MaternalSafety);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background doctor-alert email/fallback failed for user {UserId}", userId);
            }
        });
    }
}
