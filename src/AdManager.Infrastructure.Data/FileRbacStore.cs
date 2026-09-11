using System.Text.Json;
using System.Text.Json.Serialization;
using AdManager.Application;

namespace AdManager.Infrastructure.Data;

/// <summary>JSON-хранилище конфигурации делегирования (лаба). EF-вариант — позже.</summary>
public sealed class FileRbacStore : IRbacStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileRbacStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public async Task<RbacConfig> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return new RbacConfig();
            var json = await File.ReadAllTextAsync(_path, ct);
            return JsonSerializer.Deserialize<RbacConfig>(json, Json) ?? new RbacConfig();
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(RbacConfig config, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(config, Json);
            await File.WriteAllTextAsync(_path, json, ct);
        }
        finally { _lock.Release(); }
    }
}
