using AdManager.Application.Abstractions;
using AdManager.Domain;
using AdManager.Domain.Enums;

namespace AdManager.Application;

/// <summary>Настройки RBAC-движка.</summary>
public sealed class RbacOptions
{
    /// <summary>sAMAccountName супер-админов (полный доступ, минуя назначения).</summary>
    public List<string> SuperAdmins { get; set; } = new() { "merl" };

    /// <summary>Считать членов Domain Admins (RID 512) супер-админами.</summary>
    public bool DomainAdminsAreSuper { get; set; } = true;

    /// <summary>SID актора автоматизации (полный доступ; операции идут через тот же аудит).</summary>
    public string AutomationSid { get; set; } = "AUTOMATION";
}

/// <summary>Полная конфигурация делегирования.</summary>
public sealed class RbacConfig
{
    public List<HelpDeskRole> Roles { get; set; } = new();
    public List<DelegationScope> Scopes { get; set; } = new();
    public List<RoleAssignment> Assignments { get; set; } = new();
}

/// <summary>Хранилище конфигурации делегирования.</summary>
public interface IRbacStore
{
    Task<RbacConfig> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(RbacConfig config, CancellationToken ct = default);
}

/// <summary>
/// Реальный RBAC: супер-админы — полный доступ; остальные — по назначениям
/// (субъект = SID техника или группы) → роль (набор операций) → scope (OU).
/// </summary>
public sealed class RbacEngine : IRbacEngine
{
    private readonly IRbacStore _store;
    private readonly RbacOptions _options;

    public RbacEngine(IRbacStore store, RbacOptions options)
    {
        _store = store;
        _options = options;
    }

    public async Task<AuthorizationDecision> AuthorizeAsync(TechnicianContext actor, Permission operation, string targetDn, CancellationToken ct = default)
    {
        if (IsSuperAdmin(actor))
        {
            return new AuthorizationDecision(true, "super-admin");
        }

        var cfg = await _store.LoadAsync(ct);
        var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { actor.Sid };
        if (actor.GroupSids is not null)
        {
            foreach (var g in actor.GroupSids) subjects.Add(g);
        }

        foreach (var a in cfg.Assignments.Where(a => subjects.Contains(a.SubjectSid)))
        {
            var role = cfg.Roles.FirstOrDefault(r => r.Id == a.RoleId);
            if (role is null || !role.Permissions.Contains(operation)) continue;
            var scope = cfg.Scopes.FirstOrDefault(s => s.Id == a.ScopeId);
            if (scope is not null && ScopeMatches(scope, targetDn))
            {
                return new AuthorizationDecision(true, $"role '{role.Name}' via scope '{scope.Name}'");
            }
        }

        return new AuthorizationDecision(false, $"no delegated role grants {operation} on target");
    }

    private bool IsSuperAdmin(TechnicianContext actor)
    {
        if (string.Equals(actor.Sid, _options.AutomationSid, StringComparison.Ordinal)) return true;
        var sam = Sam(actor.DisplayName) ?? Sam(actor.Upn);
        if (sam is not null && _options.SuperAdmins.Any(s => string.Equals(s, sam, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (_options.DomainAdminsAreSuper && actor.GroupSids is not null &&
            actor.GroupSids.Any(g => g.EndsWith("-512", StringComparison.Ordinal)))
            return true;
        return false;
    }

    private static string? Sam(string? nameOrUpn)
    {
        if (string.IsNullOrEmpty(nameOrUpn)) return null;
        var i = nameOrUpn.IndexOf('\\');
        if (i >= 0) return nameOrUpn[(i + 1)..];
        var at = nameOrUpn.IndexOf('@');
        return at > 0 ? nameOrUpn[..at] : nameOrUpn;
    }

    private static bool ScopeMatches(DelegationScope scope, string targetDn)
    {
        foreach (var ou in scope.OuDns)
        {
            if (scope.IncludeSubtree)
            {
                if (targetDn.EndsWith("," + ou, StringComparison.OrdinalIgnoreCase) ||
                    targetDn.Equals(ou, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                var comma = targetDn.IndexOf(',');
                var parent = comma >= 0 ? targetDn[(comma + 1)..] : targetDn;
                if (parent.Equals(ou, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }
}
