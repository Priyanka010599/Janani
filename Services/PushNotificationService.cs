// Services/PushNotificationService.cs
// Sends real push notifications (arrive even when Janani isn't open) via
// Firebase Cloud Messaging's HTTP v1 API, using the same Application
// Default Credentials the rest of the app already runs under on Cloud Run
// — no separate API key or service-account file, just the
// firebasemessaging.admin IAM role granted to janani-app's service account.
// Complements janani-reminders.js, which only fires while a tab is open.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Google.Apis.Auth.OAuth2;
using Janani.Data;
using Janani.Models;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

// What care domain a notification is about. Elder alerts are a completely
// separate person/relationship from the mother's own bereavement status,
// so they're never gated by it. MaternalSafety covers the mother's own
// deterministic safety monitoring — pregnancy vitals today, the
// postpartum-checker's red-flag detection once that exists — and is never
// suppressed either, bereaved or not: her own physical/mental safety
// monitoring has to keep working through grief. Infant is gated per-child
// (see NotificationGate) rather than per-account, because a
// PartialLossMultiple mother's surviving twin still needs growth/vaccine
// alerts. General covers everything else maternal/infant-adjacent that
// assumes a straightforward ongoing pregnancy or living baby.
public enum NotificationScope
{
    MaternalSafety,
    Infant,
    Elder,
    General
}

// Pure decision logic — no DB/HTTP — so it's unit-testable on its own,
// same split as PregnancyVitalsThresholdChecker (checker decides, the
// service around it does I/O). PushNotificationService is the only caller.
public static class NotificationGate
{
    public static bool ShouldSend(NotificationScope scope, bool isBereaved, bool infantProfileExists)
    {
        if (scope is NotificationScope.Elder or NotificationScope.MaternalSafety) return true;
        if (!isBereaved) return true;
        return scope == NotificationScope.Infant && infantProfileExists;
    }
}

// The event body published to the "janani-alert-events" Pub/Sub topic and
// read back out of it by the /api/events/pubsub webhook in Program.cs —
// JananiAgentEvent.Payload carries exactly this shape for alert-notification
// events, deserialized from the JsonElement System.Text.Json produces.
public record AlertNotificationPayload(
    int UserId, string Title, string Body, NotificationScope Scope, int? InfantProfileId);

public class PushNotificationService(
    HttpClient httpClient, AppDbContext db, GooglePubSubEventService pubSubEvents, ILogger<PushNotificationService> logger)
{
    private static readonly string[] Scopes = ["https://www.googleapis.com/auth/firebase.messaging"];
    private readonly string _projectId = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
        ?? Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "janani-505411";

    // Publishes the alert to Pub/Sub so delivery happens off the alert-detection
    // path (the push subscription posts back to /api/events/pubsub, which calls
    // NotifyUserAsync below). Falls back to sending directly, synchronously, if
    // the publish itself fails -- these are safety alerts (Critical vitals,
    // postpartum red flags), so a Pub/Sub outage must never mean the alert is
    // silently dropped.
    public async Task NotifyUserViaPubSubAsync(
        int userId, string title, string body, string sourceAgent,
        NotificationScope scope = NotificationScope.General,
        int? infantProfileId = null,
        CancellationToken ct = default)
    {
        var published = await pubSubEvents.PublishEventAsync("janani-alert-events", new JananiAgentEvent
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = "alert-notification",
            SourceAgent = sourceAgent,
            UserName = userId.ToString(),
            Payload = new AlertNotificationPayload(userId, title, body, scope, infantProfileId)
        }, ct);

        if (!published)
        {
            await NotifyUserAsync(userId, title, body, scope, infantProfileId, ct);
        }
    }

    public async Task NotifyUserAsync(
        int userId, string title, string body,
        NotificationScope scope = NotificationScope.General,
        int? infantProfileId = null,
        CancellationToken ct = default)
    {
        var isBereaved = await db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.BirthOutcome)
            .FirstOrDefaultAsync(ct) is { } outcome && outcome != BirthOutcome.LiveBirth;

        // Existence alone is the signal, not ownership by the notified
        // userId — a shared caregiver receiving this alert isn't the
        // infant's owning parent, but the row existing at all still means
        // it's a living child (no row is ever created for one who isn't).
        var infantProfileExists = scope == NotificationScope.Infant && infantProfileId is { } id
            && await db.InfantProfiles.AsNoTracking().AnyAsync(i => i.Id == id, ct);

        if (!NotificationGate.ShouldSend(scope, isBereaved, infantProfileExists)) return;

        var tokens = await db.PushSubscriptions.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Token)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            await SendAsync(token, title, body, ct);
        }
    }

    private async Task SendAsync(string token, string title, string body, CancellationToken ct)
    {
        try
        {
            var credential = await GoogleCredential.GetApplicationDefaultAsync(ct);
            if (credential.IsCreateScopedRequired) credential = credential.CreateScoped(Scopes);
            var accessToken = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
                Content = JsonContent.Create(new { message = new { token, notification = new { title, body } } })
            };
            using var response = await httpClient.SendAsync(request, ct);

            // An unregistered/invalid token means the app was uninstalled or
            // the token rotated without us hearing about it — stop retrying it.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                var stale = await db.PushSubscriptions.FirstOrDefaultAsync(p => p.Token == token, ct);
                if (stale != null)
                {
                    db.PushSubscriptions.Remove(stale);
                    await db.SaveChangesAsync(ct);
                }
            }
            else if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("FCM send failed: {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FCM send threw for a subscription");
        }
    }
}
