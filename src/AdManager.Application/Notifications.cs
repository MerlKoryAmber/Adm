using AdManager.Domain;

namespace AdManager.Application;

/// <summary>SMTP для исходящих уведомлений.</summary>
public sealed class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 25;
    public bool UseSsl { get; set; }
    public string From { get; set; } = "";
    public string? User { get; set; }
    public string? Password { get; set; } // лаба: хранится в App_Data (gitignored). Прод — DPAPI (см. TODO).
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>Политика напоминаний об истечении пароля.</summary>
public sealed class PasswordExpiryPolicy
{
    public bool Enabled { get; set; }
    public List<int> DaysBefore { get; set; } = new() { 7, 3, 1 };
    public string Subject { get; set; } = "Your password expires in {days} day(s)";
    public string Body { get; set; } = "Dear {name},\n\nYour Active Directory password will expire on {date} ({days} day(s) left).\nPlease change it before then to avoid losing access.\n\nRegards,\nIT";
    public int RunHourMsk { get; set; } = 8; // ежедневный прогон в этот час МСК
}

/// <summary>Рантайм-настройки приложения (файловый стор).</summary>
public sealed class AppSettings
{
    public SmtpSettings Smtp { get; set; } = new();
    public PasswordExpiryPolicy PasswordExpiry { get; set; } = new();
    public DateTime? LastRunUtc { get; set; }
    public string? LastRunResult { get; set; }
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

public interface IEmailSender
{
    Task<OperationResult> SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

/// <summary>Пользователь с приближающимся истечением пароля.</summary>
public sealed record ExpiringUser(string Dn, string DisplayName, string? Mail, DateTime ExpiresUtc, int DaysLeft);

/// <summary>Вычисление истекающих паролей (msDS-UserPasswordExpiryTimeComputed).</summary>
public interface IPasswordExpiryService
{
    Task<IReadOnlyList<ExpiringUser>> ListExpiringAsync(int withinDays, CancellationToken ct = default);
}
