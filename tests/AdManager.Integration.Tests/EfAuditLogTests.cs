using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Domain;
using AdManager.Domain.Enums;
using AdManager.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdManager.Integration.Tests;

/// <summary>Проверка EF-аудита против LocalDB (throwaway-БД). Пропуск, если LocalDB недоступен.</summary>
public class EfAuditLogTests
{
    private const string ConnString =
        "Server=(localdb)\\MSSQLLocalDB;Database=AdManager_Test;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30";

    [SkippableFact]
    public async Task Write_and_query_audit_via_localdb()
    {
        var options = new DbContextOptionsBuilder<AdManagerDbContext>()
            .UseSqlServer(ConnString)
            .Options;

        await using var db = new AdManagerDbContext(options);
        try
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
        catch (Exception ex)
        {
            Skip.If(true, "LocalDB недоступен: " + ex.Message);
            return;
        }

        var audit = new EfAuditLog(db);
        var dn = "CN=Test User1,OU=Users,OU=AdManagerLab,DC=Merl,DC=loc";
        await audit.WriteAsync(new AuditEntry
        {
            Phase = AuditPhase.Result,
            TimestampMsk = MskTime.Now,
            ActorSid = "S-1-5-lab-tech",
            ActorName = "Lab Tech",
            Operation = Permission.ResetPassword,
            TargetDn = dn,
            Success = true,
            Message = "ef-test",
        });

        var rows = await audit.QueryAsync(new AuditQuery(TargetDn: dn, Take: 10));
        Assert.Contains(rows, r => r.Operation == Permission.ResetPassword && r.Success && r.Phase == AuditPhase.Result);

        await db.Database.EnsureDeletedAsync();
    }
}
