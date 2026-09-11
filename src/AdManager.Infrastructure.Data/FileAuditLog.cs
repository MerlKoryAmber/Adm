using System.Text.Json;
using System.Text.Json.Serialization;
using AdManager.Application.Abstractions;
using AdManager.Domain;

namespace AdManager.Infrastructure.Data;

/// <summary>
/// Временный файловый аудит (JSONL) для Фазы 0, пока нет БД.
/// Заменяется на EF/SQL-реализацию. Append-only; неизменяемость на уровне
/// файла НЕ гарантируется (нет hash-chain) — это делается в Data-слое. См. docs/handoff/TODO.md.
/// </summary>
public sealed class FileAuditLog : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _lock = new();

    public FileAuditLog(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var line = JsonSerializer.Serialize(entry, Json);
        lock (_lock)
        {
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<AuditEntry>();
        }

        var lines = await File.ReadAllLinesAsync(_path, ct);
        var items = new List<AuditEntry>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            AuditEntry? entry;
            try { entry = JsonSerializer.Deserialize<AuditEntry>(line, Json); }
            catch (JsonException) { continue; } // битую строку пропускаем
            if (entry is null)
            {
                continue;
            }
            if (query.FromMsk is { } from && entry.TimestampMsk < from) continue;
            if (query.ToMsk is { } to && entry.TimestampMsk > to) continue;
            if (!string.IsNullOrEmpty(query.ActorSid) && entry.ActorSid != query.ActorSid) continue;
            if (!string.IsNullOrEmpty(query.TargetDn) && entry.TargetDn != query.TargetDn) continue;
            items.Add(entry);
        }
        return items.Take(query.Take).ToList();
    }
}
