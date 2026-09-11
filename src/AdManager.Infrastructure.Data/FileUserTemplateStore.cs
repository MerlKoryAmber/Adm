using System.Text.Json;
using AdManager.Application;

namespace AdManager.Infrastructure.Data;

/// <summary>JSON-хранилище шаблонов формы пользователя (лаба). EF-вариант — позже.</summary>
public sealed class FileUserTemplateStore : IUserTemplateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileUserTemplateStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    private async Task<List<UserTemplate>> ReadAllAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new();
        var json = await File.ReadAllTextAsync(_path, ct);
        return JsonSerializer.Deserialize<List<UserTemplate>>(json, Json) ?? new();
    }

    private Task WriteAllAsync(List<UserTemplate> list, CancellationToken ct)
        => File.WriteAllTextAsync(_path, JsonSerializer.Serialize(list, Json), ct);

    public async Task<List<UserTemplate>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return (await ReadAllAsync(ct)).OrderBy(t => t.Name).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task<UserTemplate?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return (await ReadAllAsync(ct)).FirstOrDefault(t => t.Id == id); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(UserTemplate template, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await ReadAllAsync(ct);
            var idx = list.FindIndex(t => t.Id == template.Id);
            template.ModifiedUtc = DateTime.UtcNow;
            if (idx >= 0) list[idx] = template;
            else { template.CreatedUtc = DateTime.UtcNow; list.Add(template); }
            await WriteAllAsync(list, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await ReadAllAsync(ct);
            list.RemoveAll(t => t.Id == id);
            await WriteAllAsync(list, ct);
        }
        finally { _lock.Release(); }
    }
}
