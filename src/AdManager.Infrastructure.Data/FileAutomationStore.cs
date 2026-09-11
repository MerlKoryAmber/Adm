using System.Text.Json;
using AdManager.Application;

namespace AdManager.Infrastructure.Data;

/// <summary>JSON-хранилище автоматизаций (лаба). EF-вариант — позже.</summary>
public sealed class FileAutomationStore : IAutomationStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileAutomationStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public async Task<AutomationData> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return new AutomationData();
            var json = await File.ReadAllTextAsync(_path, ct);
            return JsonSerializer.Deserialize<AutomationData>(json, Json) ?? new AutomationData();
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(AutomationData data, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(data, Json), ct);
        }
        finally { _lock.Release(); }
    }
}
