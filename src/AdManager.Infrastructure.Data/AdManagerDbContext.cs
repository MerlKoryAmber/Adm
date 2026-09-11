using AdManager.Domain;
using Microsoft.EntityFrameworkCore;

namespace AdManager.Infrastructure.Data;

/// <summary>
/// EF Core (SQL Server / LocalDB). Пока маппится только аудит; RBAC-сущности
/// (роли/scope/назначения) добавятся в Фазе 2. Провайдер выбирается в композиции (ADR-0002).
/// </summary>
public sealed class AdManagerDbContext : DbContext
{
    public AdManagerDbContext(DbContextOptions<AdManagerDbContext> options) : base(options)
    {
    }

    public DbSet<AuditEntry> Audit => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AuditEntry>();
        e.ToTable("AuditEntries");
        e.HasKey(x => x.Id);
        e.Property(x => x.Operation).HasConversion<string>().HasMaxLength(64);
        e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.ActorSid).HasMaxLength(200);
        e.Property(x => x.ActorName).HasMaxLength(256);
        e.Property(x => x.TargetDn).HasMaxLength(1024);
        e.HasIndex(x => x.TimestampMsk);
        e.HasIndex(x => x.TargetDn);
        base.OnModelCreating(modelBuilder);
    }
}
