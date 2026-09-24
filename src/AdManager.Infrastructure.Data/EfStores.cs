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
