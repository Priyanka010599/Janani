// Services/VitalsIngestService.cs
// Extracted from ElderCare.razor's ProcessVitalsReadingAsync/NotifyElderAlertAsync
// so both the Blazor UI (manual entry, simulated device sync) and the
// POST /api/elders/{id}/vitals endpoint (real/simulated edge devices) share
// one ingest pipeline instead of two copies of the same logic.

using Janani.Data;
using Janani.Models;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

// Alert is included (not just Severity) so a caller like ElderCare.razor can
// show the alert's message immediately -- the fallback alert built when the
// explain-agent call fails is deliberately never persisted (see below), so
// there'd be nothing to re-fetch from the DB otherwise. The API endpoint only
// serializes Duplicate/AlertRaised/Severity onto the wire.
public record VitalsIngestResult(bool Duplicate, bool AlertRaised, AlertSeverity? Severity, Alert? Alert);

public class VitalsIngestService(
    AppDbContext db,
    BloomAgentRegistry agents,
    PushNotificationService pushNotifications,
    IEmailNotificationService emailNotifications,
    CareAccessService careAccess,
    IServiceScopeFactory scopeFactory,
    ILogger<VitalsIngestService> logger)
{
    public async Task<VitalsIngestResult> IngestAsync(
        ElderProfile elder, VitalsReading reading, AppLanguage language, CancellationToken ct = default)
    {
        // Idempotency: a retried/duplicate upload of the same device reading
        // (same DeviceReadingId) must never be saved or re-alerted on.
        if (!string.IsNullOrEmpty(reading.DeviceReadingId) &&
            await db.VitalsReadings.AsNoTracking().AnyAsync(v => v.DeviceReadingId == reading.DeviceReadingId, ct))
        {
            return new VitalsIngestResult(Duplicate: true, AlertRaised: false, Severity: null, Alert: null);
        }

        db.VitalsReadings.Add(reading);
        await db.SaveChangesAsync(ct);

        // Deterministic check first (no network call for the normal case);
        // the agent is only invoked once a concern already exists, to
        // explain it — see VitalsThresholdChecker for why.
        var concerns = VitalsThresholdChecker.Check(reading);
        if (concerns.Count == 0)
        {
            return new VitalsIngestResult(Duplicate: false, AlertRaised: false, Severity: null, Alert: null);
        }

        Alert activeAlert;
        try
        {
            var severity = concerns.Any(c => c.Severity == AlertSeverity.Critical)
                ? AlertSeverity.Critical
                : AlertSeverity.Warning;
            var (explanation, suggestedAction) = await agents.HealthMonitor.ExplainAsync(elder, concerns, severity, language, ct);

            var alert = new Alert
            {
                ElderProfileId = elder.Id,
                VitalsReadingId = reading.Id,
                Severity = severity,
                Message = $"{explanation} {suggestedAction}"
            };
            db.Alerts.Add(alert);
            await db.SaveChangesAsync(ct);
            activeAlert = alert;
        }
        catch (Exception)
        {
            // The vitals reading itself is already saved — an explanation
            // failure shouldn't hide that a concern was flagged at all, and
            // shouldn't hide that the concern exists from the DB either
            // (previously it wasn't persisted here — fixed on request).
            activeAlert = new Alert
            {
                ElderProfileId = elder.Id,
                VitalsReadingId = reading.Id,
                Severity = concerns.Any(c => c.Severity == AlertSeverity.Critical) ? AlertSeverity.Critical : AlertSeverity.Warning,
                Message = string.Join("; ", concerns.Select(c => c.Description))
            };
            db.Alerts.Add(activeAlert);
            await db.SaveChangesAsync(ct);
        }

        await NotifyElderAlertAsync(elder, activeAlert, ct);
        return new VitalsIngestResult(Duplicate: false, AlertRaised: true, Severity: activeAlert.Severity, Alert: activeAlert);
    }

    private async Task NotifyElderAlertAsync(ElderProfile elder, Alert alert, CancellationToken ct)
    {
        var prefix = alert.Severity == AlertSeverity.Critical ? "🚨" : "⚠️";

        // Every caregiver with access to this elder gets pushed — not just
        // whoever happened to be logging the reading — since sharing exists
        // precisely so a critical alert reaches the whole family, not one person.
        var caregiverIds = await careAccess.GetCaregiverUserIdsForElderAsync(elder.Id, ct);
        foreach (var caregiverId in caregiverIds)
        {
            await pushNotifications.NotifyUserViaPubSubAsync(caregiverId, $"{prefix} {elder.Name} needs attention", alert.Message, "elder-checker", NotificationScope.Elder, ct: ct);
        }

        // Only Critical escalates to the doctor by email — Warning stays
        // in-app/push only, so the doctor isn't paged for routine flags.
        if (alert.Severity == AlertSeverity.Critical && !string.IsNullOrWhiteSpace(elder.DoctorEmail))
        {
            var subject = $"Urgent: {elder.Name} may need attention (via Janani)";
            var body = $"{alert.Message}\n\nThis is an automated alert from Janani, a family caregiving app, " +
                       $"sent on behalf of {elder.Name}'s caregiver because a critical concern was flagged.";
            // Detached from the request: SendDoctorAlertAsync's retry backoff
            // (up to ~6s) would otherwise sit inline in the ingest response
            // path (this is awaited from POST /api/elders/{id}/vitals). Uses
            // CancellationToken.None and a fresh DI scope (not the request's
            // AppDbContext, which is disposed once the response completes)
            // since this outlives the request that triggered it.
            SendDoctorAlertInBackground(elder.Id, elder.Name, elder.DoctorEmail!, elder.DoctorName, subject, body, caregiverIds);

            // Critical only: email every caregiver too, alongside their push
            // — same redundant-channel principle as the doctor's own email/
            // push-fallback pair, so a missed or delayed push notification
            // isn't the only way a caregiver finds out. Reuses
            // SendDoctorAlertAsync directly rather than adding a new
            // interface method: its content is already generic (no
            // doctor-specific wording), and this keeps the change to one
            // file instead of both IEmailNotificationService implementations.
            SendCaregiverAlertEmailsInBackground(caregiverIds, subject, body);
        }
    }

    private void SendCaregiverAlertEmailsInBackground(IReadOnlyList<int> caregiverIds, string subject, string body)
    {
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var scopedEmail = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
            var caregivers = await scopedDb.Users.AsNoTracking()
                .Where(u => caregiverIds.Contains(u.Id))
                .Select(u => new { u.Email, u.Username })
                .ToListAsync();
            foreach (var caregiver in caregivers)
            {
                if (string.IsNullOrWhiteSpace(caregiver.Email)) continue;
                try
                {
                    await scopedEmail.SendDoctorAlertAsync(caregiver.Email, caregiver.Username, subject, body, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Caregiver alert email failed (to {Email})", caregiver.Email);
                }
            }
        });
    }

    private void SendDoctorAlertInBackground(int elderId, string elderName, string doctorEmail, string? doctorName, string subject, string body, IReadOnlyList<int> caregiverIds)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var doctorLabel = doctorName ?? "the doctor";
                var doctorEmailSent = await emailNotifications.SendDoctorAlertAsync(doctorEmail, doctorLabel, subject, body, CancellationToken.None);
                if (!doctorEmailSent)
                {
                    using var scope = scopeFactory.CreateScope();
                    var scopedPush = scope.ServiceProvider.GetRequiredService<PushNotificationService>();
                    foreach (var caregiverId in caregiverIds)
                    {
                        await scopedPush.NotifyUserViaPubSubAsync(caregiverId, $"⚠️ Couldn't email {doctorLabel}",
                            $"The automatic alert email to {doctorLabel} about {elderName} failed to send — please contact them directly.",
                            "elder-checker", NotificationScope.Elder);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background doctor-alert email/fallback failed for elder {ElderId}", elderId);
            }
        });
    }
}
