using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace AdManager.Infrastructure.Data;

/// <summary>Базовое JSON-документное хранилище состояния в БД (ADR-0006). Читает/пишет одну строку AppState
/// по ключу. Использует IDbContextFactory — безопасно из любого контекста (в т.ч. singleton BackgroundService).</summary>
public abstract class EfAppStateStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly IDbContextFactory<AdManagerDbContext> _factory;
    private readonly string _key;
    private readonly JsonSerializerOptions? _opts;

    protected EfAppStateStore(IDbContextFactory<AdManagerDbContext> factory, string key, JsonSerializerOptions? opts = null)
    {
        _factory = factory;
        _key = key;
        _opts = opts ?? Json;
    }

    protected async Task<T> LoadCoreAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AppState.AsNoTracking().FirstOrDefaultAsync(x => x.Key == _key, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Json)) return new T();
        return JsonSerializer.Deserialize<T>(row.Json, _opts) ?? new T();
    }

    protected async Task SaveCoreAsync(T value, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AppState.FirstOrDefaultAsync(x => x.Key == _key, ct);
        var json = JsonSerializer.Serialize(value, _opts);
        if (row is null)
        {
            db.AppState.Add(new AppStateEntry { Key = _key, Json = json, UpdatedUtc = DateTime.UtcNow });
        }
        else
        {
            row.Json = json;
            row.UpdatedUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
