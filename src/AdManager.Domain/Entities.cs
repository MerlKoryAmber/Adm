using AdManager.Domain.Enums;

namespace AdManager.Domain;

public enum SubjectType
{
    Technician,
    AdGroup,
}

public sealed record Technician(string Sid, string Upn, string DisplayName);

/// <summary>Роль = набор разрешённых операций + (опционально) пополевые ограничения.</summary>
public sealed class HelpDeskRole
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public HashSet<Permission> Permissions { get; init; } = new();

    /// <summary>Роль супер-администратора: полный доступ в обход назначений/scope (как bootstrap-merl,
    /// но управляется через UI). Носителя такой роли RBAC считает супер-админом.</summary>
    public bool IsSuperAdmin { get; init; }

    /// <summary>Ключи полей (FieldCatalog), которые роль разрешает задавать при создании пользователя.
    /// Пустой набор = ограничений нет (все поля), обратная совместимость. Уточняет <see cref="Permission.CreateUser"/>.</summary>
    public HashSet<string> CreateUserFields { get; init; } = new();

    /// <summary>Ключи полей (FieldCatalog), которые роль разрешает менять у пользователя.
    /// Пустой набор = ограничений нет (все поля). Уточняет <see cref="Permission.ModifyAttributes"/>.</summary>
    public HashSet<string> ModifyUserFields { get; init; } = new();
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

    /// <summary>Принудительный шаблон формы создания пользователя для этого назначения (null = без принуждения).</summary>
    public Guid? CreateTemplateId { get; init; }
    /// <summary>Принудительный шаблон формы модификации пользователя (null = без принуждения).</summary>
    public Guid? ModifyTemplateId { get; init; }
}

/// <summary>Фаза записи аудита: намерение (до операции) и результат (после).</summary>
public enum AuditPhase
{
    Attempt,
    Result,
}

/// <summary>Тип аудит-записи (для разных отчётов Delegation, эталон ADManager Plus):
/// AdOperation — операция техника над AD (Audit Report);
/// ConfigChange — изменение конфигурации панели: роли/скоупы/назначения/УЗ/настройки (Admin Audit Report);
/// Logon — вход техника в панель (Technician Logon Report).</summary>
public enum AuditKind
{
    AdOperation,
    ConfigChange,
    Logon,
}

/// <summary>Неизменяемая запись аудита.</summary>
public sealed record AuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Attempt пишется ДО операции, Result — ПОСЛЕ (закрывает окно неаудируемого изменения).</summary>
    public AuditPhase Phase { get; init; } = AuditPhase.Result;

    /// <summary>Тип записи (AD-операция / изменение конфига / вход). По умолчанию AdOperation.</summary>
    public AuditKind Kind { get; init; } = AuditKind.AdOperation;

    /// <summary>Время в МСК (§20).</summary>
    public required DateTimeOffset TimestampMsk { get; init; }
    public required string ActorSid { get; init; }
    public required string ActorName { get; init; }
    /// <summary>Для AdOperation — операция AD; для Config/Logon — не значима (Operation.None-подобное).</summary>
    public Permission Operation { get; init; }
    public string TargetDn { get; init; } = "";
    public string? Before { get; init; }
    public string? After { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? CorrelationId { get; init; }

    // --- ConfigChange (Admin Audit Report) ---
    /// <summary>Раздел панели: "Help Desk Role", "Scope", "Assignment", "Local User", "Settings"…</summary>
    public string? Feature { get; init; }
    /// <summary>Имя изменённого объекта (роль "L1", УЗ "operator1"…).</summary>
    public string? Instance { get; init; }
    /// <summary>Действие: "Created" | "Modified" | "Deleted".</summary>
    public string? Action { get; init; }

    // --- Logon (Technician Logon Report) ---
    /// <summary>IP/хост, с которого вход.</summary>
    public string? Host { get; init; }
    /// <summary>Способ входа: "Password" (локальный) | "SSO" (Windows/Negotiate).</summary>
    public string? Method { get; init; }
}

public sealed record OperationResult(bool Success, string? Message = null, string? CorrelationId = null)
{
    public static OperationResult Ok(string? message = null) => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}
