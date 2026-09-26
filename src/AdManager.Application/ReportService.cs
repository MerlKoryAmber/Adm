using AdManager.Application.Abstractions;

namespace AdManager.Application;

/// <summary>
/// Движок отчётов (эталон ADManager Plus). Каталог + выполнение по ключу.
/// Опирается на IAdDirectory; отчёты, требующие per-object атрибутов, дозагружают их
/// через GetObjectAsync. Один RunAsync — и для UI, и для автоматизаций.
/// </summary>
public sealed class ReportService : IReportService
{
    private const int UF_ACCOUNTDISABLE = 0x2;
    private const int UF_PASSWORD_NEVER_EXPIRES = 0x10000;

    private readonly IAdDirectory _dir;
    private readonly UiBaseDn _base;

    public ReportService(IAdDirectory dir, UiBaseDn baseDn)
    {
        _dir = dir;
        _base = baseDn;
    }

    public IReadOnlyList<ReportDef> Catalog => _catalog;

    public ReportDef? Find(string key) => _catalog.FirstOrDefault(r => r.Key == key);

    private static readonly ReportDef[] _catalog =
    {
        // ---- User Reports ----
        new("user.all",            "User", "General",        "All Users",                "Every user account in the domain subtree.", ReportObjectType.User),
        new("user.empty-attrs",    "User", "General",        "Users With Empty Attributes","Users missing a display name or email.", ReportObjectType.User),
        new("user.no-manager",     "User", "General",        "Users Without Managers",    "Accounts with an empty manager attribute.", ReportObjectType.User),
        new("user.recently-created","User","General",        "Recently Created Users",    "Accounts created in the last 30 days.", ReportObjectType.User),
        new("user.recently-modified","User","General",       "Recently Modified Users",   "Accounts modified in the last 30 days.", ReportObjectType.User),
        new("user.disabled",       "User", "Account Status", "Disabled Users",            "Accounts where the account-disabled flag is set.", ReportObjectType.User),
        new("user.locked",         "User", "Account Status", "Locked-out Users",          "Accounts currently locked out.", ReportObjectType.User),
        new("user.expired",        "User", "Account Status", "Account Expired Users",     "Accounts past their accountExpires date.", ReportObjectType.User),
        new("user.never-expires",  "User", "Account Status", "Account Never Expires Users","Accounts with no expiry set.", ReportObjectType.User),

        // ---- Password Reports ----
        new("pwd.never-expires",   "Password", "General", "Password Never Expires",   "Accounts with PASSWORD_NEVER_EXPIRES flag.", ReportObjectType.User),
        new("pwd.soon-expire",     "Password", "General", "Soon-To-Expire Passwords", "Passwords expiring within 7 days.", ReportObjectType.User),
        new("pwd.expired",         "Password", "General", "Password Expired Users",   "Accounts whose password has expired.", ReportObjectType.User),
        new("pwd.recently-changed","Password", "General", "Recently Changed Passwords","Passwords changed in the last 7 days.", ReportObjectType.User),

        // ---- Group Reports ----
        new("group.all",          "Group", "General", "All Groups",          "Every group in the domain subtree.", ReportObjectType.Group),
        new("group.empty",        "Group", "General", "Empty Groups",        "Groups with no members.", ReportObjectType.Group),

        // ---- Contact & OU Reports ----
        new("contact.all",        "Contact & OU", "Contacts", "All Contacts", "Every contact object.", ReportObjectType.Contact),
        new("ou.all",             "Contact & OU", "OUs",      "All OUs",       "Every organizational unit.", ReportObjectType.Ou),
        new("ou.empty",           "Contact & OU", "OUs",      "Empty OUs",     "OUs with no child objects.", ReportObjectType.Ou),
    };

    public async Task<ReportResult> RunAsync(string key, CancellationToken ct = default)
    {
        var def = Find(key) ?? _catalog[0];
        return def.ObjectType switch
        {
            ReportObjectType.User    => await RunUserAsync(def, ct),
            ReportObjectType.Group   => await RunGroupAsync(def, ct),
            ReportObjectType.Contact => await RunContactAsync(ct),
            ReportObjectType.Ou      => await RunOuAsync(def, ct),
            _ => new ReportResult(Array.Empty<string>(), Array.Empty<ReportRow>()),
        };
    }

    // ---------------- Users / Password ----------------
    private static readonly string[] UserColumns = { "Display name", "Account", "UPN", "Email", "Status" };

    private async Task<ReportResult> RunUserAsync(ReportDef def, CancellationToken ct)
    {
        var users = await _dir.ListUsersAsync(_base.Value, subtree: true, ct);
        // отчёты, которым нужны per-object атрибуты
        var needsAttrs = def.Key is "user.no-manager" or "user.recently-created" or "user.recently-modified"
            or "user.expired" or "user.never-expires" or "pwd.never-expires" or "pwd.soon-expire"
            or "pwd.expired" or "pwd.recently-changed";

        var attrs = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        if (needsAttrs)
        {
            foreach (var u in users)
            {
                var det = await _dir.GetObjectAsync(u.Dn, new[]
                {
                    "manager", "userAccountControl", "whenCreated", "whenChanged",
                    "accountExpires", "pwdLastSet", "msDS-UserPasswordExpiryTimeComputed",
                }, ct);
                if (det != null) attrs[u.Dn] = det.Attributes;
            }
        }

        var now = DateTime.UtcNow;
        IEnumerable<AdUserSummary> rows = def.Key switch
        {
            "user.disabled"        => users.Where(u => !u.Enabled),
            "user.locked"          => users.Where(u => u.LockedOut),
            "user.empty-attrs"     => users.Where(u => string.IsNullOrWhiteSpace(u.DisplayName) || string.IsNullOrWhiteSpace(u.Mail)),
            "user.no-manager"      => users.Where(u => string.IsNullOrWhiteSpace(Attr(attrs, u.Dn, "manager"))),
            "user.recently-created"=> users.Where(u => WithinDays(Attr(attrs, u.Dn, "whenCreated"), 30, now)),
            "user.recently-modified"=> users.Where(u => WithinDays(Attr(attrs, u.Dn, "whenChanged"), 30, now)),
            "user.expired"         => users.Where(u => IsExpired(Attr(attrs, u.Dn, "accountExpires"), now)),
            "user.never-expires"   => users.Where(u => IsNeverExpires(Attr(attrs, u.Dn, "accountExpires"))),
            "pwd.never-expires"    => users.Where(u => (Uac(attrs, u.Dn) & UF_PASSWORD_NEVER_EXPIRES) != 0),
            "pwd.soon-expire"      => users.Where(u => PwdExpiresWithin(Attr(attrs, u.Dn, "msDS-UserPasswordExpiryTimeComputed"), 7, now)),
            "pwd.expired"          => users.Where(u => PwdExpired(Attr(attrs, u.Dn, "msDS-UserPasswordExpiryTimeComputed"), now)),
            "pwd.recently-changed" => users.Where(u => WithinDaysFileTime(Attr(attrs, u.Dn, "pwdLastSet"), 7, now)),
            _                      => users,
        };

        var list = rows.Select(u => new ReportRow(u.Dn, new[]
        {
            u.DisplayName ?? "",
            u.SamAccountName ?? "",
            u.Upn ?? "",
            u.Mail ?? "",
            (u.Enabled ? "Enabled" : "Disabled") + (u.LockedOut ? " / Locked" : ""),
        })).ToList();
        return new ReportResult(UserColumns, list);
    }

    // ---------------- Groups ----------------
    private async Task<ReportResult> RunGroupAsync(ReportDef def, CancellationToken ct)
    {
        var groups = await _dir.ListGroupsAsync(_base.Value, subtree: true, ct);
        var cols = new[] { "Name", "Account", "Members" };
        var rows = new List<ReportRow>();
        foreach (var g in groups)
        {
            int? memberCount = null;
            if (def.Key == "group.empty")
            {
                var members = await _dir.ListGroupMembersAsync(g.Dn, ct);
                memberCount = members.Count;
                if (memberCount != 0) continue; // только пустые
            }
            rows.Add(new ReportRow(g.Dn, new[] { g.Name ?? "", g.SamAccountName ?? "", memberCount?.ToString() ?? "" }));
        }
        return new ReportResult(cols, rows);
    }

    // ---------------- Contacts ----------------
    private async Task<ReportResult> RunContactAsync(CancellationToken ct)
    {
        var contacts = await _dir.ListContactsAsync(_base.Value, subtree: true, ct);
        var cols = new[] { "Name", "Email" };
        var rows = contacts.Select(c => new ReportRow(c.Dn, new[] { c.Name ?? "", c.Mail ?? "" })).ToList();
        return new ReportResult(cols, rows);
    }

    // ---------------- OUs ----------------
    private async Task<ReportResult> RunOuAsync(ReportDef def, CancellationToken ct)
    {
        var ous = await _dir.ListAllOusAsync(_base.Value, ct);
        var cols = new[] { "Name", "Depth" };
        var rows = new List<ReportRow>();
        foreach (var o in ous)
        {
            if (def.Key == "ou.empty")
            {
                var users = await _dir.ListUsersAsync(o.Dn, subtree: false, ct);
                var groups = await _dir.ListGroupsAsync(o.Dn, subtree: false, ct);
                if (users.Count != 0 || groups.Count != 0) continue;
            }
            rows.Add(new ReportRow(o.Dn, new[] { o.Name ?? "", o.Depth.ToString() }));
        }
        return new ReportResult(cols, rows);
    }

    // ---------------- helpers ----------------
    private static string? Attr(Dictionary<string, IReadOnlyDictionary<string, string?>> a, string dn, string key)
        => a.TryGetValue(dn, out var m) ? m.GetValueOrDefault(key) : null;

    private static int Uac(Dictionary<string, IReadOnlyDictionary<string, string?>> a, string dn)
        => int.TryParse(Attr(a, dn, "userAccountControl"), out var v) ? v : 0;

    private static bool WithinDays(string? generalizedTime, int days, DateTime now)
    {
        // whenCreated/whenChanged: "yyyyMMddHHmmss.0Z"
        if (string.IsNullOrEmpty(generalizedTime) || generalizedTime.Length < 14) return false;
        if (!DateTime.TryParseExact(generalizedTime.Substring(0, 14), "yyyyMMddHHmmss",
            null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
            return false;
        return (now - dt).TotalDays <= days;
    }

    private static bool WithinDaysFileTime(string? fileTime, int days, DateTime now)
    {
        var dt = FromFileTime(fileTime);
        return dt.HasValue && (now - dt.Value).TotalDays <= days;
    }

    private static bool IsExpired(string? accountExpires, DateTime now)
    {
        var dt = FromFileTime(accountExpires);
        return dt.HasValue && dt.Value < now; // задан и в прошлом
    }

    private static bool IsNeverExpires(string? accountExpires)
        => accountExpires == "0" || accountExpires == "9223372036854775807" || string.IsNullOrEmpty(accountExpires);

    private static bool PwdExpiresWithin(string? computed, int days, DateTime now)
    {
        var dt = FromFileTime(computed);
        if (!dt.HasValue) return false;
        var left = (dt.Value - now).TotalDays;
        return left >= 0 && left <= days;
    }

    private static bool PwdExpired(string? computed, DateTime now)
    {
        var dt = FromFileTime(computed);
        return dt.HasValue && dt.Value < now;
    }

    /// <summary>AD FILETIME (100-нс с 1601) → UTC. 0 и Int64.Max («never») → null.</summary>
    private static DateTime? FromFileTime(string? value)
    {
        if (!long.TryParse(value, out var ft) || ft <= 0 || ft == long.MaxValue) return null;
        try { return DateTime.FromFileTimeUtc(ft); } catch { return null; }
    }
}

/// <summary>Обёртка над base DN домена (чтобы Application не зависел от Web UiOptions).</summary>
public sealed class UiBaseDn
{
    public string Value { get; }
    public UiBaseDn(string value) => Value = value;
}
