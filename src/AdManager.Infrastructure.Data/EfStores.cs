using System.Text.Json;
using System.Text.Json.Serialization;
using AdManager.Application;
using Microsoft.EntityFrameworkCore;

namespace AdManager.Infrastructure.Data;

/// <summary>RBAC-конфиг в БД (JSON-документ). Enum'ы — строками (как FileRbacStore).</summary>
public sealed class EfRbacStore : EfAppStateStore<RbacConfig>, IRbacStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public EfRbacStore(IDbContextFactory<AdManagerDbContext> factory)
        : base(factory, "rbac", Opts) { }

    public Task<RbacConfig> LoadAsync(CancellationToken ct = default) => LoadCoreAsync(ct);
    public Task SaveAsync(RbacConfig config, CancellationToken ct = default) => SaveCoreAsync(config, ct);
}

/// <summary>Настройки приложения в БД (JSON-документ). Секреты пока в открытом виде —
/// шифрование добавляется отдельным этапом ADR-0006.</summary>
public sealed class EfSettingsStore : EfAppStateStore<AppSettings>, ISettingsStore
{
    public EfSettingsStore(IDbContextFactory<AdManagerDbContext> factory)
        : base(factory, "settings") { }

    public Task<AppSettings> LoadAsync(CancellationToken ct = default) => LoadCoreAsync(ct);
    public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => SaveCoreAsync(settings, ct);
}

/// <summary>Автоматизации в БД (JSON-документ).</summary>
public sealed class EfAutomationStore : EfAppStateStore<AutomationData>, IAutomationStore
{
    public EfAutomationStore(IDbContextFactory<AdManagerDbContext> factory)
        : base(factory, "automation") { }

    public Task<AutomationData> LoadAsync(CancellationToken ct = default) => LoadCoreAsync(ct);
    public Task SaveAsync(AutomationData data, CancellationToken ct = default) => SaveCoreAsync(data, ct);
}

/// <summary>Шаблоны формы пользователя в БД (JSON-документ — список шаблонов целиком).</summary>
public sealed class EfUserTemplateStore : EfAppStateStore<TemplateList>, IUserTemplateStore
{
    public EfUserTemplateStore(IDbContextFactory<AdManagerDbContext> factory)
        : base(factory, "templates") { }

    public async Task<List<UserTemplate>> ListAsync(CancellationToken ct = default)
        => (await LoadCoreAsync(ct)).Items;

    public async Task<UserTemplate?> GetAsync(Guid id, CancellationToken ct = default)
        => (await LoadCoreAsync(ct)).Items.FirstOrDefault(t => t.Id == id);

    public async Task SaveAsync(UserTemplate template, CancellationToken ct = default)
    {
        var list = await LoadCoreAsync(ct);
        list.Items.RemoveAll(t => t.Id == template.Id);
        template.ModifiedUtc = DateTime.UtcNow;
        list.Items.Add(template);
        await SaveCoreAsync(list, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var list = await LoadCoreAsync(ct);
        list.Items.RemoveAll(t => t.Id == id);
        await SaveCoreAsync(list, ct);
    }
}

/// <summary>Обёртка-документ для списка шаблонов (для JSON-хранения).</summary>
public sealed class TemplateList
{
    public List<UserTemplate> Items { get; set; } = new();
}

/// <summary>Обёртка-документ для каталога custom-атрибутов.</summary>
public sealed class CustomAttrList
{
    public List<CustomAttribute> Items { get; set; } = new();
}

/// <summary>Обёртка-документ для реестра доменов.</summary>
public sealed class DomainList
{
    public List<ManagedDomain> Items { get; set; } = new();
}

/// <summary>Реестр управляемых доменов в БД (JSON-документ). Пароль УЗ шифруется
/// декоратором EncryptedDomainRegistry (ADR-0006), сам стор хранит как есть.</summary>
public sealed class EfDomainRegistry : EfAppStateStore<DomainList>, IDomainRegistry
{
    public EfDomainRegistry(IDbContextFactory<AdManagerDbContext> factory)
        : base(factory, "domains") { }

    public async Task<IReadOnlyList<ManagedDomain>> ListAsync(CancellationToken ct = default)
        => (await LoadCoreAsync(ct)).Items;

    public async Task<ManagedDomain?> GetAsync(Guid id, CancellationToken ct = default)
        => (await LoadCoreAsync(ct)).Items.FirstOrDefault(d => d.Id == id);

    public async Task<ManagedDomain?> GetActiveAsync(CancellationToken ct = default)
    {
        var items = (await LoadCoreAsync(ct)).Items;
        return items.FirstOrDefault(d => d.IsDefault && d.Enabled)
               ?? items.FirstOrDefault(d => d.Enabled)
               ?? items.FirstOrDefault();
    }

    public async Task SaveAsync(ManagedDomain domain, CancellationToken ct = default)
    {
        var list = await LoadCoreAsync(ct);
        domain.ModifiedUtc = DateTime.UtcNow;
        list.Items.RemoveAll(d => d.Id == domain.Id);
        // Первый добавленный домен автоматически становится дефолтным.
        if (list.Items.Count == 0) domain.IsDefault = true;
        if (domain.IsDefault) foreach (var d in list.Items) d.IsDefault = false;
        list.Items.Add(domain);
        await SaveCoreAsync(list, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var list = await LoadCoreAsync(ct);
        var wasDefault = list.Items.FirstOrDefault(d => d.Id == id)?.IsDefault ?? false;
        list.Items.RemoveAll(d => d.Id == id);
        // Если удалили дефолтный — назначить дефолтным первый оставшийся.
        if (wasDefault && list.Items.Count > 0 && !list.Items.Any(d => d.IsDefault))
            list.Items[0].IsDefault = true;
        await SaveCoreAsync(list, ct);
    }

    public async Task SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        var list = await LoadCoreAsync(ct);
        foreach (var d in list.Items) d.IsDefault = d.Id == id;
        await SaveCoreAsync(list, ct);
    }
}

/// <summary>Декоратор IDomainRegistry: шифрует пароль УЗ домена перед записью и
/// расшифровывает после чтения (envelope, ADR-0006) — как EncryptedSettingsStore.</summary>
public sealed class EncryptedDomainRegistry : IDomainRegistry
{
    private readonly IDomainRegistry _inner;
    private readonly ISecretProtector _protector;

    public EncryptedDomainRegistry(IDomainRegistry inner, ISecretProtector protector)
    {
        _inner = inner;
        _protector = protector;
    }

    private ManagedDomain Decrypt(ManagedDomain d)
    {
        d.CredentialPassword = _protector.Unprotect(d.CredentialPassword);
        return d;
    }

    public async Task<IReadOnlyList<ManagedDomain>> ListAsync(CancellationToken ct = default)
        => (await _inner.ListAsync(ct)).Select(Decrypt).ToList();

    public async Task<ManagedDomain?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var d = await _inner.GetAsync(id, ct);
        return d is null ? null : Decrypt(d);
    }

    public async Task<ManagedDomain?> GetActiveAsync(CancellationToken ct = default)
    {
        var d = await _inner.GetActiveAsync(ct);
        return d is null ? null : Decrypt(d);
    }

    public Task SaveAsync(ManagedDomain domain, CancellationToken ct = default)
    {
        // шифруем на копии, чтобы не мутировать объект вызывающего (у него остаётся plaintext)
        var copy = Clone(domain);
        copy.CredentialPassword = _protector.Protect(copy.CredentialPassword);
        return _inner.SaveAsync(copy, ct);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => _inner.DeleteAsync(id, ct);
    public Task SetDefaultAsync(Guid id, CancellationToken ct = default) => _inner.SetDefaultAsync(id, ct);

    private static ManagedDomain Clone(ManagedDomain d) => new()
    {
        Id = d.Id, Name = d.Name, DcServer = d.DcServer, DomainFqdn = d.DomainFqdn, BaseDn = d.BaseDn,
        CredentialMode = d.CredentialMode, CredentialUser = d.CredentialUser,
        CredentialPassword = d.CredentialPassword, CredentialDomain = d.CredentialDomain,
        ExchangeConnectionUri = d.ExchangeConnectionUri, Enabled = d.Enabled, IsDefault = d.IsDefault,
        ModifiedUtc = d.ModifiedUtc,
    };
}

/// <summary>Каталог custom-атрибутов в БД (JSON-документ).</summary>
public sealed class EfCustomAttributeStore : EfAppStateStore<CustomAttrList>, ICustomAttributeStore
{
    public EfCustomAttributeStore(IDbContextFactory<AdManagerDbContext> factory)
        : base(factory, "customattrs") { }

    public async Task<IReadOnlyList<CustomAttribute>> ListAsync(CancellationToken ct = default)
        => (await LoadCoreAsync(ct)).Items;

    public async Task SaveAsync(CustomAttribute attribute, CancellationToken ct = default)
    {
        var list = await LoadCoreAsync(ct);
        list.Items.RemoveAll(a => a.Id == attribute.Id
            || string.Equals(a.LdapName, attribute.LdapName, StringComparison.OrdinalIgnoreCase));
        list.Items.Add(attribute);
        await SaveCoreAsync(list, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var list = await LoadCoreAsync(ct);
        list.Items.RemoveAll(a => a.Id == id);
        await SaveCoreAsync(list, ct);
    }
}
