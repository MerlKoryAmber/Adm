using System.Net;
using AdManager.Domain;
using AdManager.Domain.Enums;

namespace AdManager.Application.Abstractions;

// ---------- DTO ----------

public sealed record TechnicianContext(string Sid, string Upn, string DisplayName, IReadOnlyCollection<string>? GroupSids = null);

public sealed record AuthorizationDecision(bool Allowed, string? Reason = null);

public sealed record CreateUserRequest(
    string TargetOuDn,
    string SamAccountName,
    string DisplayName,
    string UserPrincipalName,
    string? InitialPassword = null,
    bool Enabled = false,
    IReadOnlyDictionary<string, string?>? Attributes = null);

public enum GroupScope { Global, DomainLocal, Universal }
public enum GroupKind { Security, Distribution }

public sealed record CreateGroupRequest(
    string TargetOuDn,
    string SamAccountName,
    string Name,
    GroupScope Scope = GroupScope.Global,
    GroupKind Kind = GroupKind.Security);

public sealed record CreateComputerRequest(string TargetOuDn, string Name);

public sealed record CreateContactRequest(
    string TargetOuDn,
    string Name,
    IReadOnlyDictionary<string, string?>? Attributes = null);

public sealed record CreateOuRequest(string ParentDn, string Name);

/// <summary>Опции учётной записи. null = не менять.</summary>
public sealed record AccountOptions(
    bool? PasswordNeverExpires = null,
    bool? MustChangePasswordAtNextLogon = null,
    bool? CannotChangePassword = null,
    DateTime? AccountExpiresUtc = null,
    bool ClearAccountExpiry = false,
    // userAccountControl-флаги (вкладка Account в ADUC)
    bool? ReversibleEncryption = null,   // ENCRYPTED_TEXT_PASSWORD_ALLOWED 0x80
    bool? SmartcardRequired = null,      // SMARTCARD_REQUIRED 0x40000
    bool? NotDelegated = null,           // NOT_DELEGATED 0x100000
    bool? UseDesKeyOnly = null,          // USE_DES_KEY_ONLY 0x200000
    bool? DontRequirePreauth = null);    // DONT_REQUIRE_PREAUTH 0x400000

public sealed record MailboxProperties(
    string? Alias = null,
    long? IssueWarningQuotaMb = null,
    long? ProhibitSendQuotaMb = null,
    bool? HiddenFromAddressLists = null);

public sealed record AuditQuery(
    DateTimeOffset? FromMsk = null,
    DateTimeOffset? ToMsk = null,
    string? ActorSid = null,
    string? TargetDn = null,
    int Take = 200);

public sealed record AutomationDefinition(
    Guid Id,
    string Name,
    string CronMsk,
    bool Enabled,
    string TaskType,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>Mode: "gMSA" (процесс) или "StoredCredential" (хранимая УЗ).</summary>
public sealed record OperationalIdentity(string Mode, NetworkCredential? Credential);

// ---------- Контракты ----------

/// <summary>Операции над объектами AD. Выполняются от operational identity, не от техника.</summary>
public interface IAdService
{
    Task<OperationResult> ResetPasswordAsync(string userDn, string newPassword, bool mustChangeAtNextLogon, CancellationToken ct = default);
    Task<OperationResult> SetAccountEnabledAsync(string userDn, bool enabled, CancellationToken ct = default);
    Task<OperationResult> UnlockAccountAsync(string userDn, CancellationToken ct = default);
    Task<OperationResult> MoveObjectAsync(string dn, string targetOuDn, CancellationToken ct = default);
    Task<OperationResult> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteObjectAsync(string dn, CancellationToken ct = default);
    Task<OperationResult> SetAttributesAsync(string dn, IReadOnlyDictionary<string, string?> attributes, CancellationToken ct = default);
    Task<OperationResult> ManageGroupMembershipAsync(string groupDn, IReadOnlyCollection<string> addMemberDns, IReadOnlyCollection<string> removeMemberDns, CancellationToken ct = default);

    // Расширенные операции (ADManager-набор)
    Task<OperationResult> RenameAsync(string dn, string newRdn, CancellationToken ct = default);
    Task<OperationResult> SetAccountOptionsAsync(string userDn, AccountOptions options, CancellationToken ct = default);
    Task<OperationResult> SetPrimaryGroupAsync(string userDn, string groupDn, CancellationToken ct = default);
    /// <summary>Записать logonHours (21 байт) или очистить (null/пусто = вход разрешён всегда).</summary>
    Task<OperationResult> SetLogonHoursAsync(string userDn, byte[]? mask, CancellationToken ct = default);
    Task<OperationResult> CreateGroupAsync(CreateGroupRequest request, CancellationToken ct = default);
    Task<OperationResult> CreateComputerAsync(CreateComputerRequest request, CancellationToken ct = default);
    Task<OperationResult> CreateContactAsync(CreateContactRequest request, CancellationToken ct = default);
    Task<OperationResult> CreateOuAsync(CreateOuRequest request, CancellationToken ct = default);
    Task<OperationResult> ResetComputerAccountAsync(string computerDn, CancellationToken ct = default);
}

/// <summary>Операции Exchange 2019 через remote PowerShell.</summary>
public interface IExchangeService
{
    Task<OperationResult> EnableMailboxAsync(string userDn, string? alias, CancellationToken ct = default);
    Task<OperationResult> DisableMailboxAsync(string userDn, CancellationToken ct = default);
    Task<OperationResult> SetMailboxPropertiesAsync(string identity, MailboxProperties properties, CancellationToken ct = default);
    Task<OperationResult> CreateDistributionGroupAsync(string name, string targetOuDn, CancellationToken ct = default);
    Task<OperationResult> ManageDistributionMembersAsync(string groupIdentity, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken ct = default);

    // Mailbox permissions (Full Access / Send As / Send on Behalf)
    Task<OperationResult> AddMailboxPermissionAsync(string identity, string trustee, CancellationToken ct = default);
    Task<OperationResult> RemoveMailboxPermissionAsync(string identity, string trustee, CancellationToken ct = default);
    Task<OperationResult> AddSendAsAsync(string identity, string trustee, CancellationToken ct = default);
    Task<OperationResult> RemoveSendAsAsync(string identity, string trustee, CancellationToken ct = default);
    Task<OperationResult> SetSendOnBehalfAsync(string identity, string trustee, bool add, CancellationToken ct = default);
}

/// <summary>Граница безопасности: разрешена ли операция технику над объектом (роль + scope).</summary>
public interface IRbacEngine
{
    Task<AuthorizationDecision> AuthorizeAsync(TechnicianContext actor, Permission operation, string targetDn, CancellationToken ct = default);
}

/// <summary>Неизменяемый аудит (§аудит).</summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

/// <summary>Отдаёт identity/креды для операций: gMSA (процесс) или хранимая УЗ.</summary>
public interface IOperationalCredentialProvider
{
    OperationalIdentity GetIdentity(string domain);
}

/// <summary>Планировщик автоматизаций (без апрув-workflow).</summary>
public interface IAutomationScheduler
{
    Task RegisterAsync(AutomationDefinition definition, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationDefinition>> ListAsync(CancellationToken ct = default);
    Task TriggerNowAsync(Guid id, CancellationToken ct = default);
}
