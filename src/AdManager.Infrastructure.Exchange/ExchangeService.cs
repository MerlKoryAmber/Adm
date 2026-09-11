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
