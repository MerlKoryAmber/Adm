using System.DirectoryServices;
using AdManager.Application;
using AdManager.Application.Abstractions;

namespace AdManager.Infrastructure.Ad;

/// <summary>Чтение каталога AD (DirectorySearcher) под operational identity.</summary>
public sealed class AdDirectory : IAdDirectory
{
    private const int UF_ACCOUNTDISABLE = 0x2;

    private readonly AdConnectionOptions _opt;
    private readonly IOperationalCredentialProvider _cred;

    public AdDirectory(AdConnectionOptions opt, IOperationalCredentialProvider cred)
    {
        _opt = opt;
        _cred = cred;
    }

    private DirectoryEntry Bind(string dn) => Ldap.Bind(_opt, _cred, dn);

    public Task<IReadOnlyList<AdUserSummary>> ListUsersAsync(string ouDn, bool subtree, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdUserSummary>>(() =>
        {
            using var root = Bind(ouDn);
            using var s = new DirectorySearcher(root)
            {
                Filter = "(&(objectCategory=person)(objectClass=user))",
                SearchScope = subtree ? SearchScope.Subtree : SearchScope.OneLevel,
                PageSize = 1000,
            };
            foreach (var p in new[] { "distinguishedName", "sAMAccountName", "displayName", "userPrincipalName", "mail", "userAccountControl", "lockoutTime" })
                s.PropertiesToLoad.Add(p);

            var list = new List<AdUserSummary>();
            using var results = s.FindAll();
            foreach (SearchResult r in results)
            {
                var uac = GetInt(r, "userAccountControl");
                var lockout = GetLong(r, "lockoutTime");
                list.Add(new AdUserSummary(
                    Str(r, "distinguishedName"),
                    Str(r, "sAMAccountName"),
                    Str(r, "displayName"),
                    StrOrNull(r, "userPrincipalName"),
                    StrOrNull(r, "mail"),
                    Enabled: (uac & UF_ACCOUNTDISABLE) == 0,
                    LockedOut: lockout > 0));
            }
            return list.OrderBy(u => u.DisplayName).ToList();
        }, ct);

    public Task<IReadOnlyList<AdGroupSummary>> ListGroupsAsync(string ouDn, bool subtree, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdGroupSummary>>(() =>
        {
            using var root = Bind(ouDn);
            using var s = new DirectorySearcher(root)
            {
                Filter = "(objectClass=group)",
                SearchScope = subtree ? SearchScope.Subtree : SearchScope.OneLevel,
                PageSize = 1000,
            };
            foreach (var p in new[] { "distinguishedName", "sAMAccountName", "name" })
                s.PropertiesToLoad.Add(p);

            var list = new List<AdGroupSummary>();
            using var results = s.FindAll();
            foreach (SearchResult r in results)
                list.Add(new AdGroupSummary(Str(r, "distinguishedName"), Str(r, "sAMAccountName"), Str(r, "name")));
            return list.OrderBy(g => g.Name).ToList();
        }, ct);

    public Task<IReadOnlyList<AdOuSummary>> ListOusAsync(string parentDn, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdOuSummary>>(() =>
        {
            using var root = Bind(parentDn);
            using var s = new DirectorySearcher(root)
            {
                Filter = "(objectClass=organizationalUnit)",
                SearchScope = SearchScope.OneLevel,
                PageSize = 1000,
            };
            foreach (var p in new[] { "distinguishedName", "name" })
                s.PropertiesToLoad.Add(p);

            var list = new List<AdOuSummary>();
            using var results = s.FindAll();
            foreach (SearchResult r in results)
                list.Add(new AdOuSummary(Str(r, "distinguishedName"), Str(r, "name")));
            return list.OrderBy(o => o.Name).ToList();
        }, ct);

    public Task<AdObjectDetails?> GetObjectAsync(string dn, IEnumerable<string> attributes, CancellationToken ct = default)
        => Task.Run<AdObjectDetails?>(() =>
        {
            using var de = Bind(dn);
            de.RefreshCache();
            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in attributes)
            {
                map[a] = de.Properties[a].Count > 0 ? de.Properties[a][0]?.ToString() : null;
            }
            return new AdObjectDetails(dn, map);
        }, ct);

    public Task<IReadOnlyList<AdUserSummary>> ListGroupMembersAsync(string groupDn, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdUserSummary>>(() =>
        {
            using var g = Bind(groupDn);
            g.RefreshCache();
            var members = g.Properties["member"];
            var list = new List<AdUserSummary>();
            foreach (var m in members)
            {
                var memberDn = m?.ToString();
                if (string.IsNullOrEmpty(memberDn)) continue;
                try
                {
                    using var de = Bind(memberDn);
                    de.RefreshCache();
                    var uac = de.Properties["userAccountControl"].Count > 0 ? (int)de.Properties["userAccountControl"][0]! : 0;
                    var lockout = de.Properties["lockoutTime"].Count > 0 ? Convert.ToInt64(de.Properties["lockoutTime"][0]) : 0;
                    list.Add(new AdUserSummary(
                        memberDn,
                        de.Properties["sAMAccountName"].Count > 0 ? de.Properties["sAMAccountName"][0]!.ToString()! : "",
                        de.Properties["displayName"].Count > 0 ? de.Properties["displayName"][0]!.ToString()! : memberDn,
                        de.Properties["userPrincipalName"].Count > 0 ? de.Properties["userPrincipalName"][0]?.ToString() : null,
                        de.Properties["mail"].Count > 0 ? de.Properties["mail"][0]?.ToString() : null,
                        (uac & UF_ACCOUNTDISABLE) == 0,
                        lockout > 0));
                }
                catch { /* пропускаем недоступного члена */ }
            }
            return list.OrderBy(u => u.DisplayName).ToList();
        }, ct);

    public Task<IReadOnlyList<AdComputerSummary>> ListComputersAsync(string ouDn, bool subtree, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdComputerSummary>>(() =>
        {
            using var root = Bind(ouDn);
            using var s = new DirectorySearcher(root)
            {
                Filter = "(objectClass=computer)",
                SearchScope = subtree ? SearchScope.Subtree : SearchScope.OneLevel,
                PageSize = 1000,
            };
            foreach (var p in new[] { "distinguishedName", "sAMAccountName", "name", "userAccountControl", "operatingSystem" })
                s.PropertiesToLoad.Add(p);
            var list = new List<AdComputerSummary>();
            using var results = s.FindAll();
            foreach (SearchResult r in results)
            {
                var uac = GetInt(r, "userAccountControl");
                list.Add(new AdComputerSummary(Str(r, "distinguishedName"), Str(r, "sAMAccountName"), Str(r, "name"),
                    (uac & UF_ACCOUNTDISABLE) == 0, StrOrNull(r, "operatingSystem")));
            }
            return list.OrderBy(c => c.Name).ToList();
        }, ct);

    public Task<IReadOnlyList<AdContactSummary>> ListContactsAsync(string ouDn, bool subtree, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdContactSummary>>(() =>
        {
            using var root = Bind(ouDn);
            using var s = new DirectorySearcher(root)
            {
                Filter = "(objectClass=contact)",
                SearchScope = subtree ? SearchScope.Subtree : SearchScope.OneLevel,
                PageSize = 1000,
            };
            foreach (var p in new[] { "distinguishedName", "name", "displayName", "mail" })
                s.PropertiesToLoad.Add(p);
            var list = new List<AdContactSummary>();
            using var results = s.FindAll();
            foreach (SearchResult r in results)
            {
                var name = StrOrNull(r, "displayName") ?? Str(r, "name");
                list.Add(new AdContactSummary(Str(r, "distinguishedName"), name, StrOrNull(r, "mail")));
            }
            return list.OrderBy(c => c.Name).ToList();
        }, ct);

    public Task<IReadOnlyList<AdOuNode>> ListAllOusAsync(string baseDn, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdOuNode>>(() =>
        {
            using var root = Bind(baseDn);
            using var s = new DirectorySearcher(root)
            {
                Filter = "(objectClass=organizationalUnit)",
                SearchScope = SearchScope.Subtree,
                PageSize = 1000,
            };
            foreach (var p in new[] { "distinguishedName", "name" })
                s.PropertiesToLoad.Add(p);

            int baseDepth = OuCount(baseDn);
            var list = new List<AdOuNode>();
            using var results = s.FindAll();
            foreach (SearchResult r in results)
            {
                var dn = Str(r, "distinguishedName");
                list.Add(new AdOuNode(dn, Str(r, "name"), Math.Max(0, OuCount(dn) - baseDepth)));
            }
            return list.OrderBy(o => o.Dn.Length).ThenBy(o => o.Dn).ToList();
        }, ct);

    public Task<IReadOnlyList<AdSearchResult>> SearchAsync(string baseDn, string term, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdSearchResult>>(() =>
        {
            var t = EscapeFilter(term);
            using var root = Bind(baseDn);
            using var s = new DirectorySearcher(root)
            {
                Filter = "(&(|(objectCategory=person)(objectClass=group)(objectClass=computer)(objectClass=contact)(objectClass=organizationalUnit))" +
                         $"(|(cn=*{t}*)(sAMAccountName=*{t}*)(displayName=*{t}*)(mail=*{t}*)))",
                SearchScope = SearchScope.Subtree,
                PageSize = 500,
                SizeLimit = 500,
            };
            foreach (var p in new[] { "distinguishedName", "name", "displayName", "sAMAccountName", "objectClass", "userAccountControl" })
                s.PropertiesToLoad.Add(p);

            var list = new List<AdSearchResult>();
            using var results = s.FindAll();
            foreach (SearchResult r in results)
            {
                var cls = ClassOf(r);
                bool? enabled = r.Properties.Contains("userAccountControl") && r.Properties["userAccountControl"].Count > 0
                    ? (Convert.ToInt32(r.Properties["userAccountControl"][0]) & UF_ACCOUNTDISABLE) == 0
                    : null;
                var name = StrOrNull(r, "displayName") ?? Str(r, "name");
                list.Add(new AdSearchResult(Str(r, "distinguishedName"), name, StrOrNull(r, "sAMAccountName"), cls, enabled));
            }
            return list.OrderBy(x => x.ObjectClass).ThenBy(x => x.Name).ToList();
        }, ct);

    public Task<IReadOnlyList<AdUserLastLogon>> ListUsersWithLastLogonAsync(string baseDn, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdUserLastLogon>>(() =>
        {
            using var root = Bind(baseDn);
            using var s = new DirectorySearcher(root)
            {
                Filter = "(&(objectCategory=person)(objectClass=user))",
                SearchScope = SearchScope.Subtree,
                PageSize = 1000,
            };
            foreach (var p in new[] { "distinguishedName", "sAMAccountName", "displayName", "mail", "userAccountControl", "lastLogonTimestamp" })
                s.PropertiesToLoad.Add(p);

            var list = new List<AdUserLastLogon>();
            using var results = s.FindAll();
            foreach (SearchResult r in results)
            {
                var uac = GetInt(r, "userAccountControl");
                var raw = GetLong(r, "lastLogonTimestamp");
                // FILETIME (100ns с 1601-01-01 UTC). Отсутствие/0 = никогда не входил.
                DateTime? last = raw > 0 ? DateTime.FromFileTimeUtc(raw) : null;
                list.Add(new AdUserLastLogon(
                    Str(r, "distinguishedName"),
                    Str(r, "sAMAccountName"),
                    Str(r, "displayName"),
                    StrOrNull(r, "mail"),
                    Enabled: (uac & UF_ACCOUNTDISABLE) == 0,
                    LastLogonUtc: last));
            }
            return list.OrderBy(u => u.DisplayName).ToList();
        }, ct);

    public Task<IReadOnlyList<AdUserGroup>> ListUserGroupsAsync(string userDn, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<AdUserGroup>>(() =>
        {
            using var de = Bind(userDn);
            de.RefreshCache(new[] { "memberOf" });
            var list = new List<AdUserGroup>();
            // memberOf — многозначный: читаем всю коллекцию, а не первое значение.
            foreach (var m in de.Properties["memberOf"])
            {
                var dn = m?.ToString();
                if (string.IsNullOrEmpty(dn)) continue;
                list.Add(new AdUserGroup(dn, CnOf(dn)));
            }
            return list.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    private static string CnOf(string dn)
    {
        var first = dn.Split(',').FirstOrDefault()?.Trim() ?? dn;
        return first.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? first[3..] : first;
    }

    private static string ClassOf(SearchResult r)
    {
        if (!r.Properties.Contains("objectClass")) return "object";
        var classes = r.Properties["objectClass"].Cast<object>().Select(o => o!.ToString()!).ToList();
        foreach (var c in new[] { "computer", "organizationalUnit", "group", "contact", "user" })
            if (classes.Contains(c, StringComparer.OrdinalIgnoreCase)) return c;
        return classes.LastOrDefault() ?? "object";
    }

    private static string EscapeFilter(string value)
        => value.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");

    private static int OuCount(string dn)
        => dn.Split(',').Count(p => p.Trim().StartsWith("OU=", StringComparison.OrdinalIgnoreCase));

    private static string Str(SearchResult r, string p)
        => r.Properties.Contains(p) && r.Properties[p].Count > 0 ? r.Properties[p][0]!.ToString()! : "";
    private static string? StrOrNull(SearchResult r, string p)
        => r.Properties.Contains(p) && r.Properties[p].Count > 0 ? r.Properties[p][0]?.ToString() : null;
    private static int GetInt(SearchResult r, string p)
        => r.Properties.Contains(p) && r.Properties[p].Count > 0 ? Convert.ToInt32(r.Properties[p][0]) : 0;
    private static long GetLong(SearchResult r, string p)
        => r.Properties.Contains(p) && r.Properties[p].Count > 0 ? Convert.ToInt64(r.Properties[p][0]) : 0;
}
