using System.Security.Claims;
using AdManager.Application.Abstractions;

namespace AdManager.Web;

/// <summary>Человекочитаемое представление DN для UI (без LDAP DC=… — у нас не LDAP-консоль).</summary>
public static class DnFormat
{
    /// <summary>DN → путь без доменных компонентов: "CN=John,OU=Sales,DC=x,DC=y" → "Sales / John".
    /// Порядок сверху вниз (родитель → объект). Экранированные запятые сохраняются.</summary>
    public static string Friendly(string? dn)
    {
        if (string.IsNullOrWhiteSpace(dn)) return "";
        var parts = SplitDn(dn)
            .Where(p => !p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => { var i = p.IndexOf('='); return i >= 0 ? p[(i + 1)..].Trim() : p.Trim(); })
            .ToList();
        parts.Reverse(); // сверху вниз
        return parts.Count == 0 ? dn : string.Join(" / ", parts);
    }

    private static IEnumerable<string> SplitDn(string dn)
    {
        // разбиение по запятым, не экранированным обратным слэшем
        var cur = new System.Text.StringBuilder();
        for (int i = 0; i < dn.Length; i++)
        {
            var c = dn[i];
            if (c == ',' && (i == 0 || dn[i - 1] != '\\')) { yield return cur.ToString(); cur.Clear(); }
            else cur.Append(c);
        }
        if (cur.Length > 0) yield return cur.ToString();
    }
}

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

    /// <summary>Claim супер-админа для локальных УЗ панели (ADR-0007).</summary>
    public const string SuperAdminClaim = "admgr:super";

    public static TechnicianContext From(ClaimsPrincipal? principal)
    {
        var name = principal?.Identity?.Name ?? "unknown";
        var sid = principal?.FindFirst(ClaimTypes.PrimarySid)?.Value
                  ?? principal?.FindFirst(PrimarySid)?.Value
                  ?? name;
        var groups = principal?.FindAll(ClaimTypes.GroupSid).Select(c => c.Value)
                     .Concat(principal.FindAll(GroupSidClaim).Select(c => c.Value))
                     .Distinct().ToList() ?? new List<string>();
        var isSuper = principal?.FindFirst(SuperAdminClaim)?.Value == "true";
        return new TechnicianContext(sid, name, name, groups, isSuper);
    }
}
