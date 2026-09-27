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

    public Task<OperationResult> AddMailboxPermissionAsync(TechnicianContext actor, string identity, string trustee, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxPermissions, identity, $"attempt: add Full Access for {trustee}", () => _ex.AddMailboxPermissionAsync(identity, trustee, ct), ct);

    public Task<OperationResult> RemoveMailboxPermissionAsync(TechnicianContext actor, string identity, string trustee, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxPermissions, identity, $"attempt: remove Full Access for {trustee}", () => _ex.RemoveMailboxPermissionAsync(identity, trustee, ct), ct);

    public Task<OperationResult> AddSendAsAsync(TechnicianContext actor, string identity, string trustee, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxPermissions, identity, $"attempt: add Send As for {trustee}", () => _ex.AddSendAsAsync(identity, trustee, ct), ct);

    public Task<OperationResult> RemoveSendAsAsync(TechnicianContext actor, string identity, string trustee, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxPermissions, identity, $"attempt: remove Send As for {trustee}", () => _ex.RemoveSendAsAsync(identity, trustee, ct), ct);

    public Task<OperationResult> SetSendOnBehalfAsync(TechnicianContext actor, string identity, string trustee, bool add, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxPermissions, identity, $"attempt: {(add ? "add" : "remove")} Send on Behalf for {trustee}", () => _ex.SetSendOnBehalfAsync(identity, trustee, add, ct), ct);

    // --- Создание ящика с базой/архивом (Create user tab) ---
    public Task<OperationResult> EnableMailboxAsync(TechnicianContext actor, string userDn, MailboxProvisioning p, CancellationToken ct = default)
        => Run(actor, Permission.EnableMailbox, userDn, "attempt: enable mailbox", () => _ex.EnableMailboxAsync(userDn, p, ct), ct);

    // --- Modify: email-адреса ---
    public Task<OperationResult> SetEmailAddressesAsync(TechnicianContext actor, string identity, IReadOnlyList<string> addresses, string? primarySmtp, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxEmailAddresses, identity, "attempt: set email addresses", () => _ex.SetEmailAddressesAsync(identity, addresses, primarySmtp, ct), ct);

    // --- Modify: переадресация ---
    public Task<OperationResult> SetForwardingAsync(TechnicianContext actor, string identity, string? fwd, bool deliverAndForward, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxForwarding, identity, "attempt: set forwarding", () => _ex.SetForwardingAsync(identity, fwd, deliverAndForward, ct), ct);

    // --- Modify: скрытие из адресной книги ---
    public Task<OperationResult> SetHiddenFromAddressListsAsync(TechnicianContext actor, string identity, bool hidden, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxAddressBook, identity, $"attempt: {(hidden ? "hide" : "show")} in address book", () => _ex.SetHiddenFromAddressListsAsync(identity, hidden, ct), ct);

    // --- Modify: персональный архив ---
    public Task<OperationResult> SetArchiveAsync(TechnicianContext actor, string identity, bool enabled, string? archiveDatabase, CancellationToken ct = default)
        => Run(actor, Permission.SetMailboxProperties, identity, $"attempt: {(enabled ? "enable" : "disable")} personal archive", () => _ex.SetArchiveAsync(identity, enabled, archiveDatabase, ct), ct);

    // --- Modify: мобильные устройства ---
    public Task<OperationResult> WipeMobileDeviceAsync(TechnicianContext actor, string mailboxDn, string deviceIdentity, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxMobile, mailboxDn, $"attempt: wipe mobile device {deviceIdentity}", () => _ex.WipeMobileDeviceAsync(deviceIdentity, ct), ct);

    public Task<OperationResult> RemoveMobileDeviceAsync(TechnicianContext actor, string mailboxDn, string deviceIdentity, CancellationToken ct = default)
        => Run(actor, Permission.ManageMailboxMobile, mailboxDn, $"attempt: remove mobile device {deviceIdentity}", () => _ex.RemoveMobileDeviceAsync(deviceIdentity, ct), ct);

    // --- Чтение (для форм; проверка права + без аудита-намерения) ---
    public async Task<MailboxInfo> GetMailboxInfoAsync(TechnicianContext actor, string identity, CancellationToken ct = default)
    {
        var d = await _rbac.AuthorizeAsync(actor, Permission.SetMailboxProperties, identity, ct);
        if (!d.Allowed) return new MailboxInfo(false);
        return await _ex.GetMailboxInfoAsync(identity, ct);
    }

    public async Task<IReadOnlyList<MobileDevice>> ListMobileDevicesAsync(TechnicianContext actor, string identity, CancellationToken ct = default)
    {
        var d = await _rbac.AuthorizeAsync(actor, Permission.ManageMailboxMobile, identity, ct);
        if (!d.Allowed) return Array.Empty<MobileDevice>();
        return await _ex.ListMobileDevicesAsync(identity, ct);
    }

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
