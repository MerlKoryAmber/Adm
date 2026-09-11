using System.Security.Claims;
using AdManager.Application.Abstractions;

namespace AdManager.Web;

/// <summary>Параметры UI: какие OU показывать (лаба по умолчанию).</summary>
public sealed class UiOptions
{
    public string Domain { get; set; } = "merl.loc";
    public string BaseDn { get; set; } = "DC=Merl,DC=loc";
    public string RootOu { get; set; } = "OU=AdManagerLab,DC=Merl,DC=loc";
    public string UsersOu { get; set; } = "OU=Users,OU=AdManagerLab,DC=Merl,DC=loc";
    public string GroupsOu { get; set; } = "OU=Groups,OU=AdManagerLab,DC=Merl,DC=loc";
}

/// <summary>Оверлей секретов из .env (лаба, не в git) в конфигурацию.</summary>
public static class DotEnv
{
    public static void Overlay(WebApplicationBuilder builder)
    {
        var dir = new DirectoryInfo(builder.Environment.ContentRootPath);
        string? path = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate)) { path = candidate; break; }
            dir = dir.Parent;
        }
        if (path is null) return;

        var map = new Dictionary<string, string?>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
            var i = line.IndexOf('=');
            var k = line[..i].Trim();
            var v = line[(i + 1)..].Trim();
            switch (k)
            {
                case "AD_DC": map["Ad:Server"] = v; break;
                case "AD_USER": map["Operational:User"] = v; map["Operational:Mode"] = "StoredCredential"; break;
                case "AD_PASSWORD": map["Operational:Password"] = v; break;
            }
        }
        builder.Configuration.AddInMemoryCollection(map);
    }
}

/// <summary>ClaimsPrincipal (Windows Auth) → TechnicianContext (актор для RBAC/аудита).</summary>
public static class CurrentUser
{
    private const string PrimarySid = "http://schemas.microsoft.com/ws/2008/06/identity/claims/primarysid";

    private const string GroupSidClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid";

    public static TechnicianContext From(ClaimsPrincipal? principal)
    {
        var name = principal?.Identity?.Name ?? "unknown";
        var sid = principal?.FindFirst(ClaimTypes.PrimarySid)?.Value
                  ?? principal?.FindFirst(PrimarySid)?.Value
                  ?? name;
        var groups = principal?.FindAll(ClaimTypes.GroupSid).Select(c => c.Value)
                     .Concat(principal.FindAll(GroupSidClaim).Select(c => c.Value))
                     .Distinct().ToList() ?? new List<string>();
        return new TechnicianContext(sid, name, name, groups);
    }
}
