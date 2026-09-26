using System.Text.Json;
using AdManager.Application;

namespace AdManager.Infrastructure.Data;

/// <summary>JSON-хранилище каталога custom-атрибутов (лаба/dev).</summary>
public sealed class FileCustomAttributeStore : ICustomAttributeStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileCustomAttributeStore(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    private async Task<List<CustomAttribute>> ReadAllAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new();
        var json = await File.ReadAllTextAsync(_path, ct);
        return JsonSerializer.Deserialize<List<CustomAttribute>>(json, Json) ?? new();
    }

    private Task WriteAllAsync(List<CustomAttribute> list, CancellationToken ct)
        => File.WriteAllTextAsync(_path, JsonSerializer.Serialize(list, Json), ct);

    public async Task<IReadOnlyList<CustomAttribute>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return (await ReadAllAsync(ct)).OrderBy(a => a.Label).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(CustomAttribute attribute, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await ReadAllAsync(ct);
            list.RemoveAll(a => a.Id == attribute.Id
                || string.Equals(a.LdapName, attribute.LdapName, StringComparison.OrdinalIgnoreCase));
            list.Add(attribute);
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
            list.RemoveAll(a => a.Id == id);
            await WriteAllAsync(list, ct);
        }
        finally { _lock.Release(); }
    }
}
