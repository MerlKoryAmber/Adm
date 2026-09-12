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

    // --- Пополевые права (CreateUser / ModifyAttributes) ---

    private static RbacEngine BuildWithModifyFields(params string[] fields)
    {
        var path = Path.Combine(Path.GetTempPath(), "admgr_rbac_" + Guid.NewGuid().ToString("N") + ".json");
        var store = new FileRbacStore(path);
        var role = new HelpDeskRole { Name = "L1", Permissions = { Permission.ModifyAttributes } };
        foreach (var f in fields) role.ModifyUserFields.Add(f);
        var scope = new DelegationScope { Name = "Sales", OuDns = { SalesOu }, IncludeSubtree = true };
        var cfg = new RbacConfig { Roles = { role }, Scopes = { scope } };
        cfg.Assignments.Add(new RoleAssignment { SubjectType = SubjectType.Technician, SubjectSid = "S-1-tech", RoleId = role.Id, ScopeId = scope.Id });
        store.SaveAsync(cfg).GetAwaiter().GetResult();
        return new RbacEngine(store, new RbacOptions { SuperAdmins = { "admin" } });
    }

    private static readonly TechnicianContext Tech = new("S-1-tech", "MERL\\tech", "MERL\\tech");

    [Fact]
    public async Task Fields_restricted_to_selected_set()
    {
        var engine = BuildWithModifyFields("telephoneNumber", "mobile");
        var fp = await engine.AllowedFieldsAsync(Tech, Permission.ModifyAttributes, TargetInSales);
        Assert.False(fp.Unrestricted);
        Assert.True(fp.Allows("telephoneNumber"));
        Assert.True(fp.Allows("mobile"));
        Assert.False(fp.Allows("department"));
    }

    [Fact]
    public async Task Empty_field_set_means_unrestricted()
    {
        var engine = BuildWithModifyFields(); // роль с ModifyAttributes без пополевого списка
        var fp = await engine.AllowedFieldsAsync(Tech, Permission.ModifyAttributes, TargetInSales);
        Assert.True(fp.Unrestricted);
        Assert.True(fp.Allows("anything"));
    }

    [Fact]
    public async Task SuperAdmin_unrestricted_fields()
    {
        var engine = BuildWithModifyFields("telephoneNumber");
        var admin = new TechnicianContext("S-1-a", "MERL\\admin", "MERL\\admin");
        var fp = await engine.AllowedFieldsAsync(admin, Permission.ModifyAttributes, TargetInSales);
        Assert.True(fp.Unrestricted);
    }

    [Fact]
    public async Task No_matching_role_out_of_scope_grants_no_fields()
    {
        var engine = BuildWithModifyFields("telephoneNumber");
        var fp = await engine.AllowedFieldsAsync(Tech, Permission.ModifyAttributes, TargetOutside);
        Assert.False(fp.Unrestricted);
        Assert.False(fp.Allows("telephoneNumber"));
    }

    [Fact]
    public async Task Non_field_operation_is_unrestricted()
    {
        var engine = BuildWithModifyFields("telephoneNumber");
        var fp = await engine.AllowedFieldsAsync(Tech, Permission.ResetPassword, TargetInSales);
        Assert.True(fp.Unrestricted); // пополевые ограничения не применяются к прочим операциям
    }

    [Fact]
    public void Field_sets_round_trip_through_store()
    {
        var path = Path.Combine(Path.GetTempPath(), "admgr_rbac_" + Guid.NewGuid().ToString("N") + ".json");
        var store = new FileRbacStore(path);
        var role = new HelpDeskRole { Name = "L1", Permissions = { Permission.ModifyAttributes } };
        role.ModifyUserFields.Add("telephoneNumber");
        role.CreateUserFields.Add("givenName");
        store.SaveAsync(new RbacConfig { Roles = { role } }).GetAwaiter().GetResult();

        var back = store.LoadAsync().GetAwaiter().GetResult().Roles.Single();
        Assert.Contains("telephoneNumber", back.ModifyUserFields);
        Assert.Contains("givenName", back.CreateUserFields);
    }
}
