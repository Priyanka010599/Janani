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
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public class PushNotificationService(HttpClient httpClient, AppDbContext db, ILogger<PushNotificationService> logger)
{
    private static readonly string[] Scopes = ["https://www.googleapis.com/auth/firebase.messaging"];
    private readonly string _projectId = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
        ?? Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "janani-505411";

    public async Task NotifyUserAsync(int userId, string title, string body, CancellationToken ct = default)
    {
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
