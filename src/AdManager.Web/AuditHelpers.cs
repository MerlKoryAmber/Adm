using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Domain;

namespace AdManager.Web;

/// <summary>Запись событий входа в панель (Technician Logon Report, ADR-план).</summary>
public static class LogonAudit
{
    public static Task WriteAsync(IAuditLog audit, string sid, string name, string host, string method, bool success, string message)
        => audit.WriteAsync(new AuditEntry
        {
            Kind = AuditKind.Logon,
            TimestampMsk = MskTime.Now,
            ActorSid = sid,
            ActorName = name,
            Success = success,
            Host = host,
            Method = method,
            Message = message,
            Action = "Logon",
        });
}

/// <summary>Дедупликация записи доменного SSO-входа: один logon на SID в пределах окна,
/// чтобы каждый запрос не плодил записи (у Windows Auth нет явного login-шага).</summary>
public static class SsoLogonTracker
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> Seen = new();

    /// <summary>true, если для этого SID пора записать logon (первый раз или окно истекло).</summary>
    public static bool ShouldLog(string sid)
    {
        var now = DateTimeOffset.UtcNow;
        if (Seen.TryGetValue(sid, out var last) && now - last < Window) return false;
        Seen[sid] = now;
        return true;
    }
}

/// <summary>Запись изменений конфигурации панели (Admin Audit Report).</summary>
public static class ConfigAudit
{
    /// <param name="feature">Раздел: "Help Desk Role", "Scope", "Assignment", "Local User", "Settings"</param>
    /// <param name="instance">Имя изменённого объекта</param>
    /// <param name="action">"Created" | "Modified" | "Deleted"</param>
    public static Task WriteAsync(IAuditLog audit, TechnicianContext actor, string feature, string instance, string action, string? before = null, string? after = null)
        => audit.WriteAsync(new AuditEntry
        {
            Kind = AuditKind.ConfigChange,
            TimestampMsk = MskTime.Now,
            ActorSid = actor.Sid,
            ActorName = actor.DisplayName,
            Success = true,
            Feature = feature,
            Instance = instance,
            Action = action,
            Before = before,
            After = after,
        });
}
