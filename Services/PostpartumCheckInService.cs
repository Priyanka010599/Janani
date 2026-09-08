// Services/PostpartumCheckInService.cs
// Orchestrates the daily postpartum check-in and EPDS-10 flows: save the
// reading, run the appropriate pure checker, explain via the agent only if
// a concern exists (falling back to the raw checker text if the agent call
// fails), create a PostpartumAlert, and notify. Mirrors the flow embedded
// directly in Vitals.razor for pregnancy vitals — pulled out into a
// service here since no check-in page exists yet to host it; a future
// page calls this rather than reimplementing the flow.

using Janani.Data;
using Janani.Models;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public class PostpartumCheckInService(
    AppDbContext db,
    PostpartumRecoveryMonitorService monitor,
    PushNotificationService pushNotifications,
    IEmailNotificationService emailNotifications)
{
    // Deterministic — not left to the LLM to remember to mention. Appended
    // directly to alert.Message below whenever EPDS item 10 (self-harm) is
    // nonzero, so it reaches the mother via the always-sent push
    // notification regardless of whether the agent call succeeds or fails,
    // and regardless of whether a DoctorEmail is on file at all. Number
    // verified live (WebSearch, official telemanas.mohfw.gov.in + a PIB
    // press note) — see the bereavement knowledge base for the same number.
    private const string TeleManasLine =
        "If you're having thoughts of harming yourself, please reach out right now to your provider, a trusted person nearby, " +
        "or the Tele-MANAS helpline (14416 or 1-800-891-4416, available 24/7 in English and 20 regional languages).";

    public async Task<PostpartumCheckIn> SubmitCheckInAsync(
        PostpartumCheckIn checkIn, string userName, string? doctorName, string? doctorEmail,
        AppLanguage language = AppLanguage.English, CancellationToken ct = default)
    {
        db.PostpartumCheckIns.Add(checkIn);
        await db.SaveChangesAsync(ct);

        var concerns = PostpartumRecoveryChecker.Check(checkIn);
        if (concerns.Count > 0)
        {
            await RaiseAlertAsync(
                checkIn.UserId,
                concerns.Select(c => (c.Description, c.Severity)).ToList(),
                checkIn.Id, null, userName, doctorName, doctorEmail, language,
                includesSelfHarmConcern: false, ct);
        }

        return checkIn;
    }

    public async Task<EpdsScreening> SubmitEpdsScreeningAsync(
        EpdsScreening screening, string userName, string? doctorName, string? doctorEmail,
        AppLanguage language = AppLanguage.English, CancellationToken ct = default)
    {
        db.EpdsScreenings.Add(screening);
        await db.SaveChangesAsync(ct);

        var concerns = EpdsScreeningChecker.Check(screening);
        if (concerns.Count > 0)
        {
            await RaiseAlertAsync(
                screening.UserId,
                concerns.Select(c => (c.Description, c.Severity)).ToList(),
                null, screening.Id, userName, doctorName, doctorEmail, language,
                includesSelfHarmConcern: screening.Item10 > 0, ct);
        }

        return screening;
    }

    private async Task RaiseAlertAsync(
        int userId, List<(string Description, AlertSeverity Severity)> concerns,
        int? checkInId, int? epdsScreeningId,
        string userName, string? doctorName, string? doctorEmail, AppLanguage language,
        bool includesSelfHarmConcern, CancellationToken ct)
    {
        var severity = concerns.Any(c => c.Severity == AlertSeverity.Critical) ? AlertSeverity.Critical : AlertSeverity.Warning;

        string message;
        try
        {
            var (explanation, suggestedAction) = await monitor.ExplainAsync(
                userName, concerns.Select(c => c.Description), severity, language, ct);
            message = $"{explanation} {suggestedAction}";
        }
        catch (Exception)
        {
            // The concern itself is already real and already being
            // recorded — an explanation failure shouldn't hide that a
            // concern was flagged at all.
            message = string.Join("; ", concerns.Select(c => c.Description));
        }

        // Deterministic append, after either branch above — guarantees the
        // crisis line reaches the persisted/pushed message regardless of
        // what the LLM did or didn't say, and regardless of DoctorEmail.
        if (includesSelfHarmConcern)
        {
            var separator = message.TrimEnd().EndsWith('.') || message.TrimEnd().EndsWith(';') ? "" : ".";
            message = $"{message.TrimEnd()}{separator} {TeleManasLine}";
        }

        var alert = new PostpartumAlert
        {
            UserId = userId,
            PostpartumCheckInId = checkInId,
            EpdsScreeningId = epdsScreeningId,
            Severity = severity,
            Message = message
        };
        db.PostpartumAlerts.Add(alert);
        await db.SaveChangesAsync(ct);

        await NotifyAlertAsync(alert, userName, doctorName, doctorEmail, ct);
    }

    private async Task NotifyAlertAsync(PostpartumAlert alert, string userName, string? doctorName, string? doctorEmail, CancellationToken ct)
    {
        var prefix = alert.Severity == AlertSeverity.Critical ? "🚨" : "⚠️";

        // MaternalSafety — never suppressed by bereavement status; her own
        // physical/mental safety monitoring has to keep working through
        // grief. See NotificationGate in PushNotificationService.cs.
        await pushNotifications.NotifyUserViaPubSubAsync(
            alert.UserId, $"{prefix} Your postpartum check-in needs attention", alert.Message,
            "postpartum-checker", NotificationScope.MaternalSafety, ct: ct);

        // Only Critical escalates to the doctor by email — Warning stays
        // in-app/push only, matching Vitals.razor's own rule.
        if (alert.Severity == AlertSeverity.Critical && !string.IsNullOrWhiteSpace(doctorEmail))
        {
            var subject = $"Urgent: {userName} may need attention (via Janani)";
            var body = $"{alert.Message}\n\nThis is an automated alert from Janani, a pregnancy and family " +
                       $"caregiving app, sent on behalf of {userName} because a critical postpartum concern was flagged.";
            var doctorLabel = string.IsNullOrWhiteSpace(doctorName) ? "Doctor" : doctorName;
            var doctorEmailSent = await emailNotifications.SendDoctorAlertAsync(doctorEmail!, doctorLabel, subject, body, ct);
            if (!doctorEmailSent)
            {
                await pushNotifications.NotifyUserViaPubSubAsync(alert.UserId, $"⚠️ Couldn't email {doctorLabel}",
                    $"The automatic alert email to {doctorLabel} failed to send — please contact them directly.",
                    "postpartum-checker", NotificationScope.MaternalSafety, ct: ct);
            }
        }
    }
}
