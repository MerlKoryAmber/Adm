using System.DirectoryServices;
using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Domain.Enums;
using AdManager.Infrastructure.Ad;
using AdManager.Infrastructure.Data;
using Xunit;

namespace AdManager.Integration.Tests;

/// <summary>
/// Live-срез операций AD против лабы merl.loc через AdManagementService.
/// Пропускается, если .env не найден (объекты создаёт build/lab-setup.ps1).
/// </summary>
public class ResetPasswordSliceTests
{
    private const string UsersOu = "OU=Users,OU=AdManagerLab,DC=Merl,DC=loc";
    private const string GroupDn = "CN=HelpDesk-L1,OU=Groups,OU=AdManagerLab,DC=Merl,DC=loc";
    private const string TestUserDn = "CN=Test User1,OU=Users,OU=AdManagerLab,DC=Merl,DC=loc";
    private const string TestUserUpn = "test.user1@merl.loc";

    private static (AdManagementService mgmt, AdDirectory dir, TechnicianContext actor) Build(Dictionary<string, string> env)
    {
        var opt = new AdConnectionOptions { Server = env["AD_DC"], Domain = "merl.loc" };
        var cred = new ConfiguredCredentialProvider(new OperationalCredentialOptions
        {
            Mode = "StoredCredential",
            User = env["AD_USER"],
            Password = env["AD_PASSWORD"],
        });
        var ad = new AdService(opt, cred);
        var dir = new AdDirectory(opt, cred);
        var audit = new FileAuditLog(Path.Combine(RepoRoot(), "artifacts", "audit.jsonl"));
        var mgmt = new AdManagementService(new AllowAllRbacEngine(), ad, audit);
        var actor = new TechnicianContext("S-1-5-lab-tech", "tech@merl.loc", "Lab Tech");
        return (mgmt, dir, actor);
    }

    [SkippableFact]
    public async Task Reset_password_flows_through_pipeline_and_takes_effect()
    {
        var env = LoadEnv();
        Skip.If(env is null, "нет .env — лаба недоступна");
        var (mgmt, _, actor) = Build(env!);

        var newPwd = "Rp!" + Guid.NewGuid().ToString("N")[..8] + "aB1";
        var result = await mgmt.ResetPasswordAsync(actor, TestUserDn, newPwd, mustChange: false);

        Assert.True(result.Success, result.Message);
        Assert.True(CanBind(env!["AD_DC"], TestUserUpn, newPwd), "вход новым паролем не удался");
    }

    [SkippableFact]
    public async Task Disable_then_enable_reflects_in_directory()
    {
        var env = LoadEnv();
        Skip.If(env is null, "нет .env");
        var (mgmt, dir, actor) = Build(env!);

        Assert.True((await mgmt.SetEnabledAsync(actor, TestUserDn, false)).Success);
        Assert.False((await FindUser(dir, "test.user1")).Enabled);

        Assert.True((await mgmt.SetEnabledAsync(actor, TestUserDn, true)).Success);
        Assert.True((await FindUser(dir, "test.user1")).Enabled);
    }

    [SkippableFact]
    public async Task Unlock_succeeds()
    {
        var env = LoadEnv();
        Skip.If(env is null, "нет .env");
        var (mgmt, _, actor) = Build(env!);
        Assert.True((await mgmt.UnlockAsync(actor, TestUserDn)).Success);
    }

    [SkippableFact]
    public async Task Create_modify_group_delete_user_lifecycle()
    {
        var env = LoadEnv();
        Skip.If(env is null, "нет .env");
        var (mgmt, dir, actor) = Build(env!);

        var sam = "it.tmp" + Guid.NewGuid().ToString("N")[..6];
        var dn = $"CN=Temp {sam},{UsersOu}";
        var req = new CreateUserRequest(UsersOu, sam, $"Temp {sam}", $"{sam}@merl.loc",
            InitialPassword: "Tmp!" + Guid.NewGuid().ToString("N")[..8] + "Zz1", Enabled: true,
            Attributes: new Dictionary<string, string?> { ["department"] = "IT", ["title"] = "Engineer" });

        try
        {
            Assert.True((await mgmt.CreateUserAsync(actor, req)).Success);
            var created = await FindUser(dir, sam);
            Assert.True(created.Enabled);

            // modify attribute
            Assert.True((await mgmt.SetAttributesAsync(actor, dn, new Dictionary<string, string?> { ["title"] = "Senior Engineer" })).Success);
            var det = await dir.GetObjectAsync(dn, new[] { "title" });
            Assert.Equal("Senior Engineer", det!.Attributes["title"]);

            // add to group + verify membership
            Assert.True((await mgmt.ManageGroupMembershipAsync(actor, GroupDn, new[] { dn }, Array.Empty<string>())).Success);
            var members = await dir.ListGroupMembersAsync(GroupDn);
            Assert.Contains(members, m => m.SamAccountName == sam);

            // remove from group
            Assert.True((await mgmt.ManageGroupMembershipAsync(actor, GroupDn, Array.Empty<string>(), new[] { dn })).Success);
        }
        finally
        {
            await mgmt.DeleteAsync(actor, dn); // cleanup
        }

        Assert.DoesNotContain(await dir.ListUsersAsync(UsersOu, subtree: false), u => u.SamAccountName == sam);
    }

    private static async Task<AdUserSummary> FindUser(AdDirectory dir, string sam)
    {
        var users = await dir.ListUsersAsync(UsersOu, subtree: false);
        return users.Single(u => u.SamAccountName == sam);
    }

    private static bool CanBind(string server, string upn, string password)
    {
        try
        {
            using var de = new DirectoryEntry($"LDAP://{server}/RootDSE", upn, password, AuthenticationTypes.Secure);
            _ = de.Properties["defaultNamingContext"].Value;
            return true;
        }
        catch { return false; }
    }

    private static Dictionary<string, string>? LoadEnv()
    {
        var path = FindUp(".env");
        if (path is null) return null;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
            var i = line.IndexOf('=');
            map[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return map.ContainsKey("AD_DC") ? map : null;
    }

    private static string RepoRoot()
    {
        var envPath = FindUp(".env");
        return envPath is not null ? Path.GetDirectoryName(envPath)! : AppContext.BaseDirectory;
    }

    private static string? FindUp(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
