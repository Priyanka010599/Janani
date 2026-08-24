// Services/CalendarSyncService.cs
// Per-user Google Calendar sync for prenatal appointments. Self-service only —
// tokens are scoped to the individual user, never surfaced to any employer.
// Config via GOOGLE_CALENDAR_CLIENT_ID/GOOGLE_CALENDAR_CLIENT_SECRET env vars,
// same pattern as AI_PROVIDER in Program.cs. A sync failure never blocks the
// user from saving her appointment — everything here logs and degrades rather
// than throws out to the caller, matching GoogleCloudStorageService's style.

using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Janani.Data;
using Janani.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public interface ICalendarSyncService
{
    string BuildAuthorizationUrl(string state, string redirectUri);
    Task CompleteConnectionAsync(int userId, string code, string redirectUri, CancellationToken ct = default);
    Task DisconnectAsync(int userId, CancellationToken ct = default);
    Task SyncAppointmentAsync(int userId, AppointmentEntry appointment, CancellationToken ct = default);
    Task<bool> CheckHealthAsync(CancellationToken ct = default);
}

public class GoogleCalendarSyncService : ICalendarSyncService
{
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<GoogleCalendarSyncService> _logger;
    private readonly string? _clientId;
    private readonly string? _clientSecret;

    public GoogleCalendarSyncService(AppDbContext db, IDataProtectionProvider dpProvider, ILogger<GoogleCalendarSyncService> logger)
    {
        _db = db;
        _logger = logger;
        _protector = dpProvider.CreateProtector("Janani.CalendarSync.Tokens.v1");
        // Trim: secrets set via Secret Manager can pick up a trailing
        // newline depending on how the value was written (e.g. piping a
        // string to `gcloud secrets create` from PowerShell) - Google's
        // OAuth server does an exact string match on client_id, so an
        // invisible trailing \r\n here manifests as "invalid_client".
        _clientId = Environment.GetEnvironmentVariable("GOOGLE_CALENDAR_CLIENT_ID")?.Trim();
        _clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CALENDAR_CLIENT_SECRET")?.Trim();
    }

    public string BuildAuthorizationUrl(string state, string redirectUri)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
            throw new InvalidOperationException("Google Calendar sync is not configured.");

        var flow = BuildFlow();
        var request = (GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(redirectUri);
        request.State = state;
        request.AccessType = "offline"; // required to get a refresh_token back
        return request.Build().ToString();
    }

    public async Task CompleteConnectionAsync(int userId, string code, string redirectUri, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
            throw new InvalidOperationException("Google Calendar sync is not configured.");

        var flow = BuildFlow();
        var token = await flow.ExchangeCodeForTokenAsync(userId.ToString(), code, redirectUri, ct);

        var account = await _db.ConnectedCalendarAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Provider == "Google", ct);
        if (account == null)
        {
            account = new ConnectedCalendarAccount { UserId = userId, Provider = "Google" };
            _db.ConnectedCalendarAccounts.Add(account);
        }

        account.EncryptedAccessToken = _protector.Protect(token.AccessToken);
        // Google only returns a refresh_token on the first consent grant for a
        // given user/client pair even with prompt=consent in some edge cases —
        // don't clobber a previously-stored one with nothing.
        if (!string.IsNullOrEmpty(token.RefreshToken))
            account.EncryptedRefreshToken = _protector.Protect(token.RefreshToken);
        account.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds ?? 3600);
        account.ConnectedAt = DateTime.UtcNow;
        account.SyncEnabled = true;
        account.LastSyncFailed = false;
        account.LastSyncError = null;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DisconnectAsync(int userId, CancellationToken ct = default)
    {
        var account = await _db.ConnectedCalendarAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Provider == "Google", ct);
        if (account == null) return;

        try
        {
            var refreshToken = _protector.Unprotect(account.EncryptedRefreshToken);
            using var http = new HttpClient();
            await http.PostAsync($"https://oauth2.googleapis.com/revoke?token={Uri.EscapeDataString(refreshToken)}", content: null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Calendar token revoke failed for user {UserId} (continuing with local disconnect)", userId);
        }

        // Her synced events stay on her calendar — disconnecting stops future
        // sync, it doesn't retroactively erase history.
        _db.ConnectedCalendarAccounts.Remove(account);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SyncAppointmentAsync(int userId, AppointmentEntry appointment, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret)) return;

        // Everything, including the account lookup itself, lives inside the
        // try below — this method must never throw out to the caller. It
        // used to look up the account before the try block, so a transient
        // DB hiccup here would crash the appointment save that called this.
        ConnectedCalendarAccount? account = null;
        try
        {
            account = await _db.ConnectedCalendarAccounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Provider == "Google", ct);
            if (account == null || !account.SyncEnabled) return;

            var flow = BuildFlow();
            var remainingSeconds = Math.Max(0, (account.ExpiresAt - DateTime.UtcNow).TotalSeconds);
            var tokenResponse = new TokenResponse
            {
                AccessToken = _protector.Unprotect(account.EncryptedAccessToken),
                RefreshToken = _protector.Unprotect(account.EncryptedRefreshToken),
                IssuedUtc = DateTime.UtcNow,
                ExpiresInSeconds = (long)remainingSeconds
            };
            var credential = new UserCredential(flow, userId.ToString(), tokenResponse);
            await credential.GetAccessTokenForRequestAsync(cancellationToken: ct); // auto-refreshes if stale

            // Persist whatever token state we ended up with — cheap, and keeps
            // the stored tokens correct after a refresh without extra branching.
            account.EncryptedAccessToken = _protector.Protect(credential.Token.AccessToken);
            if (!string.IsNullOrEmpty(credential.Token.RefreshToken))
                account.EncryptedRefreshToken = _protector.Protect(credential.Token.RefreshToken);
            account.ExpiresAt = DateTime.UtcNow.AddSeconds(credential.Token.ExpiresInSeconds ?? 3600);

            using var service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Janani"
            });

            var description = string.Join("\n", new[]
            {
                !string.IsNullOrEmpty(appointment.DoctorName) ? $"Doctor: {appointment.DoctorName}" : null,
                !string.IsNullOrEmpty(appointment.QuestionsToAsk) ? $"Questions to ask: {appointment.QuestionsToAsk}" : null,
                !string.IsNullOrEmpty(appointment.NotesAfterVisit) ? $"Notes: {appointment.NotesAfterVisit}" : null
            }.Where(s => s != null));

            var start = new DateTimeOffset(appointment.AppointmentDate);
            var calendarEvent = new Event
            {
                Summary = appointment.Title,
                Location = appointment.Location,
                Description = description,
                Start = new EventDateTime { DateTimeDateTimeOffset = start },
                End = new EventDateTime { DateTimeDateTimeOffset = start.AddMinutes(30) } // no duration field on AppointmentEntry yet
            };

            if (string.IsNullOrEmpty(appointment.GoogleEventId))
            {
                var inserted = await service.Events.Insert(calendarEvent, account.GoogleCalendarId).ExecuteAsync(ct);
                appointment.GoogleEventId = inserted.Id;
            }
            else
            {
                try
                {
                    await service.Events.Update(calendarEvent, account.GoogleCalendarId, appointment.GoogleEventId).ExecuteAsync(ct);
                }
                catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
                {
                    // Event was deleted on the Google side (or belongs to a
                    // previously-connected account) — self-heal by recreating it.
                    var inserted = await service.Events.Insert(calendarEvent, account.GoogleCalendarId).ExecuteAsync(ct);
                    appointment.GoogleEventId = inserted.Id;
                }
            }

            account.LastSyncFailed = false;
            account.LastSyncError = null;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Calendar sync failed for user {UserId}", userId);
            if (account != null)
            {
                try
                {
                    account.LastSyncFailed = true;
                    account.LastSyncError = ex.Message;
                    await _db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    // Even recording the failure failed - log and move on,
                    // never let this escape to the caller.
                    _logger.LogWarning(saveEx, "Failed to record Calendar sync failure for user {UserId}", userId);
                }
            }
        }
    }

    public Task<bool> CheckHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret));

    private GoogleAuthorizationCodeFlow BuildFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
        Scopes = [CalendarService.Scope.CalendarEvents],
        DataStore = new NullDataStore(),
        Prompt = "consent" // required so reconnects reliably return a refresh_token too
    });

    // We own token persistence ourselves (encrypted in ConnectedCalendarAccounts)
    // rather than letting the flow write to disk via the library's own stores.
    private sealed class NullDataStore : IDataStore
    {
        public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;
        public Task DeleteAsync<T>(string key) => Task.CompletedTask;
        public Task<T> GetAsync<T>(string key) => Task.FromResult(default(T)!);
        public Task ClearAsync() => Task.CompletedTask;
    }
}
