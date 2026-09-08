// Services/EmailNotificationService.cs
// Sends automatic emails to a designated doctor when a Critical alert fires
// (see VitalsIngestService/PregnancyVitalsIngestService/PostpartumCheckInService/
// InfantCare.razor), via one centrally-configured Gmail account acting as
// Janani itself over SMTP -- not per-caregiver OAuth. Zero setup per
// caregiver: as long as SMTP_SENDER_EMAIL/SMTP_APP_PASSWORD are configured
// once for the whole app, every Critical alert's doctor email just goes out.
// Trade-off versus the old per-user-Gmail-OAuth design: the doctor sees a
// shared "Janani" sender address, not the caregiver's own name -- accepted
// deliberately in exchange for removing all per-caregiver setup.

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Janani.Services;

public interface IEmailNotificationService
{
    Task<bool> SendDoctorAlertAsync(string doctorEmail, string doctorName, string subject, string body, CancellationToken ct = default);
}

public class SmtpEmailNotificationService : IEmailNotificationService
{
    private readonly ILogger<SmtpEmailNotificationService> _logger;
    private readonly string? _senderEmail;
    private readonly string? _appPassword;

    public SmtpEmailNotificationService(ILogger<SmtpEmailNotificationService> logger)
    {
        _logger = logger;
        _senderEmail = Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL")?.Trim();
        _appPassword = Environment.GetEnvironmentVariable("SMTP_APP_PASSWORD")?.Trim();
    }

    public async Task<bool> SendDoctorAlertAsync(string doctorEmail, string doctorName, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_senderEmail) || string.IsNullOrEmpty(_appPassword))
        {
            _logger.LogWarning("Doctor alert email skipped — SMTP_SENDER_EMAIL/SMTP_APP_PASSWORD not configured.");
            return false;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Janani", _senderEmail));
                message.To.Add(new MailboxAddress(doctorName, doctorEmail));
                message.Subject = subject;
                message.Body = new TextPart("plain") { Text = body };

                // A fresh client per send: MailKit's SmtpClient isn't meant to be
                // held open/reused across concurrent calls, and alerts are rare
                // enough that reconnecting each time costs nothing meaningful.
                using var client = new SmtpClient();
                await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, ct);
                await client.AuthenticateAsync(_senderEmail, _appPassword, ct);
                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);
                return true;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Doctor alert email attempt {Attempt}/{MaxAttempts} failed (to {DoctorEmail}), retrying", attempt, maxAttempts, doctorEmail);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct); }
                catch (OperationCanceledException) { return false; }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Doctor alert email failed after {Attempts} attempt(s) (to {DoctorEmail})", attempt, doctorEmail);
                return false;
            }
        }
        return false;
    }
}
