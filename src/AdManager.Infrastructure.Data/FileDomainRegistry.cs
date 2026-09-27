using System.Text.Json;
using AdManager.Application;

namespace AdManager.Infrastructure.Data;

/// <summary>JSON-хранилище реестра доменов (лаба/dev). Пароль — как есть (dev);
/// в Ef-режиме секрет шифруется EncryptedDomainRegistry.</summary>
public sealed class FileDomainRegistry : IDomainRegistry
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileDomainRegistry(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    private async Task<List<ManagedDomain>> ReadAllAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new();
        var json = await File.ReadAllTextAsync(_path, ct);
        return JsonSerializer.Deserialize<List<ManagedDomain>>(json, Json) ?? new();
    }

    private Task WriteAllAsync(List<ManagedDomain> list, CancellationToken ct)
        => File.WriteAllTextAsync(_path, JsonSerializer.Serialize(list, Json), ct);

    public async Task<IReadOnlyList<ManagedDomain>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await ReadAllAsync(ct); }
        finally { _lock.Release(); }
    }

    public async Task<ManagedDomain?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return (await ReadAllAsync(ct)).FirstOrDefault(d => d.Id == id); }
        finally { _lock.Release(); }
    }

    public async Task<ManagedDomain?> GetActiveAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var items = await ReadAllAsync(ct);
            return items.FirstOrDefault(d => d.IsDefault && d.Enabled)
                   ?? items.FirstOrDefault(d => d.Enabled)
                   ?? items.FirstOrDefault();
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(ManagedDomain domain, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await ReadAllAsync(ct);
            domain.ModifiedUtc = DateTime.UtcNow;
            list.RemoveAll(d => d.Id == domain.Id);
            if (list.Count == 0) domain.IsDefault = true;
            if (domain.IsDefault) foreach (var d in list) d.IsDefault = false;
            list.Add(domain);
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
            var wasDefault = list.FirstOrDefault(d => d.Id == id)?.IsDefault ?? false;
            list.RemoveAll(d => d.Id == id);
            if (wasDefault && list.Count > 0 && !list.Any(d => d.IsDefault)) list[0].IsDefault = true;
            await WriteAllAsync(list, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await ReadAllAsync(ct);
            foreach (var d in list) d.IsDefault = d.Id == id;
            await WriteAllAsync(list, ct);
        }
        finally { _lock.Release(); }
    }
}
