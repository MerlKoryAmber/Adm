using AdManager.Application.Abstractions;
using AdManager.Domain;
using AdManager.Domain.Enums;

namespace AdManager.Application;

/// <summary>
/// Делегированный конвейер операций Exchange: RBAC → аудит(намерение) → операция → аудит(результат).
/// Операции выполняет operational identity через remote PowerShell (IExchangeService).
/// </summary>
public sealed class ExchangeManagementService
{
    private readonly IRbacEngine _rbac;
    private readonly IExchangeService _ex;
    private readonly IAuditLog _audit;

    public ExchangeManagementService(IRbacEngine rbac, IExchangeService ex, IAuditLog audit)
    {
        _rbac = rbac;
        _ex = ex;
        _audit = audit;
    }

    public Task<OperationResult> EnableMailboxAsync(TechnicianContext actor, string userDn, string? alias, CancellationToken ct = default)
        => Run(actor, Permission.EnableMailbox, userDn, "attempt: enable mailbox", () => _ex.EnableMailboxAsync(userDn, alias, ct), ct);

    public Task<OperationResult> DisableMailboxAsync(TechnicianContext actor, string userDn, CancellationToken ct = default)
        => Run(actor, Permission.DisableMailbox, userDn, "attempt: disable mailbox", () => _ex.DisableMailboxAsync(userDn, ct), ct);

    public Task<OperationResult> SetMailboxPropertiesAsync(TechnicianContext actor, string identity, MailboxProperties props, CancellationToken ct = default)
        => Run(actor, Permission.SetMailboxProperties, identity, "attempt: set mailbox properties", () => _ex.SetMailboxPropertiesAsync(identity, props, ct), ct);

    public Task<OperationResult> CreateDistributionGroupAsync(TechnicianContext actor, string name, string targetOuDn, CancellationToken ct = default)
        => Run(actor, Permission.ManageDistribution, targetOuDn, $"attempt: create distribution group {name}", () => _ex.CreateDistributionGroupAsync(name, targetOuDn, ct), ct);

    public Task<OperationResult> ManageDistributionMembersAsync(TechnicianContext actor, string groupIdentity, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken ct = default)
        => Run(actor, Permission.ManageDistribution, groupIdentity, "attempt: distribution membership", () => _ex.ManageDistributionMembersAsync(groupIdentity, add, remove, ct), ct);

    private async Task<OperationResult> Run(TechnicianContext actor, Permission perm, string targetDn, string attemptMsg, Func<Task<OperationResult>> op, CancellationToken ct)
    {
        var decision = await _rbac.AuthorizeAsync(actor, perm, targetDn, ct);
        if (!decision.Allowed)
        {
            var reason = decision.Reason ?? "not authorized";
            await _audit.WriteAsync(Entry(actor, perm, targetDn, AuditPhase.Result, false, reason), ct);
            return OperationResult.Fail(reason);
        }

        await _audit.WriteAsync(Entry(actor, perm, targetDn, AuditPhase.Attempt, false, attemptMsg), ct);
        var result = await op();
        await _audit.WriteAsync(Entry(actor, perm, targetDn, AuditPhase.Result, result.Success, result.Message), ct);
        return result;
    }

    private static AuditEntry Entry(TechnicianContext a, Permission op, string dn, AuditPhase phase, bool ok, string? msg) => new()
    {
        Phase = phase,
        TimestampMsk = MskTime.Now,
        ActorSid = a.Sid,
        ActorName = a.DisplayName,
        Operation = op,
        TargetDn = dn,
        Success = ok,
        Message = msg,
    };
}
