// Services/EmailNotificationService.cs
// Sends automatic emails to a designated doctor when a Critical alert fires
// (see ElderCare.razor/InfantCare.razor), via Gmail acting as the caregiver
// themselves — not a third-party transactional email service, so nothing new
// to provision beyond a Google OAuth consent. Deliberately a separate OAuth
// connection from CalendarSyncService's, even though both are "Google":
// gmail.send is a more sensitive grant than calendar.events and shouldn't be
// bundled into a scope the user didn't ask for when they connected Calendar.
// Same shape as CalendarSyncService otherwise — encrypted token storage,
// never throws out to the caller, degrades to a no-op if not connected.

using System.Net.Mail;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Janani.Data;
using Janani.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public interface IEmailNotificationService
{
    string BuildAuthorizationUrl(string state, string redirectUri);
    Task CompleteConnectionAsync(int userId, string code, string redirectUri, CancellationToken ct = default);
    Task DisconnectAsync(int userId, CancellationToken ct = default);
    Task<bool> IsConnectedAsync(int userId, CancellationToken ct = default);
    Task<bool> SendDoctorAlertAsync(int userId, string doctorEmail, string doctorName, string subject, string body, CancellationToken ct = default);
}

public class GmailNotificationService : IEmailNotificationService
{
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<GmailNotificationService> _logger;
    private readonly string? _clientId;
    private readonly string? _clientSecret;

    public GmailNotificationService(AppDbContext db, IDataProtectionProvider dpProvider, ILogger<GmailNotificationService> logger)
    {
        _db = db;
        _logger = logger;
        _protector = dpProvider.CreateProtector("Janani.EmailNotifications.Tokens.v1");
        // Same OAuth client as Calendar sync — one Google Cloud OAuth client
        // can issue tokens for different scopes on different consent flows.
        _clientId = Environment.GetEnvironmentVariable("GOOGLE_CALENDAR_CLIENT_ID")?.Trim();
        _clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CALENDAR_CLIENT_SECRET")?.Trim();
    }

    public string BuildAuthorizationUrl(string state, string redirectUri)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
            throw new InvalidOperationException("Doctor alert email is not configured.");

        var flow = BuildFlow();
        var request = (GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(redirectUri);
        request.State = state;
        request.AccessType = "offline";
        return request.Build().ToString();
    }

    public async Task CompleteConnectionAsync(int userId, string code, string redirectUri, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
            throw new InvalidOperationException("Doctor alert email is not configured.");

        var flow = BuildFlow();
        var token = await flow.ExchangeCodeForTokenAsync(userId.ToString(), code, redirectUri, ct);

        var account = await _db.ConnectedEmailAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Provider == "Google", ct);
        if (account == null)
        {
            account = new ConnectedEmailAccount { UserId = userId, Provider = "Google" };
            _db.ConnectedEmailAccounts.Add(account);
        }

        account.EncryptedAccessToken = _protector.Protect(token.AccessToken);
        if (!string.IsNullOrEmpty(token.RefreshToken))
            account.EncryptedRefreshToken = _protector.Protect(token.RefreshToken);
        account.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds ?? 3600);
        account.ConnectedAt = DateTime.UtcNow;
        account.LastSendFailed = false;
        account.LastSendError = null;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DisconnectAsync(int userId, CancellationToken ct = default)
    {
        var account = await _db.ConnectedEmailAccounts
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
            _logger.LogWarning(ex, "Gmail token revoke failed for user {UserId} (continuing with local disconnect)", userId);
        }

        _db.ConnectedEmailAccounts.Remove(account);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> IsConnectedAsync(int userId, CancellationToken ct = default) =>
        await _db.ConnectedEmailAccounts.AnyAsync(a => a.UserId == userId && a.Provider == "Google", ct);

    public async Task<bool> SendDoctorAlertAsync(int userId, string doctorEmail, string doctorName, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret)) return false;

        ConnectedEmailAccount? account = null;
        try
        {
            account = await _db.ConnectedEmailAccounts
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Provider == "Google", ct);
            if (account == null) return false;

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
            await credential.GetAccessTokenForRequestAsync(cancellationToken: ct);

            account.EncryptedAccessToken = _protector.Protect(credential.Token.AccessToken);
            if (!string.IsNullOrEmpty(credential.Token.RefreshToken))
                account.EncryptedRefreshToken = _protector.Protect(credential.Token.RefreshToken);
            account.ExpiresAt = DateTime.UtcNow.AddSeconds(credential.Token.ExpiresInSeconds ?? 3600);

            using var service = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Janani"
            });

            var message = new Message { Raw = BuildRawMessage(doctorEmail, doctorName, subject, body) };
            await service.Users.Messages.Send(message, "me").ExecuteAsync(ct);

            account.LastSendFailed = false;
            account.LastSendError = null;
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Doctor alert email failed for user {UserId}", userId);
            if (account != null)
            {
                try
                {
                    account.LastSendFailed = true;
                    account.LastSendError = ex.Message;
                    await _db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "Failed to record doctor alert email failure for user {UserId}", userId);
                }
            }
            return false;
        }
    }

    private static string BuildRawMessage(string toEmail, string toName, string subject, string body)
    {
        // .NET has no built-in MIME writer for the Gmail API's raw message
        // format — build the headers by hand rather than pull in a full SMTP
        // client for a simple plain-text message with no attachments.
        var to = new MailAddress(toEmail, toName).ToString();
        var raw = $"To: {to}\r\nSubject: {EncodeHeader(subject)}\r\nContent-Type: text/plain; charset=UTF-8\r\n\r\n{body}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string EncodeHeader(string value) =>
        $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";

    private GoogleAuthorizationCodeFlow BuildFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
        Scopes = [GmailService.Scope.GmailSend],
        DataStore = new NullEmailDataStore(),
        Prompt = "consent"
    });

    private sealed class NullEmailDataStore : IDataStore
    {
        public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;
        public Task DeleteAsync<T>(string key) => Task.CompletedTask;
        public Task<T> GetAsync<T>(string key) => Task.FromResult(default(T)!);
        public Task ClearAsync() => Task.CompletedTask;
    }
}
