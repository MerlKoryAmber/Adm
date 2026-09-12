using System.DirectoryServices;
using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Domain;

namespace AdManager.Infrastructure.Ad;

/// <summary>
/// AD-адаптер на System.DirectoryServices под operational identity.
/// AuthenticationTypes.Secure (sign+seal) позволяет SetPassword без LDAPS.
/// Все операции offload'ятся в пул (S.DS синхронный).
/// </summary>
public sealed class AdService : IAdService
{
    private const int UF_ACCOUNTDISABLE = 0x2;
    private const int UF_NORMAL_ACCOUNT = 0x200;

    private readonly AdConnectionOptions _opt;
    private readonly IOperationalCredentialProvider _cred;

    public AdService(AdConnectionOptions opt, IOperationalCredentialProvider cred)
    {
        _opt = opt;
        _cred = cred;
    }

    private DirectoryEntry Bind(string dn) => Ldap.Bind(_opt, _cred, dn);

    private Task<OperationResult> Do(Func<OperationResult> action, CancellationToken ct)
        => Task.Run(() =>
        {
            try { return action(); }
            catch (Exception ex) { return OperationResult.Fail((ex.InnerException ?? ex).Message); }
        }, ct);

    public Task<OperationResult> ResetPasswordAsync(string userDn, string newPassword, bool mustChangeAtNextLogon, CancellationToken ct = default)
        => Do(() =>
        {
            using var de = Bind(userDn);
            de.Invoke("SetPassword", new object[] { newPassword });
            if (mustChangeAtNextLogon)
            {
                de.Properties["pwdLastSet"].Value = 0;
                de.CommitChanges();
            }
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> SetAccountEnabledAsync(string userDn, bool enabled, CancellationToken ct = default)
        => Do(() =>
        {
            using var de = Bind(userDn);
            var uac = (int)(de.Properties["userAccountControl"].Value ?? UF_NORMAL_ACCOUNT);
            uac = enabled ? (uac & ~UF_ACCOUNTDISABLE) : (uac | UF_ACCOUNTDISABLE);
            de.Properties["userAccountControl"].Value = uac;
            de.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> UnlockAccountAsync(string userDn, CancellationToken ct = default)
        => Do(() =>
        {
            using var de = Bind(userDn);
            de.Properties["lockoutTime"].Value = 0;
            de.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> MoveObjectAsync(string dn, string targetOuDn, CancellationToken ct = default)
        => Do(() =>
        {
            using var de = Bind(dn);
            using var target = Bind(targetOuDn);
            de.MoveTo(target);
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
        => Do(() =>
        {
            using var parent = Bind(request.TargetOuDn);
            var u = parent.Children.Add($"CN={Escape(request.DisplayName)}", "user");
            u.Properties["sAMAccountName"].Value = request.SamAccountName;
            u.Properties["userPrincipalName"].Value = request.UserPrincipalName;
            u.Properties["displayName"].Value = request.DisplayName;
            if (request.Attributes is not null)
            {
                foreach (var (k, v) in request.Attributes)
                {
                    if (!string.IsNullOrEmpty(v)) u.Properties[k].Value = v;
                }
            }
            u.CommitChanges();

            if (!string.IsNullOrEmpty(request.InitialPassword))
            {
                u.Invoke("SetPassword", new object[] { request.InitialPassword });
                u.CommitChanges();
            }
            if (request.Enabled)
            {
                u.Properties["userAccountControl"].Value = UF_NORMAL_ACCOUNT; // enabled
                u.CommitChanges();
            }
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> DeleteObjectAsync(string dn, CancellationToken ct = default)
        => Do(() =>
        {
            using var de = Bind(dn);
            de.DeleteTree();
            de.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> SetAttributesAsync(string dn, IReadOnlyDictionary<string, string?> attributes, CancellationToken ct = default)
        => Do(() =>
        {
            using var de = Bind(dn);
            foreach (var (k, v) in attributes)
            {
                if (string.IsNullOrEmpty(v)) de.Properties[k].Clear();
                else de.Properties[k].Value = v;
            }
            de.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> ManageGroupMembershipAsync(string groupDn, IReadOnlyCollection<string> addMemberDns, IReadOnlyCollection<string> removeMemberDns, CancellationToken ct = default)
        => Do(() =>
        {
            using var g = Bind(groupDn);
            foreach (var dn in addMemberDns)
            {
                if (!g.Properties["member"].Contains(dn)) g.Properties["member"].Add(dn);
            }
            foreach (var dn in removeMemberDns)
            {
                if (g.Properties["member"].Contains(dn)) g.Properties["member"].Remove(dn);
            }
            g.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> RenameAsync(string dn, string newRdn, CancellationToken ct = default)
        => Do(() =>
        {
            using var de = Bind(dn);
            de.Rename(newRdn);
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> SetAccountOptionsAsync(string userDn, AccountOptions options, CancellationToken ct = default)
        => Do(() =>
        {
            const int DONT_EXPIRE_PASSWORD = 0x10000;
            const int PASSWD_CANT_CHANGE = 0x40;      // прим.: AD не применяет через UAC (реально — ACL); оставлено как есть
            const int ENCRYPTED_TEXT_PASSWORD_ALLOWED = 0x80;
            const int SMARTCARD_REQUIRED = 0x40000;
            const int NOT_DELEGATED = 0x100000;
            const int USE_DES_KEY_ONLY = 0x200000;
            const int DONT_REQUIRE_PREAUTH = 0x400000;
            using var de = Bind(userDn);
            var uac = (int)(de.Properties["userAccountControl"].Value ?? UF_NORMAL_ACCOUNT);
            static int Apply(int uacVal, bool? on, int bit) => on is bool v ? (v ? (uacVal | bit) : (uacVal & ~bit)) : uacVal;
            uac = Apply(uac, options.PasswordNeverExpires, DONT_EXPIRE_PASSWORD);
            uac = Apply(uac, options.CannotChangePassword, PASSWD_CANT_CHANGE);
            uac = Apply(uac, options.ReversibleEncryption, ENCRYPTED_TEXT_PASSWORD_ALLOWED);
            uac = Apply(uac, options.SmartcardRequired, SMARTCARD_REQUIRED);
            uac = Apply(uac, options.NotDelegated, NOT_DELEGATED);
            uac = Apply(uac, options.UseDesKeyOnly, USE_DES_KEY_ONLY);
            uac = Apply(uac, options.DontRequirePreauth, DONT_REQUIRE_PREAUTH);
            de.Properties["userAccountControl"].Value = uac;
            if (options.MustChangePasswordAtNextLogon is bool mc) de.Properties["pwdLastSet"].Value = mc ? 0 : -1;
            if (options.ClearAccountExpiry) de.Properties["accountExpires"].Value = 0;
            else if (options.AccountExpiresUtc is DateTime exp) de.Properties["accountExpires"].Value = exp.ToFileTimeUtc();
            de.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> SetPrimaryGroupAsync(string userDn, string groupDn, CancellationToken ct = default)
        => Do(() =>
        {
            using var group = Bind(groupDn);
            group.RefreshCache();
            if (group.Properties["objectSid"].Value is not byte[] sid || sid.Length < 4)
                return OperationResult.Fail("Cannot read group SID.");
            var rid = BitConverter.ToInt32(sid, sid.Length - 4); // RID = последний sub-authority (little-endian)
            using var user = Bind(userDn);
            user.Properties["primaryGroupID"].Value = rid; // требует, чтобы пользователь уже был членом группы
            user.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> SetLogonHoursAsync(string userDn, byte[]? mask, CancellationToken ct = default)
        => Do(() =>
        {
            using var de = Bind(userDn);
            if (mask is null || mask.Length == 0) de.Properties["logonHours"].Clear();          // нет атрибута = вход всегда разрешён
            else if (mask.Length != 21) return OperationResult.Fail("logonHours must be 21 bytes.");
            else de.Properties["logonHours"].Value = mask;
            de.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> CreateGroupAsync(CreateGroupRequest request, CancellationToken ct = default)
        => Do(() =>
        {
            using var parent = Bind(request.TargetOuDn);
            var g = parent.Children.Add($"CN={Escape(request.Name)}", "group");
            g.Properties["sAMAccountName"].Value = request.SamAccountName;
            int scope = request.Scope switch
            {
                GroupScope.DomainLocal => 0x4,
                GroupScope.Universal => 0x8,
                _ => 0x2, // Global
            };
            long groupType = scope | (request.Kind == GroupKind.Security ? 0x80000000L : 0L);
            g.Properties["groupType"].Value = unchecked((int)groupType);
            g.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> CreateComputerAsync(CreateComputerRequest request, CancellationToken ct = default)
        => Do(() =>
        {
            const int WORKSTATION_TRUST_ACCOUNT = 0x1000;
            using var parent = Bind(request.TargetOuDn);
            var c = parent.Children.Add($"CN={Escape(request.Name)}", "computer");
            c.Properties["sAMAccountName"].Value = request.Name.ToUpperInvariant() + "$";
            c.Properties["userAccountControl"].Value = WORKSTATION_TRUST_ACCOUNT;
            c.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> CreateContactAsync(CreateContactRequest request, CancellationToken ct = default)
        => Do(() =>
        {
            using var parent = Bind(request.TargetOuDn);
            var k = parent.Children.Add($"CN={Escape(request.Name)}", "contact");
            k.Properties["displayName"].Value = request.Name;
            if (request.Attributes is not null)
                foreach (var (a, v) in request.Attributes)
                    if (!string.IsNullOrEmpty(v)) k.Properties[a].Value = v;
            k.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> CreateOuAsync(CreateOuRequest request, CancellationToken ct = default)
        => Do(() =>
        {
            using var parent = Bind(request.ParentDn);
            var o = parent.Children.Add($"OU={Escape(request.Name)}", "organizationalUnit");
            o.CommitChanges();
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> ResetComputerAccountAsync(string computerDn, CancellationToken ct = default)
        => Do(() =>
        {
            using var de = Bind(computerDn);
            var sam = de.Properties["sAMAccountName"].Value?.ToString() ?? "";
            var name = sam.TrimEnd('$');
            // Сброс машинного пароля к значению по умолчанию (имя компьютера в нижнем регистре) — как dsmod/ADManager.
            de.Invoke("SetPassword", new object[] { name.ToLowerInvariant() });
            return OperationResult.Ok();
        }, ct);

    // Экранирование запятой/спецсимволов в RDN-значении.
    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace(",", "\\,").Replace("+", "\\+")
                .Replace("\"", "\\\"").Replace("<", "\\<").Replace(">", "\\>").Replace(";", "\\;");
}
