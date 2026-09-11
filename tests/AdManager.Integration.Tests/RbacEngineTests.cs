using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Domain;
using AdManager.Domain.Enums;
using AdManager.Infrastructure.Data;
using Xunit;

namespace AdManager.Integration.Tests;

/// <summary>Юнит-проверка RbacEngine (без AD/лабы).</summary>
public class RbacEngineTests
{
    private const string SalesOu = "OU=Sales,DC=Merl,DC=loc";
    private const string TargetInSales = "CN=John,OU=Sales,DC=Merl,DC=loc";
    private const string TargetOutside = "CN=Jane,OU=IT,DC=Merl,DC=loc";

    private static (RbacEngine engine, RbacConfig cfg) Build()
    {
        var path = Path.Combine(Path.GetTempPath(), "admgr_rbac_" + Guid.NewGuid().ToString("N") + ".json");
        var store = new FileRbacStore(path);
        var role = new HelpDeskRole { Name = "L1", Permissions = { Permission.ResetPassword } };
        var scope = new DelegationScope { Name = "Sales", OuDns = { SalesOu }, IncludeSubtree = true };
        var cfg = new RbacConfig { Roles = { role }, Scopes = { scope } };
        cfg.Assignments.Add(new RoleAssignment { SubjectType = SubjectType.Technician, SubjectSid = "S-1-tech", RoleId = role.Id, ScopeId = scope.Id });
        store.SaveAsync(cfg).GetAwaiter().GetResult();
        return (new RbacEngine(store, new RbacOptions { SuperAdmins = { "admin" } }), cfg);
    }

    [Fact]
    public async Task SuperAdmin_allowed_everywhere()
    {
        var (engine, _) = Build();
        var actor = new TechnicianContext("S-1-x", "MERL\\admin", "MERL\\admin");
        var d = await engine.AuthorizeAsync(actor, Permission.DeleteUser, TargetOutside);
        Assert.True(d.Allowed);
    }

    [Fact]
    public async Task DomainAdmins_group_is_super()
    {
        var (engine, _) = Build();
        var actor = new TechnicianContext("S-1-y", "MERL\\bob", "MERL\\bob", new[] { "S-1-5-21-1-2-3-512" });
        Assert.True((await engine.AuthorizeAsync(actor, Permission.DeleteUser, TargetOutside)).Allowed);
    }

    [Fact]
    public async Task Technician_allowed_in_scope_denied_outside_and_for_other_perms()
    {
        var (engine, _) = Build();
        var tech = new TechnicianContext("S-1-tech", "MERL\\tech", "MERL\\tech");

        Assert.True((await engine.AuthorizeAsync(tech, Permission.ResetPassword, TargetInSales)).Allowed);
        Assert.False((await engine.AuthorizeAsync(tech, Permission.ResetPassword, TargetOutside)).Allowed);
        Assert.False((await engine.AuthorizeAsync(tech, Permission.DeleteUser, TargetInSales)).Allowed);
    }

    [Fact]
    public async Task Unknown_subject_denied()
    {
        var (engine, _) = Build();
        var stranger = new TechnicianContext("S-1-nobody", "MERL\\nobody", "MERL\\nobody");
        Assert.False((await engine.AuthorizeAsync(stranger, Permission.ResetPassword, TargetInSales)).Allowed);
    }
}
