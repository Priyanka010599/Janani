// Services/ResendEmailNotificationService.cs
// Second IEmailNotificationService implementation, alongside the Gmail-SMTP
// one. Same contract, different transport: an HTTPS call to Resend carrying a
// send-only API key, instead of an SMTP login with a Gmail App Password.
//
// Why not authenticate as the Cloud Run service account, the way Storage,
// Pub/Sub, FCM and Vertex all do? Because Gmail is the one Google service that
// won't accept a service account as an identity: a service account can only
// send mail by impersonating a real mailbox through domain-wide delegation,
// and that needs Google Workspace with a verified domain — a consumer
// @gmail.com address can never be impersonated. GCP also has no first-party
// transactional email service, and blocks outbound port 25 outright, so a
// third-party sender is the standard path here.
//
// This doesn't make the credential disappear — nothing can, short of Workspace
// plus domain-wide delegation. What it changes is the blast radius. A Gmail App
// Password grants FULL access to that mailbox (read, delete, send-as) and can't
// be narrowed. A Resend key is send-only, revocable on its own, has no
// consumer-Gmail daily cap, and delivers better from Cloud Run. The key lives
// in Secret Manager and is mounted via --set-secrets, so the service account is
// what unlocks it — the same pattern DATABASE_URL already uses.

using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Janani.Services;

public class ResendEmailNotificationService : IEmailNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ResendEmailNotificationService> _logger;
    private readonly string? _apiKey;
    private readonly string _from;

    public ResendEmailNotificationService(HttpClient httpClient, ILogger<ResendEmailNotificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        // Trim for the same reason the SMTP service does: a Secret Manager
        // value can pick up a trailing newline depending on how it was written,
        // and that surfaces as an opaque 401 rather than anything diagnostic.
        _apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY")?.Trim();
        // onboarding@resend.dev is Resend's shared test sender: it works with no
        // DNS setup, but will ONLY deliver to the address that owns the Resend
        // account. Sending to an arbitrary doctor's mailbox needs a verified
        // domain, with RESEND_FROM set to an address on it.
        _from = Environment.GetEnvironmentVariable("RESEND_FROM")?.Trim() is { Length: > 0 } from
            ? from
            : "Janani <onboarding@resend.dev>";
    }

    public async Task<bool> SendDoctorAlertAsync(
        string doctorEmail, string doctorName, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Doctor alert email skipped — RESEND_API_KEY not configured.");
            return false;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
                {
                    Headers = { Authorization = new AuthenticationHeaderValue("Bearer", _apiKey) },
                    Content = JsonContent.Create(new
                    {
                        from = _from,
                        to = new[] { doctorEmail },
                        subject,
                        text = body
                    })
                };

                using var response = await _httpClient.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) return true;

                // Resend answers a rejection with a JSON body explaining why
                // (unverified domain, bad key, invalid recipient). Logging the
                // status alone would make those indistinguishable.
                var detail = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Doctor alert email rejected by Resend ({Status}) on attempt {Attempt}/{MaxAttempts}: {Detail}",
                    (int)response.StatusCode, attempt, maxAttempts, detail);

                // A 4xx is a configuration problem — the same request will be
                // rejected identically three times, so fail fast rather than
                // sitting through the backoff for nothing. Only retry 5xx.
                if ((int)response.StatusCode < 500) return false;
                if (attempt == maxAttempts) return false;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "Doctor alert email attempt {Attempt}/{MaxAttempts} failed (to {DoctorEmail}), retrying",
                    attempt, maxAttempts, doctorEmail);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Doctor alert email failed after {Attempts} attempt(s) (to {DoctorEmail})", attempt, doctorEmail);
                return false;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct); }
            catch (OperationCanceledException) { return false; }
        }

        return false;
    }
}
