using System.Net;
using AdManager.Application.Abstractions;
using AdManager.Domain.Enums;

namespace AdManager.Application;

/// <summary>Параметры подключения к AD.</summary>
public sealed class AdConnectionOptions
{
    public string Server { get; set; } = "";   // FQDN или IP DC
    public string Domain { get; set; } = "";
    public string? BaseDn { get; set; }
}

/// <summary>Режим operational identity и (для StoredCredential) креды.</summary>
public sealed class OperationalCredentialOptions
{
    public string Mode { get; set; } = "gMSA"; // gMSA | StoredCredential
    public string? User { get; set; }
    public string? Password { get; set; }
    public string Domain { get; set; } = "";
}

/// <summary>Параметры подключения к Exchange (remote PowerShell).</summary>
public sealed class ExchangeOptions
{
    public string ServerFqdn { get; set; } = "";
    /// <summary>Напр. http://exch.merl.loc/PowerShell/</summary>
    public string ConnectionUri { get; set; } = "";
    /// <summary>Kerberos | Negotiate | Basic</summary>
    public string Authentication { get; set; } = "Kerberos";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionUri);
}

/// <summary>Отдаёт identity для операций: процесс (gMSA) или хранимая УЗ.
/// Источник хранимой УЗ (ADR-0006): сначала БД (settings-стор, пароль расшифрован
/// декоратором), при отсутствии — конфиг/.env как bootstrap-fallback.</summary>
public sealed class ConfiguredCredentialProvider : IOperationalCredentialProvider
{
    private readonly OperationalCredentialOptions _o;
    private readonly ISettingsStore? _settings;

    public ConfiguredCredentialProvider(OperationalCredentialOptions o, ISettingsStore? settings = null)
    {
        _o = o;
        _settings = settings;
    }

    public OperationalIdentity GetIdentity(string domain)
    {
        // 1) БД (рантайм-настройки). Singleton вне request-контекста — sync-ожидание безопасно.
        if (_settings != null)
        {
            var sc = _settings.LoadAsync().GetAwaiter().GetResult().OperationalCredential;
            if (sc.IsStored)
                return new OperationalIdentity("StoredCredential", new NetworkCredential(sc.User, sc.Password, sc.Domain));
        }

        // 2) fallback: конфиг/.env (bootstrap до первой настройки в UI)
        if (string.Equals(_o.Mode, "StoredCredential", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(_o.User))
        {
            return new OperationalIdentity("StoredCredential", new NetworkCredential(_o.User, _o.Password));
        }

        return new OperationalIdentity("gMSA", null);
    }
}

/// <summary>Временная заглушка RBAC для Фазы 0 — разрешает всё. Заменить в Фазе 2.</summary>
public sealed class AllowAllRbacEngine : IRbacEngine
{
    public Task<AuthorizationDecision> AuthorizeAsync(TechnicianContext actor, Permission operation, string targetDn, CancellationToken ct = default)
        => Task.FromResult(new AuthorizationDecision(true, "AllowAll (Фаза 0)"));

    public Task<FieldPermission> AllowedFieldsAsync(TechnicianContext actor, Permission operation, string targetDn, CancellationToken ct = default)
        => Task.FromResult(FieldPermission.All);

    public Task<Guid?> EnforcedTemplateAsync(TechnicianContext actor, string kind, string targetDn, CancellationToken ct = default)
        => Task.FromResult<Guid?>(null);

    public Task<EffectiveAccess> EffectiveAccessAsync(TechnicianContext actor, CancellationToken ct = default)
        => Task.FromResult(new EffectiveAccess(true, new HashSet<Permission>()));
}

/// <summary>Время МСК (§20).</summary>
public static class MskTime
{
    public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3));

    /// <summary>Перевести UTC-момент в МСК (UTC+3).</summary>
    public static DateTimeOffset ToMsk(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(TimeSpan.FromHours(3));
}
