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
/// <summary>Один триггер напоминания: за сколько дней до истечения и какой текст слать.</summary>
public sealed class ExpiryTrigger
{
    public int DaysBefore { get; set; }
    public string Subject { get; set; } = "Your password expires in {days} day(s)";
    public string Body { get; set; } = "Dear {name},\n\nYour Active Directory password will expire on {date} ({days} day(s) left).\nPlease change it before then to avoid losing access.\n\nRegards,\nIT";
}

public sealed class PasswordExpiryPolicy
{
    public bool Enabled { get; set; }
    public int RunHourMsk { get; set; } = 8; // ежедневный прогон в этот час МСК

    /// <summary>Триггеры (несколько порогов, у каждого свой текст).</summary>
    public List<ExpiryTrigger> Triggers { get; set; } = new()
    {
        new() { DaysBefore = 7 },
        new() { DaysBefore = 3 },
        new() { DaysBefore = 1, Subject = "Action required: your password expires tomorrow",
                Body = "Dear {name},\n\nYour Active Directory password expires on {date} — only {days} day(s) left.\nPlease change it today to avoid losing access.\n\nRegards,\nIT" },
    };

    public IEnumerable<int> DaysBefore => Triggers.Select(t => t.DaysBefore);
}

/// <summary>Настройки HTTPS/TLS для веб-хоста (справочно + для инструкций привязки сертификата).</summary>
public sealed class HttpsSettings
{
    public bool RequireHttps { get; set; }
    public bool UseHsts { get; set; }
    /// <summary>Способ поставки сертификата: IIS binding (по умолчанию), PFX-файл, или store по thumbprint.</summary>
    public string CertificateSource { get; set; } = "IIS"; // IIS | PfxFile | Store
    public string? PfxPath { get; set; }
    public string? PfxPassword { get; set; } // лаба: App_Data (gitignored). Прод — DPAPI/секрет-хранилище.
    public string? Thumbprint { get; set; }
    public int HttpsPort { get; set; } = 443;
}

/// <summary>Рантайм-настройки приложения (файловый стор).</summary>
public sealed class AppSettings
{
    public SmtpSettings Smtp { get; set; } = new();
    public HttpsSettings Https { get; set; } = new();
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
