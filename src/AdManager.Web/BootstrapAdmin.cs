using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Domain;
using AdManager.Domain.Enums;

namespace AdManager.Web;

/// <summary>
/// Bootstrap встроенных объектов при установке (ADR-0007): роль Administrators
/// (super-admin, builtin) + назначение встроенного admin в неё. Идемпотентно.
/// </summary>
public static class BootstrapAdmin
{
    public const string AdminRoleName = "Administrators";

    public static async Task EnsureAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        var rbac = sp.GetRequiredService<IRbacStore>();
        var localUsers = sp.GetService<ILocalUserStore>();
        var baseDn = sp.GetRequiredService<UiOptions>().BaseDn;

        // Апгрейд существующего стенда: пометить admin как встроенного, если ещё не помечен.
        if (localUsers is not null)
        {
            var existingAdmin = await localUsers.GetByNameAsync("admin", ct);
            if (existingAdmin is not null && !existingAdmin.IsBuiltin)
            {
                existingAdmin.IsBuiltin = true;
                await localUsers.SaveAsync(existingAdmin, ct);
            }
        }

        var cfg = await rbac.LoadAsync(ct);

        // 1) встроенная роль Administrators. Существующую одноимённую super-роль (создана вручную)
        //    помечаем builtin — апгрейд стенда.
        var role = cfg.Roles.FirstOrDefault(r => r.IsBuiltin)
                   ?? cfg.Roles.FirstOrDefault(r => r.Name == AdminRoleName && r.IsSuperAdmin);
        var changed = false;
        if (role is null)
        {
            role = new HelpDeskRole { Name = AdminRoleName, IsSuperAdmin = true, IsBuiltin = true };
            cfg.Roles.Add(role);
            changed = true;
        }
        else if (!role.IsBuiltin)
        {
            // HelpDeskRole.IsBuiltin — init-only; пересоздаём запись с тем же Id, помеченную builtin.
            cfg.Roles.Remove(role);
            role = new HelpDeskRole
            {
                Id = role.Id, Name = role.Name, IsSuperAdmin = role.IsSuperAdmin, IsBuiltin = true,
                Permissions = role.Permissions, CreateUserFields = role.CreateUserFields, ModifyUserFields = role.ModifyUserFields,
            };
            cfg.Roles.Add(role);
            changed = true;
        }

        // 2) встроенный scope (весь домен) — назначению нужен ScopeId (super-роль его игнорирует)
        var scope = cfg.Scopes.FirstOrDefault(s => s.Name == "Whole domain");
        if (scope is null)
        {
            scope = new DelegationScope { Name = "Whole domain", OuDns = { baseDn }, IncludeSubtree = true };
            cfg.Scopes.Add(scope);
            changed = true;
        }

        // 3) назначение встроенного admin -> Administrators
        var admin = localUsers is null ? null : await localUsers.GetByNameAsync("admin", ct);
        if (admin is not null && !cfg.Assignments.Any(a => a.SubjectSid == admin.Sid && a.RoleId == role.Id))
        {
            cfg.Assignments.Add(new RoleAssignment
            {
                SubjectType = SubjectType.Technician,
                SubjectSid = admin.Sid,
                SubjectName = admin.UserName,
                RoleId = role.Id,
                ScopeId = scope.Id,
            });
            changed = true;
        }

        if (changed) await rbac.SaveAsync(cfg, ct);
    }
}
