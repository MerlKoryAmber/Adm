using System.Net;
using AdManager.Application;
using AdManager.Domain;
using AdManager.Domain.Enums;

namespace AdManager.Application.Abstractions;

// ---------- DTO ----------

public sealed record TechnicianContext(string Sid, string Upn, string DisplayName, IReadOnlyCollection<string>? GroupSids = null, bool IsSuperAdmin = false);

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

/// <summary>Параметры создания почтового ящика (Enable-Mailbox), включая персональный архив.</summary>
public sealed record MailboxProvisioning(
    string? Alias = null,
    string? Database = null,            // mailbox database (пусто = авто-выбор Exchange)
    bool EnableArchive = false,
    string? ArchiveDatabase = null);    // база персонального архива (пусто = та же/авто)

/// <summary>Почтовая база Exchange (кэшируется).</summary>
public sealed record MailboxDatabase(string Name, string? Server = null, string? Guid = null);

/// <summary>Мобильное устройство ActiveSync, привязанное к ящику.</summary>
public sealed record MobileDevice(
    string Identity,
    string? DeviceModel = null,
    string? DeviceOs = null,
    string? DeviceType = null,
    string? FirstSyncUtc = null,
    string? LastSyncUtc = null);

/// <summary>Текущее состояние ящика (для формы Modify). Кэшируется (AD+Exchange).</summary>
public sealed record MailboxInfo(
    bool HasMailbox,
    string? PrimarySmtp = null,
    IReadOnlyList<string>? EmailAddresses = null,     // proxyAddresses
    bool HiddenFromAddressLists = false,
    string? ForwardingAddress = null,
    bool DeliverToMailboxAndForward = false,
    IReadOnlyList<string>? FullAccess = null,
    IReadOnlyList<string>? SendAs = null,
    IReadOnlyList<string>? SendOnBehalf = null,
    bool ArchiveEnabled = false,
    string? Database = null);

public sealed record AuditQuery(
    DateTimeOffset? FromMsk = null,
    DateTimeOffset? ToMsk = null,
    string? ActorSid = null,
    string? TargetDn = null,
    int Take = 200,
    AuditKind? Kind = null);

/// <summary>Определение автоматизации (эталон ADManager Plus): задача над объектами
/// из отчёта, по человекочитаемому расписанию. Cron не используется.</summary>
public sealed class AutomationDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Задача (AutomationTaskTypes) + её параметры (напр. groupDn, targetOu).</summary>
    public string TaskType { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>Источник объектов — ключ отчёта из IReportService (Select objects → From Report).</summary>
    public string SourceReportKey { get; set; } = "";

    /// <summary>Refine Result: доп-условия поверх строк отчёта (все должны совпасть, AND).</summary>
    public List<RefineCondition> Refine { get; set; } = new();

    /// <summary>Исключать объекты, обработанные прошлым запуском (чтобы не гонять одно и то же).</summary>
    public bool ExcludePreviouslyModified { get; set; }

    /// <summary>DN, обработанные прошлым запуском (для ExcludePreviouslyModified).</summary>
    public List<string> LastProcessedDns { get; set; } = new();

    /// <summary>Когда запускать (человекочитаемо).</summary>
    public AutomationSchedule Schedule { get; set; } = new();

    public DateTime? LastRunMsk { get; set; }
    public string? LastRunResult { get; set; }
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}

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
    /// <summary>Заменить все значения многозначного атрибута (пустой список = очистить).</summary>
    Task<OperationResult> SetMultiValueAsync(string dn, string attribute, IReadOnlyList<string> values, CancellationToken ct = default);
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
    /// <summary>Enable-Mailbox с выбором базы и (опц.) включением персонального архива.</summary>
    Task<OperationResult> EnableMailboxAsync(string userDn, MailboxProvisioning provisioning, CancellationToken ct = default);
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

    // --- расширенные mailbox-операции (Modify) ---
    /// <summary>Список почтовых баз (для селекторов). Кэшируется вызывающим слоем.</summary>
    Task<IReadOnlyList<MailboxDatabase>> ListDatabasesAsync(CancellationToken ct = default);
    /// <summary>Текущее состояние ящика (адреса, скрытие, форвардинг, права, архив).</summary>
    Task<MailboxInfo> GetMailboxInfoAsync(string identity, CancellationToken ct = default);
    /// <summary>Заменить список email-адресов (proxyAddresses). Первый Primary — SMTP:, прочие smtp:.</summary>
    Task<OperationResult> SetEmailAddressesAsync(string identity, IReadOnlyList<string> addresses, string? primarySmtp, CancellationToken ct = default);
    /// <summary>Переадресация: адрес (пусто = снять) + доставлять ли копию в сам ящик.</summary>
    Task<OperationResult> SetForwardingAsync(string identity, string? forwardingSmtp, bool deliverToMailboxAndForward, CancellationToken ct = default);
    /// <summary>Скрыть/показать ящик в адресной книге.</summary>
    Task<OperationResult> SetHiddenFromAddressListsAsync(string identity, bool hidden, CancellationToken ct = default);
    /// <summary>Включить/выключить персональный архив (опц. на конкретной базе).</summary>
    Task<OperationResult> SetArchiveAsync(string identity, bool enabled, string? archiveDatabase, CancellationToken ct = default);
    /// <summary>Список мобильных устройств (ActiveSync) ящика.</summary>
    Task<IReadOnlyList<MobileDevice>> ListMobileDevicesAsync(string identity, CancellationToken ct = default);
    /// <summary>Удалённая очистка (wipe) мобильного устройства.</summary>
    Task<OperationResult> WipeMobileDeviceAsync(string deviceIdentity, CancellationToken ct = default);
    /// <summary>Убрать партнёрство с мобильным устройством (Remove-MobileDevice).</summary>
    Task<OperationResult> RemoveMobileDeviceAsync(string deviceIdentity, CancellationToken ct = default);
}

/// <summary>Какие поля техник вправе задавать для операции над объектом.</summary>
/// <param name="Unrestricted">true = ограничений нет (роль без пополевого списка) — разрешены все поля.</param>
/// <param name="Fields">Разрешённые ключи полей (FieldCatalog), когда <paramref name="Unrestricted"/> = false.</param>
public sealed record FieldPermission(bool Unrestricted, IReadOnlySet<string> Fields)
{
    public bool Allows(string fieldKey) => Unrestricted || Fields.Contains(fieldKey);
    public static readonly FieldPermission All = new(true, new HashSet<string>());
    public static readonly FieldPermission None = new(false, new HashSet<string>());
}

/// <summary>Граница безопасности: разрешена ли операция технику над объектом (роль + scope).</summary>
public interface IRbacEngine
{
    Task<AuthorizationDecision> AuthorizeAsync(TechnicianContext actor, Permission operation, string targetDn, CancellationToken ct = default);

    /// <summary>Разрешённые поля для CreateUser/ModifyAttributes над targetDn (объединение по совпавшим ролям;
    /// роль без списка = без ограничений). Только для этих двух операций; для прочих — <see cref="FieldPermission.All"/>.</summary>
    Task<FieldPermission> AllowedFieldsAsync(TechnicianContext actor, Permission operation, string targetDn, CancellationToken ct = default);

    /// <summary>Принудительный шаблон формы (Create|Modify) для актора над targetDn: Id первого назначения
    /// в scope, где шаблон задан. null — принуждения нет (техник выбирает сам, или супер-админ). </summary>
    Task<Guid?> EnforcedTemplateAsync(TechnicianContext actor, string kind, string targetDn, CancellationToken ct = default);

    /// <summary>Все операции, доступные актору хотя бы в одном scope (для фильтрации меню/навигации).
    /// Супер-админ — все операции. Не учитывает конкретный target (грубая проверка «есть ли право вообще»).</summary>
    Task<EffectiveAccess> EffectiveAccessAsync(TechnicianContext actor, CancellationToken ct = default);
}

/// <summary>Сводный доступ актора (для UI-навигации). SuperAdmin — полный доступ.</summary>
public sealed record EffectiveAccess(bool IsSuperAdmin, IReadOnlySet<Permission> Permissions)
{
    public bool Can(Permission p) => IsSuperAdmin || Permissions.Contains(p);
    public bool CanAny(params Permission[] ps) => IsSuperAdmin || ps.Any(Permissions.Contains);
}

/// <summary>Неизменяемый аудит (§аудит).</summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct = default);
    /// <summary>Удалить записи старше указанной даты (retention). Возвращает число удалённых.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default);
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
