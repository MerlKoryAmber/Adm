using AdManager.Application;

namespace AdManager.Infrastructure.Data;

/// <summary>
/// Декоратор ISettingsStore: шифрует секретные поля AppSettings перед записью в
/// нижележащий стор (Ef/File) и расшифровывает после чтения (ADR-0006, envelope).
/// Секреты: Smtp.Password, Https.PfxPassword, OperationalCredential.Password.
/// Одна точка на оба стора — Ef и File не дублируют крипто-логику.
/// </summary>
public sealed class EncryptedSettingsStore : ISettingsStore
{
    private readonly ISettingsStore _inner;
    private readonly ISecretProtector _protector;

    public EncryptedSettingsStore(ISettingsStore inner, ISecretProtector protector)
    {
        _inner = inner;
        _protector = protector;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        var s = await _inner.LoadAsync(ct);
        // расшифровка на месте (значения без токена — legacy-plain, вернутся как есть)
        s.Smtp.Password = _protector.Unprotect(s.Smtp.Password);
        s.Https.PfxPassword = _protector.Unprotect(s.Https.PfxPassword);
        s.OperationalCredential.Password = _protector.Unprotect(s.OperationalCredential.Password);
        return s;
    }

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        // Шифруем на КОПИИ полей, чтобы не мутировать объект, которым владеет вызывающий
        // (он продолжит работать с plaintext в памяти после Save).
        var toStore = Clone(settings);
        toStore.Smtp.Password = _protector.Protect(toStore.Smtp.Password);
        toStore.Https.PfxPassword = _protector.Protect(toStore.Https.PfxPassword);
        toStore.OperationalCredential.Password = _protector.Protect(toStore.OperationalCredential.Password);
        return _inner.SaveAsync(toStore, ct);
    }

    /// <summary>Поверхностная копия с новыми секрето-несущими под-объектами
    /// (достаточно, т.к. шифруем только их строковые поля).</summary>
    private static AppSettings Clone(AppSettings s) => new()
    {
        Smtp = new SmtpSettings
        {
            Host = s.Smtp.Host, Port = s.Smtp.Port, UseSsl = s.Smtp.UseSsl,
            From = s.Smtp.From, User = s.Smtp.User, Password = s.Smtp.Password,
        },
        Https = new HttpsSettings
        {
            RequireHttps = s.Https.RequireHttps, UseHsts = s.Https.UseHsts,
            CertificateSource = s.Https.CertificateSource, PfxPath = s.Https.PfxPath,
            PfxPassword = s.Https.PfxPassword, Thumbprint = s.Https.Thumbprint,
            HttpsPort = s.Https.HttpsPort,
        },
        OperationalCredential = new StoredCredentialSettings
        {
            Mode = s.OperationalCredential.Mode, User = s.OperationalCredential.User,
            Password = s.OperationalCredential.Password, Domain = s.OperationalCredential.Domain,
        },
        PasswordExpiry = s.PasswordExpiry,   // не секрет — ссылка ок
        AuditRetentionDays = s.AuditRetentionDays,
        ExchangeCache = s.ExchangeCache,     // не секрет — ссылка ок
        LastRunUtc = s.LastRunUtc,
        LastRunResult = s.LastRunResult,
    };
}
