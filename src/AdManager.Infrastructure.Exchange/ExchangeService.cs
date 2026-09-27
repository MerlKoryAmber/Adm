using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security;
using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Domain;

namespace AdManager.Infrastructure.Exchange;

/// <summary>
/// Exchange 2019 через remote PowerShell (WSMan/Kerberos, конфигурация Microsoft.Exchange).
/// Под operational identity. Реализовано по докам Microsoft/ADManager; НЕ верифицировано на живом
/// сервере (в лабе Exchange не поднят) — см. docs/handoff/TODO.md.
/// Пул runspace-ов — TODO; пока runspace на вызов.
/// </summary>
public sealed class ExchangeService : IExchangeService
{
    private const string ExchangeShellUri = "http://schemas.microsoft.com/powershell/Microsoft.Exchange";

    private readonly ExchangeOptions _opt;
    private readonly IOperationalCredentialProvider _cred;

    public ExchangeService(ExchangeOptions opt, IOperationalCredentialProvider cred)
    {
        _opt = opt;
        _cred = cred;
    }

    public Task<OperationResult> EnableMailboxAsync(string userDn, string? alias, CancellationToken ct = default)
        => Run(ps =>
        {
            ps.AddCommand("Enable-Mailbox").AddParameter("Identity", userDn);
            if (!string.IsNullOrEmpty(alias)) ps.AddParameter("Alias", alias);
        }, ct);

    public Task<OperationResult> EnableMailboxAsync(string userDn, MailboxProvisioning p, CancellationToken ct = default)
        => Task.Run(async () =>
        {
            var enable = await Run(ps =>
            {
                ps.AddCommand("Enable-Mailbox").AddParameter("Identity", userDn);
                if (!string.IsNullOrEmpty(p.Alias)) ps.AddParameter("Alias", p.Alias);
                if (!string.IsNullOrEmpty(p.Database)) ps.AddParameter("Database", p.Database);
            }, ct);
            if (!enable.Success) return enable;
            if (p.EnableArchive)
            {
                var arch = await Run(ps =>
                {
                    ps.AddCommand("Enable-Mailbox").AddParameter("Identity", userDn).AddParameter("Archive", true);
                    if (!string.IsNullOrEmpty(p.ArchiveDatabase)) ps.AddParameter("ArchiveDatabase", p.ArchiveDatabase);
                }, ct);
                if (!arch.Success) return OperationResult.Fail("Mailbox created, but archive failed: " + arch.Message);
            }
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> DisableMailboxAsync(string userDn, CancellationToken ct = default)
        => Run(ps => ps.AddCommand("Disable-Mailbox").AddParameter("Identity", userDn).AddParameter("Confirm", false), ct);

    public Task<OperationResult> SetMailboxPropertiesAsync(string identity, MailboxProperties properties, CancellationToken ct = default)
        => Run(ps =>
        {
            ps.AddCommand("Set-Mailbox").AddParameter("Identity", identity);
            if (!string.IsNullOrEmpty(properties.Alias)) ps.AddParameter("Alias", properties.Alias);
            if (properties.IssueWarningQuotaMb is { } warn) ps.AddParameter("IssueWarningQuota", $"{warn}MB");
            if (properties.ProhibitSendQuotaMb is { } send) ps.AddParameter("ProhibitSendQuota", $"{send}MB");
            if (properties.HiddenFromAddressLists is { } hidden) ps.AddParameter("HiddenFromAddressListsEnabled", hidden);
        }, ct);

    public Task<OperationResult> CreateDistributionGroupAsync(string name, string targetOuDn, CancellationToken ct = default)
        => Run(ps =>
        {
            ps.AddCommand("New-DistributionGroup").AddParameter("Name", name);
            if (!string.IsNullOrEmpty(targetOuDn)) ps.AddParameter("OrganizationalUnit", targetOuDn);
        }, ct);

    public Task<OperationResult> ManageDistributionMembersAsync(string groupIdentity, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken ct = default)
        => Task.Run(async () =>
        {
            foreach (var m in add)
            {
                var r = await Run(ps => ps.AddCommand("Add-DistributionGroupMember").AddParameter("Identity", groupIdentity).AddParameter("Member", m), ct);
                if (!r.Success) return r;
            }
            foreach (var m in remove)
            {
                var r = await Run(ps => ps.AddCommand("Remove-DistributionGroupMember").AddParameter("Identity", groupIdentity).AddParameter("Member", m).AddParameter("Confirm", false), ct);
                if (!r.Success) return r;
            }
            return OperationResult.Ok();
        }, ct);

    public Task<OperationResult> AddMailboxPermissionAsync(string identity, string trustee, CancellationToken ct = default)
        => Run(ps => ps.AddCommand("Add-MailboxPermission")
            .AddParameter("Identity", identity)
            .AddParameter("User", trustee)
            .AddParameter("AccessRights", "FullAccess")
            .AddParameter("AutoMapping", true), ct);

    public Task<OperationResult> RemoveMailboxPermissionAsync(string identity, string trustee, CancellationToken ct = default)
        => Run(ps => ps.AddCommand("Remove-MailboxPermission")
            .AddParameter("Identity", identity)
            .AddParameter("User", trustee)
            .AddParameter("AccessRights", "FullAccess")
            .AddParameter("Confirm", false), ct);

    public Task<OperationResult> AddSendAsAsync(string identity, string trustee, CancellationToken ct = default)
        => Run(ps => ps.AddCommand("Add-ADPermission")
            .AddParameter("Identity", identity)
            .AddParameter("User", trustee)
            .AddParameter("ExtendedRights", "Send As"), ct);

    public Task<OperationResult> RemoveSendAsAsync(string identity, string trustee, CancellationToken ct = default)
        => Run(ps => ps.AddCommand("Remove-ADPermission")
            .AddParameter("Identity", identity)
            .AddParameter("User", trustee)
            .AddParameter("ExtendedRights", "Send As")
            .AddParameter("Confirm", false), ct);

    public Task<OperationResult> SetSendOnBehalfAsync(string identity, string trustee, bool add, CancellationToken ct = default)
        => Run(ps =>
        {
            var delta = new Hashtable { { add ? "Add" : "Remove", trustee } };
            ps.AddCommand("Set-Mailbox")
              .AddParameter("Identity", identity)
              .AddParameter("GrantSendOnBehalfTo", delta);
        }, ct);

    // ---------- расширенные mailbox-операции ----------

    public Task<IReadOnlyList<MailboxDatabase>> ListDatabasesAsync(CancellationToken ct = default)
        => RunQuery(ps => ps.AddCommand("Get-MailboxDatabase").AddParameter("Status", true),
            o => new MailboxDatabase(
                Name: Prop(o, "Name") ?? "",
                Server: Prop(o, "Server"),
                Guid: Prop(o, "Guid")),
            ct);

    public async Task<MailboxInfo> GetMailboxInfoAsync(string identity, CancellationToken ct = default)
    {
        var mbxs = await RunQuery(ps => ps.AddCommand("Get-Mailbox").AddParameter("Identity", identity), o => o, ct);
        var mbx = mbxs.FirstOrDefault();
        if (mbx is null) return new MailboxInfo(false);

        var addresses = PropList(mbx, "EmailAddresses");
        var primary = Prop(mbx, "PrimarySmtpAddress");
        bool hidden = string.Equals(Prop(mbx, "HiddenFromAddressListsEnabled"), "True", StringComparison.OrdinalIgnoreCase);
        var fwd = Prop(mbx, "ForwardingSmtpAddress");
        if (string.IsNullOrWhiteSpace(fwd)) fwd = Prop(mbx, "ForwardingAddress");
        bool deliverAndForward = string.Equals(Prop(mbx, "DeliverToMailboxAndForward"), "True", StringComparison.OrdinalIgnoreCase);
        bool archive = !string.IsNullOrWhiteSpace(Prop(mbx, "ArchiveDatabase")) || !string.IsNullOrWhiteSpace(Prop(mbx, "ArchiveGuid")) && Prop(mbx, "ArchiveGuid") != "00000000-0000-0000-0000-000000000000";
        var db = Prop(mbx, "Database");
        var sendOnBehalf = PropList(mbx, "GrantSendOnBehalfTo");

        var fa = await RunQuery(ps => ps.AddCommand("Get-MailboxPermission").AddParameter("Identity", identity),
            o => new { User = Prop(o, "User"), Rights = Prop(o, "AccessRights"), Deny = Prop(o, "Deny") }, ct);
        var fullAccess = fa.Where(x => (x.Rights ?? "").Contains("FullAccess") && !string.Equals(x.Deny, "True", StringComparison.OrdinalIgnoreCase)
                                       && !(x.User ?? "").StartsWith("NT AUTHORITY", StringComparison.OrdinalIgnoreCase)
                                       && !(x.User ?? "").Contains("SELF", StringComparison.OrdinalIgnoreCase))
                            .Select(x => x.User!).Distinct().ToList();

        var sa = await RunQuery(ps => ps.AddCommand("Get-ADPermission").AddParameter("Identity", identity),
            o => new { User = Prop(o, "User"), Rights = Prop(o, "ExtendedRights") }, ct);
        var sendAs = sa.Where(x => (x.Rights ?? "").Contains("Send-As", StringComparison.OrdinalIgnoreCase) || (x.Rights ?? "").Contains("Send As", StringComparison.OrdinalIgnoreCase))
                       .Select(x => x.User!).Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("NT AUTHORITY", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();

        return new MailboxInfo(
            HasMailbox: true,
            PrimarySmtp: primary,
            EmailAddresses: addresses,
            HiddenFromAddressLists: hidden,
            ForwardingAddress: fwd,
            DeliverToMailboxAndForward: deliverAndForward,
            FullAccess: fullAccess,
            SendAs: sendAs,
            SendOnBehalf: sendOnBehalf,
            ArchiveEnabled: archive,
            Database: db);
    }

    public Task<OperationResult> SetEmailAddressesAsync(string identity, IReadOnlyList<string> addresses, string? primarySmtp, CancellationToken ct = default)
        => Run(ps =>
        {
            // proxyAddresses: Primary — "SMTP:", остальные — "smtp:".
            var list = new System.Collections.Generic.List<string>();
            foreach (var a in addresses)
            {
                var addr = a.Trim();
                if (string.IsNullOrEmpty(addr)) continue;
                var bare = addr.Contains(':') ? addr[(addr.IndexOf(':') + 1)..] : addr;
                var isPrimary = !string.IsNullOrEmpty(primarySmtp) && string.Equals(bare, primarySmtp, StringComparison.OrdinalIgnoreCase);
                list.Add((isPrimary ? "SMTP:" : "smtp:") + bare);
            }
            ps.AddCommand("Set-Mailbox").AddParameter("Identity", identity).AddParameter("EmailAddresses", list.ToArray());
        }, ct);

    public Task<OperationResult> SetForwardingAsync(string identity, string? forwardingSmtp, bool deliverToMailboxAndForward, CancellationToken ct = default)
        => Run(ps =>
        {
            ps.AddCommand("Set-Mailbox").AddParameter("Identity", identity);
            if (string.IsNullOrWhiteSpace(forwardingSmtp))
            {
                ps.AddParameter("ForwardingSmtpAddress", null);
                ps.AddParameter("ForwardingAddress", null);
                ps.AddParameter("DeliverToMailboxAndForward", false);
            }
            else
            {
                ps.AddParameter("ForwardingSmtpAddress", forwardingSmtp.Trim());
                ps.AddParameter("DeliverToMailboxAndForward", deliverToMailboxAndForward);
            }
        }, ct);

    public Task<OperationResult> SetHiddenFromAddressListsAsync(string identity, bool hidden, CancellationToken ct = default)
        => Run(ps => ps.AddCommand("Set-Mailbox").AddParameter("Identity", identity).AddParameter("HiddenFromAddressListsEnabled", hidden), ct);

    public Task<OperationResult> SetArchiveAsync(string identity, bool enabled, string? archiveDatabase, CancellationToken ct = default)
        => Run(ps =>
        {
            if (enabled)
            {
                ps.AddCommand("Enable-Mailbox").AddParameter("Identity", identity).AddParameter("Archive", true);
                if (!string.IsNullOrEmpty(archiveDatabase)) ps.AddParameter("ArchiveDatabase", archiveDatabase);
            }
            else
            {
                ps.AddCommand("Disable-Mailbox").AddParameter("Identity", identity).AddParameter("Archive", true).AddParameter("Confirm", false);
            }
        }, ct);

    public Task<IReadOnlyList<MobileDevice>> ListMobileDevicesAsync(string identity, CancellationToken ct = default)
        => RunQuery(ps => ps.AddCommand("Get-MobileDevice").AddParameter("Mailbox", identity),
            o => new MobileDevice(
                Identity: Prop(o, "Identity") ?? Prop(o, "Guid") ?? "",
                DeviceModel: Prop(o, "DeviceModel"),
                DeviceOs: Prop(o, "DeviceOS"),
                DeviceType: Prop(o, "DeviceType"),
                FirstSyncUtc: Prop(o, "FirstSyncTime"),
                LastSyncUtc: Prop(o, "WhenChanged")),
            ct);

    public Task<OperationResult> WipeMobileDeviceAsync(string deviceIdentity, CancellationToken ct = default)
        => Run(ps => ps.AddCommand("Clear-MobileDevice").AddParameter("Identity", deviceIdentity).AddParameter("Confirm", false), ct);

    public Task<OperationResult> RemoveMobileDeviceAsync(string deviceIdentity, CancellationToken ct = default)
        => Run(ps => ps.AddCommand("Remove-MobileDevice").AddParameter("Identity", deviceIdentity).AddParameter("Confirm", false), ct);

    private static string? Prop(PSObject o, string name)
    {
        var v = o?.Properties[name]?.Value;
        return v?.ToString();
    }

    private static IReadOnlyList<string> PropList(PSObject o, string name)
    {
        var v = o?.Properties[name]?.Value;
        if (v is null) return Array.Empty<string>();
        if (v is IEnumerable en and not string)
            return en.Cast<object?>().Where(x => x is not null).Select(x => x!.ToString()!).ToList();
        return new[] { v.ToString()! };
    }

    /// <summary>Выполнить команду и спроецировать результаты. При ошибке/недоступности — пустой список.</summary>
    private Task<IReadOnlyList<T>> RunQuery<T>(Action<PowerShell> build, Func<PSObject, T> map, CancellationToken ct)
        => Task.Run<IReadOnlyList<T>>(() =>
        {
            if (!_opt.IsConfigured) return Array.Empty<T>();
            try
            {
                using var runspace = OpenRunspace();
                using var ps = PowerShell.Create();
                ps.Runspace = runspace;
                build(ps);
                var results = ps.Invoke();
                if (ps.HadErrors && results.Count == 0) return Array.Empty<T>();
                return results.Where(r => r is not null).Select(map).ToList();
            }
            catch { return Array.Empty<T>(); }
        }, ct);

    private Task<OperationResult> Run(Action<PowerShell> build, CancellationToken ct)
        => Task.Run(() =>
        {
            if (!_opt.IsConfigured)
                return OperationResult.Fail("Exchange is not configured (set Exchange:ConnectionUri).");
            try
            {
                using var runspace = OpenRunspace();
                using var ps = PowerShell.Create();
                ps.Runspace = runspace;
                build(ps);
                ps.Invoke();
                if (ps.HadErrors)
                {
                    var err = string.Join("; ", ps.Streams.Error.Select(e => e.ToString()));
                    return OperationResult.Fail(string.IsNullOrEmpty(err) ? "Exchange command failed" : err);
                }
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                return OperationResult.Fail((ex.InnerException ?? ex).Message);
            }
        }, ct);

    private Runspace OpenRunspace()
    {
        var connection = new WSManConnectionInfo(new Uri(_opt.ConnectionUri), ExchangeShellUri, GetPsCredential())
        {
            AuthenticationMechanism = ParseAuth(_opt.Authentication),
            MaximumConnectionRedirectionCount = 4,
        };
        var runspace = RunspaceFactory.CreateRunspace(connection);
        runspace.Open();
        return runspace;
    }

    private PSCredential? GetPsCredential()
    {
        var id = _cred.GetIdentity(_opt.ServerFqdn);
        if (id.Credential is null) return null; // gMSA/процесс
        var secure = new SecureString();
        foreach (var c in id.Credential.Password ?? "") secure.AppendChar(c);
        secure.MakeReadOnly();
        return new PSCredential(id.Credential.UserName, secure);
    }

    private static AuthenticationMechanism ParseAuth(string auth) => auth?.ToLowerInvariant() switch
    {
        "negotiate" => AuthenticationMechanism.Negotiate,
        "basic" => AuthenticationMechanism.Basic,
        "credssp" => AuthenticationMechanism.Credssp,
        _ => AuthenticationMechanism.Kerberos,
    };
}
