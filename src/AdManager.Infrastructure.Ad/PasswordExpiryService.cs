using System.DirectoryServices;
using AdManager.Application;
using AdManager.Application.Abstractions;

namespace AdManager.Infrastructure.Ad;

/// <summary>Вычисляет пользователей с приближающимся истечением пароля (msDS-UserPasswordExpiryTimeComputed).</summary>
public sealed class PasswordExpiryService : IPasswordExpiryService
{
    private const long NeverExpires = 0x7FFFFFFFFFFFFFFF;
    private const string ExpiryAttr = "msDS-UserPasswordExpiryTimeComputed";

    private readonly AdConnectionOptions _opt;
    private readonly IOperationalCredentialProvider _cred;

    public PasswordExpiryService(AdConnectionOptions opt, IOperationalCredentialProvider cred)
    {
        _opt = opt;
        _cred = cred;
    }

    private DirectoryEntry Bind(string dn) => Ldap.Bind(_opt, _cred, dn);

    private string BaseDn()
    {
        if (!string.IsNullOrWhiteSpace(_opt.BaseDn)) return _opt.BaseDn!;
        using var root = Bind("RootDSE");
        return root.Properties["defaultNamingContext"][0]?.ToString() ?? "";
    }

    public Task<IReadOnlyList<ExpiringUser>> ListExpiringAsync(int withinDays, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<ExpiringUser>>(() =>
        {
            using var root = Bind(BaseDn());
            // включённые, пароль истекает (не PNE 0x10000, не disabled 0x2)
            using var s = new DirectorySearcher(root)
            {
                Filter = "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(!(userAccountControl:1.2.840.113556.1.4.803:=65536)))",
                SearchScope = SearchScope.Subtree,
                PageSize = 1000,
            };
            foreach (var p in new[] { "distinguishedName", "displayName", "mail", ExpiryAttr })
                s.PropertiesToLoad.Add(p);

            var now = DateTime.UtcNow;
            var list = new List<ExpiringUser>();
            using var results = s.FindAll();
            foreach (SearchResult r in results)
            {
                if (r.Properties[ExpiryAttr].Count == 0) continue;
                long ft;
                try { ft = Convert.ToInt64(r.Properties[ExpiryAttr][0]); }
                catch { continue; }
                if (ft <= 0 || ft == NeverExpires) continue; // 0 = сменить при входе; max = не истекает

                DateTime expires;
                try { expires = DateTime.FromFileTimeUtc(ft); }
                catch { continue; }

                var daysLeft = (int)Math.Floor((expires - now).TotalDays);
                if (daysLeft > withinDays) continue;

                list.Add(new ExpiringUser(
                    Str(r, "distinguishedName"),
                    StrOr(r, "displayName", Str(r, "distinguishedName")),
                    StrOrNull(r, "mail"),
                    expires,
                    daysLeft));
            }
            return list.OrderBy(u => u.DaysLeft).ToList();
        }, ct);

    private static string Str(SearchResult r, string p) => r.Properties[p].Count > 0 ? r.Properties[p][0]?.ToString() ?? "" : "";
    private static string? StrOrNull(SearchResult r, string p) { var v = Str(r, p); return string.IsNullOrEmpty(v) ? null : v; }
    private static string StrOr(SearchResult r, string p, string fallback) { var v = Str(r, p); return string.IsNullOrEmpty(v) ? fallback : v; }
}
