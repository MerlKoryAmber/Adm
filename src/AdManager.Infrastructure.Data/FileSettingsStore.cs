using System.Text.Json;
using AdManager.Application;

namespace AdManager.Infrastructure.Data;

/// <summary>JSON-хранилище рантайм-настроек (лаба). App_Data/app-settings.json.</summary>
public sealed class FileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileSettingsStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var json = await File.ReadAllTextAsync(_path, ct);
            return JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings();
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(settings, Json), ct); }
        finally { _lock.Release(); }
    }
}
