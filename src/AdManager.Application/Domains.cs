using AdManager.Application.Abstractions;

namespace AdManager.Application;

/// <summary>
/// Управляемый домен AD (реестр для мульти-доменного режима). У каждого домена —
/// свой DC, BaseDn и operational identity (gMSA или хранимая УЗ). Пароль — секрет
/// (шифруется envelope при хранении, ADR-0006). Фаза 1: активен домен IsDefault.
/// </summary>
public sealed class ManagedDomain
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Отображаемое имя (напр. "Merl.loc" или "HQ").</summary>
    public string Name { get; set; } = "";
    /// <summary>FQDN или IP контроллера домена.</summary>
    public string DcServer { get; set; } = "";
    /// <summary>FQDN домена (напр. merl.loc).</summary>
    public string DomainFqdn { get; set; } = "";
    /// <summary>База поиска (DN). Пусто — вывести из DomainFqdn (DC=…).</summary>
    public string? BaseDn { get; set; }

    /// <summary>Operational identity: "gMSA" (без пароля) | "StoredCredential" (УЗ с паролем).</summary>
    public string CredentialMode { get; set; } = "gMSA";
    public string? CredentialUser { get; set; }
    public string? CredentialPassword { get; set; } // секрет: шифруется envelope
    /// <summary>Домен операционной УЗ (если отличается).</summary>
    public string? CredentialDomain { get; set; }

    /// <summary>Exchange remote PowerShell URI для этого домена (опц.).</summary>
    public string? ExchangeConnectionUri { get; set; }

    public bool Enabled { get; set; } = true;
    /// <summary>Активный по умолчанию (Фаза 1: используется он).</summary>
    public bool IsDefault { get; set; }

    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>BaseDn или выведенный из FQDN (a.b.c → DC=a,DC=b,DC=c).</summary>
    public string EffectiveBaseDn =>
        !string.IsNullOrWhiteSpace(BaseDn) ? BaseDn!
        : string.Join(",", (DomainFqdn ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries).Select(p => "DC=" + p));

    public bool IsStoredCredential =>
        string.Equals(CredentialMode, "StoredCredential", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(CredentialUser);

    public AdConnectionOptions ToConnectionOptions() =>
        new() { Server = DcServer, Domain = DomainFqdn, BaseDn = EffectiveBaseDn };
}

/// <summary>Реестр управляемых доменов (хранится в БД / файле).</summary>
public interface IDomainRegistry
{
    Task<IReadOnlyList<ManagedDomain>> ListAsync(CancellationToken ct = default);
    Task<ManagedDomain?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Активный домен (IsDefault; если нет — первый включённый; если пусто — null).</summary>
    Task<ManagedDomain?> GetActiveAsync(CancellationToken ct = default);
    Task SaveAsync(ManagedDomain domain, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Сделать домен активным (снимает IsDefault с прочих).</summary>
    Task SetDefaultAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Провайдер кредов для операций конкретного домена (реестр → УЗ этого домена).</summary>
public sealed class DomainCredentialProvider : IOperationalCredentialProvider
{
    private readonly ManagedDomain _domain;
    public DomainCredentialProvider(ManagedDomain domain) => _domain = domain;

    public OperationalIdentity GetIdentity(string domain)
    {
        if (_domain.IsStoredCredential)
            return new OperationalIdentity("StoredCredential",
                new System.Net.NetworkCredential(_domain.CredentialUser, _domain.CredentialPassword,
                    _domain.CredentialDomain ?? _domain.DomainFqdn));
        return new OperationalIdentity("gMSA", null);
    }
}
