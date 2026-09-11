using AdManager.Application.Abstractions;
using AdManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace AdManager.Infrastructure.Data;

/// <summary>Аудит в БД (EF Core). Append-only на уровне приложения.</summary>
public sealed class EfAuditLog : IAuditLog
{
    private readonly AdManagerDbContext _db;

    public EfAuditLog(AdManagerDbContext db) => _db = db;

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        _db.Audit.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        IQueryable<AuditEntry> q = _db.Audit.AsNoTracking();
        if (query.FromMsk is { } from) q = q.Where(x => x.TimestampMsk >= from);
        if (query.ToMsk is { } to) q = q.Where(x => x.TimestampMsk <= to);
        if (!string.IsNullOrEmpty(query.ActorSid)) q = q.Where(x => x.ActorSid == query.ActorSid);
        if (!string.IsNullOrEmpty(query.TargetDn)) q = q.Where(x => x.TargetDn == query.TargetDn);
        return await q.OrderByDescending(x => x.TimestampMsk).Take(query.Take).ToListAsync(ct);
    }
}
