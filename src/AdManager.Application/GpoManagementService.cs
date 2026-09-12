using AdManager.Application.Abstractions;
using AdManager.Domain;
using AdManager.Domain.Enums;

namespace AdManager.Application;

/// <summary>
/// Делегированный конвейер управления линками GPO: RBAC → аудит(намерение) → операция → аудит(результат).
/// Target для RBAC/аудита — scope (OU/домен), к которому привязывается политика.
/// </summary>
public sealed class GpoManagementService
{
    private readonly IRbacEngine _rbac;
    private readonly IGpoService _gpo;
    private readonly IAuditLog _audit;

    public GpoManagementService(IRbacEngine rbac, IGpoService gpo, IAuditLog audit)
    {
        _rbac = rbac;
        _gpo = gpo;
        _audit = audit;
    }

    public Task<OperationResult> LinkAsync(TechnicianContext actor, string scopeDn, string gpoId, CancellationToken ct = default)
        => Run(actor, scopeDn, $"attempt: link GPO {gpoId}", () => _gpo.LinkAsync(scopeDn, gpoId, ct), ct);

    public Task<OperationResult> UnlinkAsync(TechnicianContext actor, string scopeDn, string gpoId, CancellationToken ct = default)
        => Run(actor, scopeDn, $"attempt: unlink GPO {gpoId}", () => _gpo.UnlinkAsync(scopeDn, gpoId, ct), ct);

    public Task<OperationResult> SetLinkOptionsAsync(TechnicianContext actor, string scopeDn, string gpoId, bool enforced, bool enabled, CancellationToken ct = default)
        => Run(actor, scopeDn, $"attempt: GPO link options enforced={enforced} enabled={enabled}", () => _gpo.SetLinkOptionsAsync(scopeDn, gpoId, enforced, enabled, ct), ct);

    private async Task<OperationResult> Run(TechnicianContext actor, string scopeDn, string attemptMsg, Func<Task<OperationResult>> op, CancellationToken ct)
    {
        var decision = await _rbac.AuthorizeAsync(actor, Permission.ManageGpoLinks, scopeDn, ct);
        if (!decision.Allowed)
        {
            var reason = decision.Reason ?? "not authorized";
            await _audit.WriteAsync(Entry(actor, scopeDn, AuditPhase.Result, false, reason), ct);
            return OperationResult.Fail(reason);
        }

        await _audit.WriteAsync(Entry(actor, scopeDn, AuditPhase.Attempt, false, attemptMsg), ct);
        var result = await op();
        await _audit.WriteAsync(Entry(actor, scopeDn, AuditPhase.Result, result.Success, result.Message), ct);
        return result;
    }

    private static AuditEntry Entry(TechnicianContext a, string dn, AuditPhase phase, bool ok, string? msg) => new()
    {
        Phase = phase,
        TimestampMsk = MskTime.Now,
        ActorSid = a.Sid,
        ActorName = a.DisplayName,
        Operation = Permission.ManageGpoLinks,
        TargetDn = dn,
        Success = ok,
        Message = msg,
    };
}
