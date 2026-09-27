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

/// <summary>Хранимая доменная УЗ для operational identity (альтернатива gMSA).
/// Пароль — секрет, шифруется envelope (ADR-0006). Пусто/Mode=gMSA — работаем без пароля.</summary>
public sealed class StoredCredentialSettings
{
    /// <summary>gMSA (по умолчанию, без пароля) | StoredCredential (доменная УЗ с паролем).</summary>
    public string Mode { get; set; } = "gMSA";
    public string? User { get; set; }
    public string? Password { get; set; } // секрет: шифруется envelope
    public string Domain { get; set; } = "";

    public bool IsStored => string.Equals(Mode, "StoredCredential", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(User);
}

/// <summary>Кэш данных Exchange: список почтовых баз и mailbox-атрибуты.
/// Опрос Exchange дорогой (remote PowerShell), поэтому кэшируется по расписанию.</summary>
public sealed class ExchangeCacheSettings
{
    /// <summary>Обновлять список почтовых баз каждые N часов (по умолчанию 24).</summary>
    public int DatabaseListRefreshHours { get; set; } = 24;
    /// <summary>Обновлять кэш mailbox-атрибутов (AD+Exchange) каждые N часов (по умолчанию 6).</summary>
    public int MailboxAttributesRefreshHours { get; set; } = 6;
    public DateTime? DatabasesLastRefreshUtc { get; set; }
    public DateTime? MailboxAttributesLastRefreshUtc { get; set; }
}

/// <summary>Рантайм-настройки приложения (файловый стор).</summary>
public sealed class AppSettings
{
    public SmtpSettings Smtp { get; set; } = new();
    public HttpsSettings Https { get; set; } = new();
    public PasswordExpiryPolicy PasswordExpiry { get; set; } = new();
    /// <summary>Operational identity: gMSA или хранимая УЗ (ADR-0006, из .env перенесено в БД).</summary>
    public StoredCredentialSettings OperationalCredential { get; set; } = new();
    /// <summary>Срок хранения логов аудита (дней). 0 = не чистить. По умолчанию 365.</summary>
    public int AuditRetentionDays { get; set; } = 365;
    /// <summary>Расписание кэширования данных Exchange.</summary>
    public ExchangeCacheSettings ExchangeCache { get; set; } = new();
    public DateTime? LastRunUtc { get; set; }
    public string? LastRunResult { get; set; }
}

/// <summary>Защита секретов: envelope encryption (AES-256-GCM, ключ вне БД — ADR-0006).
/// Реализация в Infrastructure. Protect отдаёт самоописывающий токен (префикс версии),
/// Unprotect понимает и токен, и legacy-plain (обратная совместимость при миграции).</summary>
public interface ISecretProtector
{
    /// <summary>Зашифровать секрет. null/пусто возвращается как есть.</summary>
    string? Protect(string? plaintext);
    /// <summary>Расшифровать. Значение без префикса-токена считается legacy-plain и возвращается как есть.</summary>
    string? Unprotect(string? stored);
    /// <summary>true, если строка — уже зашифрованный токен этого протектора.</summary>
    bool IsProtected(string? value);
}

/// <summary>Управление ключом шифрования секретов (keyring, ADR-0006): бэкап и ротация.</summary>
public interface IKeyring
{
    /// <summary>Сгенерировать новый ключ, вернув старый (для пере-шифровки). Ключ на диске обновляется.</summary>
    byte[] Rotate();
    /// <summary>Экспорт текущего ключа для резервной копии (сырые байты; хранить раздельно от БД).</summary>
    byte[] ExportKey();
    /// <summary>Отпечаток текущего ключа (для отображения/сверки бэкапа, не сам ключ).</summary>
    string KeyFingerprint();
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

/// <summary>Кэш списка почтовых баз Exchange (обновляется по расписанию + принудительно из Settings).</summary>
public interface IExchangeDataCache
{
    /// <summary>Кэшированный список баз (пустой, пока не прогрет).</summary>
    IReadOnlyList<Abstractions.MailboxDatabase> Databases { get; }
    DateTime? DatabasesRefreshedUtc { get; }
    /// <summary>Принудительно перечитать список баз из Exchange.</summary>
    Task RefreshDatabasesAsync(CancellationToken ct = default);
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
