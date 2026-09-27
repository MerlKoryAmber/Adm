namespace AdManager.Application.Abstractions;

// ---------- DTO чтения каталога ----------

public sealed record AdUserSummary(
    string Dn,
    string SamAccountName,
    string DisplayName,
    string? Upn,
    string? Mail,
    bool Enabled,
    bool LockedOut);

public sealed record AdGroupSummary(string Dn, string SamAccountName, string Name);

public sealed record AdOuSummary(string Dn, string Name, bool HasChildren = false);

/// <summary>OU для выпадающего списка (Depth — уровень вложенности от базы, для отступа).</summary>
public sealed record AdOuNode(string Dn, string Name, int Depth);

public sealed record AdComputerSummary(string Dn, string SamAccountName, string Name, bool Enabled, string? OperatingSystem);

public sealed record AdContactSummary(string Dn, string Name, string? Mail);

public sealed record AdSearchResult(string Dn, string Name, string? SamAccountName, string ObjectClass, bool? Enabled);

public sealed record AdObjectDetails(string Dn, IReadOnlyDictionary<string, string?> Attributes);

/// <summary>Страница результатов + общее число совпадений (для серверной пагинации).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total)
{
    public static readonly PagedResult<T> Empty = new(Array.Empty<T>(), 0);
}

/// <summary>Фильтры грида пользователей (серверная сторона).</summary>
public sealed record UserListFilter(
    string? Search = null,
    bool LockedOnly = false,
    bool DisabledOnly = false,
    bool HideDisabled = false);

/// <summary>Чтение каталога AD (для UI-обзора). Read-only, под operational identity.</summary>
public interface IAdDirectory
{
    Task<IReadOnlyList<AdUserSummary>> ListUsersAsync(string ouDn, bool subtree, CancellationToken ct = default);
    Task<IReadOnlyList<AdGroupSummary>> ListGroupsAsync(string ouDn, bool subtree, CancellationToken ct = default);

    /// <summary>Страница пользователей с серверной пагинацией (VLV) + фильтрами. Total — всего совпадений.</summary>
    Task<PagedResult<AdUserSummary>> ListUsersPagedAsync(string ouDn, bool subtree, UserListFilter filter, int skip, int take, CancellationToken ct = default);
    /// <summary>Страница групп с серверной пагинацией (VLV) + поиск по подстроке.</summary>
    Task<PagedResult<AdGroupSummary>> ListGroupsPagedAsync(string ouDn, bool subtree, string? search, int skip, int take, CancellationToken ct = default);
    /// <summary>Страница компьютеров с серверной пагинацией (VLV) + поиск по подстроке + фильтр «только отключённые».</summary>
    Task<PagedResult<AdComputerSummary>> ListComputersPagedAsync(string ouDn, bool subtree, string? search, bool disabledOnly, int skip, int take, CancellationToken ct = default);
    /// <summary>Страница контактов с серверной пагинацией (VLV) + поиск по подстроке.</summary>
    Task<PagedResult<AdContactSummary>> ListContactsPagedAsync(string ouDn, bool subtree, string? search, int skip, int take, CancellationToken ct = default);
    Task<IReadOnlyList<AdOuSummary>> ListOusAsync(string parentDn, CancellationToken ct = default);
    Task<AdObjectDetails?> GetObjectAsync(string dn, IEnumerable<string> attributes, CancellationToken ct = default);
    Task<IReadOnlyList<AdUserSummary>> ListGroupMembersAsync(string groupDn, CancellationToken ct = default);

    /// <summary>Группы пользователя (memberOf, многозначный). Name — читаемое имя (CN).</summary>
    Task<IReadOnlyList<AdGroupSummary>> ListUserGroupsAsync(string userDn, CancellationToken ct = default);

    /// <summary>Маска logonHours (21 байт, 168 бит = 7×24, UTC). null — атрибут не задан (вход разрешён всегда).</summary>
    Task<byte[]?> GetLogonHoursAsync(string userDn, CancellationToken ct = default);

    /// <summary>Все значения многозначного атрибута (напр. otherTelephone, url, proxyAddresses).</summary>
    Task<IReadOnlyList<string>> GetMultiValueAsync(string dn, string attribute, CancellationToken ct = default);

    /// <summary>objectSid объекта в виде строки S-1-5-… (для назначений RBAC по имени).</summary>
    Task<string?> GetSidAsync(string dn, CancellationToken ct = default);
    Task<IReadOnlyList<AdComputerSummary>> ListComputersAsync(string ouDn, bool subtree, CancellationToken ct = default);
    Task<IReadOnlyList<AdContactSummary>> ListContactsAsync(string ouDn, bool subtree, CancellationToken ct = default);

    /// <summary>Все OU поддерева (для выбора OU в формах).</summary>
    Task<IReadOnlyList<AdOuNode>> ListAllOusAsync(string baseDn, CancellationToken ct = default);

    /// <summary>Поиск объектов по подстроке (cn/sAMAccountName/displayName/mail) в поддереве.</summary>
    Task<IReadOnlyList<AdSearchResult>> SearchAsync(string baseDn, string term, CancellationToken ct = default);

    /// <summary>Есть ли атрибут с таким LDAP-именем в схеме AD (для custom-атрибутов).</summary>
    Task<bool> AttributeExistsInSchemaAsync(string ldapName, CancellationToken ct = default);
}

/// <summary>
/// Кэш полного дерева OU (ADR: чтение AD дорого при тысячах OU). Держит дерево в
/// памяти, обновляет по расписанию (BackgroundService, раз в час) и по требованию.
/// Возвращает уже отфильтрованное дерево — без системных контейнеров (Domain
/// Controllers и well-known), куда делегировать scope нельзя.
/// </summary>
public interface IOuTreeProvider
{
    /// <summary>Полное дерево OU (иерархический порядок, Depth для отступа), из кэша.</summary>
    Task<IReadOnlyList<AdOuNode>> GetTreeAsync(CancellationToken ct = default);
    /// <summary>Принудительно перечитать дерево из AD (после правок OU или по кнопке Refresh).</summary>
    Task RefreshAsync(CancellationToken ct = default);
}
