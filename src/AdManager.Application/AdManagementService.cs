using AdManager.Application.Abstractions;
using AdManager.Domain;
using AdManager.Domain.Enums;

namespace AdManager.Application;

/// <summary>
/// Единый делегированный конвейер операций AD (по образцу ADManager Plus):
/// RBAC → аудит(намерение) → операция под operational identity → аудит(результат).
/// UI и автоматизация ходят только сюда, не в IAdService напрямую.
/// </summary>
public sealed class AdManagementService
{
    private readonly IRbacEngine _rbac;
    private readonly IAdService _ad;
    private readonly IAuditLog _audit;

    public AdManagementService(IRbacEngine rbac, IAdService ad, IAuditLog audit)
    {
        _rbac = rbac;
        _ad = ad;
        _audit = audit;
    }

    public Task<OperationResult> ResetPasswordAsync(TechnicianContext actor, string userDn, string newPassword, bool mustChange, CancellationToken ct = default)
        => Run(actor, Permission.ResetPassword, userDn, "attempt: reset password", () => _ad.ResetPasswordAsync(userDn, newPassword, mustChange, ct), ct);

    public Task<OperationResult> SetEnabledAsync(TechnicianContext actor, string userDn, bool enabled, CancellationToken ct = default)
        => Run(actor, Permission.EnableDisableUser, userDn, enabled ? "attempt: enable" : "attempt: disable", () => _ad.SetAccountEnabledAsync(userDn, enabled, ct), ct);

    public Task<OperationResult> UnlockAsync(TechnicianContext actor, string userDn, CancellationToken ct = default)
        => Run(actor, Permission.UnlockAccount, userDn, "attempt: unlock", () => _ad.UnlockAccountAsync(userDn, ct), ct);

    public Task<OperationResult> MoveAsync(TechnicianContext actor, string dn, string targetOuDn, CancellationToken ct = default)
        => Run(actor, Permission.MoveObject, dn, $"attempt: move to {targetOuDn}", () => _ad.MoveObjectAsync(dn, targetOuDn, ct), ct);

    public Task<OperationResult> CreateUserAsync(TechnicianContext actor, CreateUserRequest request, CancellationToken ct = default)
        => Run(actor, Permission.CreateUser, request.TargetOuDn, $"attempt: create {request.SamAccountName}", () => _ad.CreateUserAsync(request, ct), ct);

    public Task<OperationResult> DeleteAsync(TechnicianContext actor, string dn, CancellationToken ct = default)
        => Run(actor, Permission.DeleteUser, dn, "attempt: delete", () => _ad.DeleteObjectAsync(dn, ct), ct);

    public Task<OperationResult> SetAttributesAsync(TechnicianContext actor, string dn, IReadOnlyDictionary<string, string?> attributes, CancellationToken ct = default)
        => Run(actor, Permission.ModifyAttributes, dn, "attempt: modify attributes", () => _ad.SetAttributesAsync(dn, attributes, ct), ct);

    public Task<OperationResult> ManageGroupMembershipAsync(TechnicianContext actor, string groupDn, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken ct = default)
        => Run(actor, Permission.ManageGroupMembership, groupDn, "attempt: group membership", () => _ad.ManageGroupMembershipAsync(groupDn, add, remove, ct), ct);

    public Task<OperationResult> RenameAsync(TechnicianContext actor, string dn, string newRdn, CancellationToken ct = default)
        => Run(actor, Permission.Rename, dn, $"attempt: rename to {newRdn}", () => _ad.RenameAsync(dn, newRdn, ct), ct);

    public Task<OperationResult> SetAccountOptionsAsync(TechnicianContext actor, string userDn, AccountOptions options, CancellationToken ct = default)
        => Run(actor, Permission.SetAccountOptions, userDn, "attempt: account options", () => _ad.SetAccountOptionsAsync(userDn, options, ct), ct);

    public Task<OperationResult> SetPrimaryGroupAsync(TechnicianContext actor, string userDn, string groupDn, CancellationToken ct = default)
        => Run(actor, Permission.ManageGroupMembership, userDn, $"attempt: set primary group {groupDn}", () => _ad.SetPrimaryGroupAsync(userDn, groupDn, ct), ct);

    public Task<OperationResult> SetLogonHoursAsync(TechnicianContext actor, string userDn, byte[]? mask, CancellationToken ct = default)
        => Run(actor, Permission.SetAccountOptions, userDn, "attempt: set logon hours", () => _ad.SetLogonHoursAsync(userDn, mask, ct), ct);

    public Task<OperationResult> CreateGroupAsync(TechnicianContext actor, CreateGroupRequest request, CancellationToken ct = default)
        => Run(actor, Permission.CreateGroup, request.TargetOuDn, $"attempt: create group {request.SamAccountName}", () => _ad.CreateGroupAsync(request, ct), ct);

    public Task<OperationResult> CreateComputerAsync(TechnicianContext actor, CreateComputerRequest request, CancellationToken ct = default)
        => Run(actor, Permission.CreateComputer, request.TargetOuDn, $"attempt: create computer {request.Name}", () => _ad.CreateComputerAsync(request, ct), ct);

    public Task<OperationResult> CreateContactAsync(TechnicianContext actor, CreateContactRequest request, CancellationToken ct = default)
        => Run(actor, Permission.CreateContact, request.TargetOuDn, $"attempt: create contact {request.Name}", () => _ad.CreateContactAsync(request, ct), ct);

    public Task<OperationResult> CreateOuAsync(TechnicianContext actor, CreateOuRequest request, CancellationToken ct = default)
        => Run(actor, Permission.CreateOu, request.ParentDn, $"attempt: create OU {request.Name}", () => _ad.CreateOuAsync(request, ct), ct);

    public Task<OperationResult> DeleteObjectAsync(TechnicianContext actor, string dn, CancellationToken ct = default)
        => Run(actor, Permission.DeleteObject, dn, "attempt: delete", () => _ad.DeleteObjectAsync(dn, ct), ct);

    public Task<OperationResult> ResetComputerAccountAsync(TechnicianContext actor, string dn, CancellationToken ct = default)
        => Run(actor, Permission.ManageComputer, dn, "attempt: reset computer account", () => _ad.ResetComputerAccountAsync(dn, ct), ct);

    public Task<OperationResult> SetAttributesAsync(TechnicianContext actor, string dn, IReadOnlyDictionary<string, string?> attributes, string targetLabel, CancellationToken ct = default)
        => Run(actor, Permission.ModifyAttributes, dn, $"attempt: modify {targetLabel}", () => _ad.SetAttributesAsync(dn, attributes, ct), ct);

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
