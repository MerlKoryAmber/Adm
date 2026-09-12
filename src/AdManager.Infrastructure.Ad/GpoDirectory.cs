using System.DirectoryServices;
using System.Text.RegularExpressions;
using AdManager.Application;
using AdManager.Application.Abstractions;

namespace AdManager.Infrastructure.Ad;

/// <summary>Чтение групповых политик и их линков через LDAP (без GPMC).</summary>
public sealed partial class GpoDirectory : IGpoDirectory
{
    private readonly AdConnectionOptions _opt;
    private readonly IOperationalCredentialProvider _cred;

    public GpoDirectory(AdConnectionOptions opt, IOperationalCredentialProvider cred)
    {
        _opt = opt;
        _cred = cred;
    }

    private DirectoryEntry Bind(string dn) => Ldap.Bind(_opt, _cred, dn);

    internal string BaseDn()
    {
        if (!string.IsNullOrWhiteSpace(_opt.BaseDn)) return _opt.BaseDn!;
        using var root = Bind("RootDSE");
        return root.Properties["defaultNamingContext"][0]?.ToString() ?? "";
    }

    private string PoliciesDn() => $"CN=Policies,CN=System,{BaseDn()}";

    public Task<IReadOnlyList<GpoSummary>> ListGposAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<GpoSummary>>(() => ReadGpos(), ct);

    private List<GpoSummary> ReadGpos()
    {
        using var root = Bind(PoliciesDn());
        using var s = new DirectorySearcher(root)
        {
            Filter = "(objectClass=groupPolicyContainer)",
            SearchScope = SearchScope.OneLevel,
            PageSize = 1000,
        };
        foreach (var p in new[] { "displayName", "cn", "distinguishedName", "versionNumber", "flags", "whenChanged" })
            s.PropertiesToLoad.Add(p);

        var list = new List<GpoSummary>();
        using var results = s.FindAll();
        foreach (SearchResult r in results)
        {
            var flags = GetInt(r, "flags");
            list.Add(new GpoSummary(
                Str(r, "cn"),
                StrOr(r, "displayName", Str(r, "cn")),
                Str(r, "distinguishedName"),
                GetInt(r, "versionNumber"),
                UserSettingsDisabled: (flags & 1) != 0,
                ComputerSettingsDisabled: (flags & 2) != 0,
                WhenChanged: GetDate(r, "whenChanged")));
        }
        return list.OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<IReadOnlyList<GpoLink>> ListLinksAsync(string scopeDn, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<GpoLink>>(() =>
        {
            var names = ReadGpos().ToDictionary(g => g.Id, g => g.DisplayName, StringComparer.OrdinalIgnoreCase);
            using var de = Bind(scopeDn);
            de.RefreshCache();
            var gplink = de.Properties["gPLink"].Count > 0 ? de.Properties["gPLink"][0]?.ToString() : null;
            var scopeName = ScopeName(de, scopeDn);
            return ParseLinks(scopeDn, scopeName, gplink, names);
        }, ct);

    public Task<IReadOnlyList<GpoLink>> ListAllLinksAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<GpoLink>>(() =>
        {
            var names = ReadGpos().ToDictionary(g => g.Id, g => g.DisplayName, StringComparer.OrdinalIgnoreCase);
            var all = new List<GpoLink>();

            // домен-объект
            var baseDn = BaseDn();
            using (var dom = Bind(baseDn))
            {
                dom.RefreshCache();
                var gplink = dom.Properties["gPLink"].Count > 0 ? dom.Properties["gPLink"][0]?.ToString() : null;
                all.AddRange(ParseLinks(baseDn, "(domain root)", gplink, names));
            }

            // все OU
            using var root = Bind(baseDn);
            using var s = new DirectorySearcher(root)
            {
                Filter = "(objectClass=organizationalUnit)",
                SearchScope = SearchScope.Subtree,
                PageSize = 1000,
            };
            s.PropertiesToLoad.Add("distinguishedName");
            s.PropertiesToLoad.Add("ou");
            s.PropertiesToLoad.Add("gPLink");
            using var results = s.FindAll();
            foreach (SearchResult r in results)
            {
                var dn = Str(r, "distinguishedName");
                var gplink = r.Properties["gPLink"].Count > 0 ? r.Properties["gPLink"][0]?.ToString() : null;
                if (string.IsNullOrWhiteSpace(gplink)) continue;
                all.AddRange(ParseLinks(dn, StrOr(r, "ou", dn), gplink, names));
            }
            return all;
        }, ct);

    private static List<GpoLink> ParseLinks(string scopeDn, string scopeName, string? gplink, IReadOnlyDictionary<string, string> names)
    {
        var list = new List<GpoLink>();
        if (string.IsNullOrWhiteSpace(gplink)) return list;
        var order = 0;
        foreach (Match m in GpLinkRegex().Matches(gplink))
        {
            order++;
            var id = ExtractCn(m.Groups["path"].Value);
            var opt = int.TryParse(m.Groups["opt"].Value, out var o) ? o : 0;
            list.Add(new GpoLink(
                scopeDn, scopeName, id,
                names.TryGetValue(id, out var n) ? n : id,
                order,
                Enforced: (opt & 2) != 0,
                Enabled: (opt & 1) == 0));
        }
        return list;
    }

    private static string ScopeName(DirectoryEntry de, string dn)
    {
        var ou = de.Properties["ou"].Count > 0 ? de.Properties["ou"][0]?.ToString() : null;
        if (!string.IsNullOrEmpty(ou)) return ou!;
        var name = de.Properties["name"].Count > 0 ? de.Properties["name"][0]?.ToString() : null;
        return name ?? dn;
    }

    private static string ExtractCn(string ldapPath)
    {
        // ldapPath: "LDAP://cn={GUID},cn=policies,cn=system,DC=.." или "cn={GUID},.."
        var m = Regex.Match(ldapPath, @"cn=(\{[0-9A-Fa-f\-]+\})", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : ldapPath;
    }

    // ---- helpers ----
    private static string Str(SearchResult r, string p) => r.Properties[p].Count > 0 ? r.Properties[p][0]?.ToString() ?? "" : "";
    private static string StrOr(SearchResult r, string p, string fallback) { var v = Str(r, p); return string.IsNullOrEmpty(v) ? fallback : v; }
    private static int GetInt(SearchResult r, string p) => r.Properties[p].Count > 0 && int.TryParse(r.Properties[p][0]?.ToString(), out var v) ? v : 0;
    private static DateTime? GetDate(SearchResult r, string p) => r.Properties[p].Count > 0 && r.Properties[p][0] is DateTime dt ? dt : null;

    [GeneratedRegex(@"\[LDAP://(?<path>[^;\]]+);(?<opt>\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex GpLinkRegex();
}
