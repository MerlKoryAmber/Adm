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

/// <summary>Отдаёт identity для операций: процесс (gMSA) или хранимая УЗ.</summary>
public sealed class ConfiguredCredentialProvider : IOperationalCredentialProvider
{
    private readonly OperationalCredentialOptions _o;
    public ConfiguredCredentialProvider(OperationalCredentialOptions o) => _o = o;

    public OperationalIdentity GetIdentity(string domain)
    {
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
}

/// <summary>Время МСК (§20).</summary>
public static class MskTime
{
    public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3));
}
