using AdManager.Domain.Enums;

namespace AdManager.Domain;

public enum SubjectType
{
    Technician,
    AdGroup,
}

public sealed record Technician(string Sid, string Upn, string DisplayName);

/// <summary>Роль = набор разрешённых операций.</summary>
public sealed class HelpDeskRole
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public HashSet<Permission> Permissions { get; init; } = new();
}

/// <summary>На какие объекты распространяется роль.</summary>
public sealed class DelegationScope
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public List<string> OuDns { get; init; } = new();
    public List<string> GroupDns { get; init; } = new();
    public bool IncludeSubtree { get; init; } = true;
}

/// <summary>Назначение: субъект (техник или AD-группа) → роль → scope.</summary>
public sealed class RoleAssignment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required SubjectType SubjectType { get; init; }

    /// <summary>SID техника или AD-группы (стабильный ключ для сверки с токеном).</summary>
    public required string SubjectSid { get; init; }
    /// <summary>Читаемое имя субъекта (для UI; на авторизацию не влияет).</summary>
    public string? SubjectName { get; init; }
    public required Guid RoleId { get; init; }
    public required Guid ScopeId { get; init; }
}

/// <summary>Фаза записи аудита: намерение (до операции) и результат (после).</summary>
public enum AuditPhase
{
    Attempt,
    Result,
}

/// <summary>Неизменяемая запись аудита.</summary>
public sealed record AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Attempt пишется ДО операции, Result — ПОСЛЕ (закрывает окно неаудируемого изменения).</summary>
    public AuditPhase Phase { get; init; } = AuditPhase.Result;

    /// <summary>Время в МСК (§20).</summary>
    public required DateTimeOffset TimestampMsk { get; init; }
    public required string ActorSid { get; init; }
    public required string ActorName { get; init; }
    public required Permission Operation { get; init; }
    public required string TargetDn { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record OperationResult(bool Success, string? Message = null, string? CorrelationId = null)
{
    public static OperationResult Ok(string? message = null) => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}
