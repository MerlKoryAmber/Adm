using System.Net;
using System.Net.Mail;
using AdManager.Application;
using AdManager.Domain;

namespace AdManager.Infrastructure.Data;

/// <summary>Отправка почты по SMTP (System.Net.Mail). Настройки берутся из ISettingsStore на момент отправки.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly ISettingsStore _store;
    public SmtpEmailSender(ISettingsStore store) => _store = store;

    public async Task<OperationResult> SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var smtp = (await _store.LoadAsync(ct)).Smtp;
        if (!smtp.IsConfigured) return OperationResult.Fail("SMTP is not configured (set host and from address in Settings).");
        if (string.IsNullOrWhiteSpace(to)) return OperationResult.Fail("Recipient has no email address.");

        try
        {
            using var msg = new MailMessage(smtp.From, to, subject, body);
            using var client = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = smtp.UseSsl };
            if (!string.IsNullOrEmpty(smtp.User))
                client.Credentials = new NetworkCredential(smtp.User, smtp.Password);
            await client.SendMailAsync(msg, ct);
            return OperationResult.Ok($"Sent to {to}");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }
}
