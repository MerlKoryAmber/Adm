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

public sealed record AdOuSummary(string Dn, string Name);

/// <summary>OU для выпадающего списка (Depth — уровень вложенности от базы, для отступа).</summary>
public sealed record AdOuNode(string Dn, string Name, int Depth);

public sealed record AdComputerSummary(string Dn, string SamAccountName, string Name, bool Enabled, string? OperatingSystem);

public sealed record AdContactSummary(string Dn, string Name, string? Mail);

public sealed record AdSearchResult(string Dn, string Name, string? SamAccountName, string ObjectClass, bool? Enabled);

public sealed record AdObjectDetails(string Dn, IReadOnlyDictionary<string, string?> Attributes);

/// <summary>Чтение каталога AD (для UI-обзора). Read-only, под operational identity.</summary>
public interface IAdDirectory
{
    Task<IReadOnlyList<AdUserSummary>> ListUsersAsync(string ouDn, bool subtree, CancellationToken ct = default);
    Task<IReadOnlyList<AdGroupSummary>> ListGroupsAsync(string ouDn, bool subtree, CancellationToken ct = default);
    Task<IReadOnlyList<AdOuSummary>> ListOusAsync(string parentDn, CancellationToken ct = default);
    Task<AdObjectDetails?> GetObjectAsync(string dn, IEnumerable<string> attributes, CancellationToken ct = default);
    Task<IReadOnlyList<AdUserSummary>> ListGroupMembersAsync(string groupDn, CancellationToken ct = default);

    /// <summary>Группы пользователя (memberOf, многозначный). Name — читаемое имя (CN).</summary>
    Task<IReadOnlyList<AdGroupSummary>> ListUserGroupsAsync(string userDn, CancellationToken ct = default);

    /// <summary>Маска logonHours (21 байт, 168 бит = 7×24, UTC). null — атрибут не задан (вход разрешён всегда).</summary>
    Task<byte[]?> GetLogonHoursAsync(string userDn, CancellationToken ct = default);
    Task<IReadOnlyList<AdComputerSummary>> ListComputersAsync(string ouDn, bool subtree, CancellationToken ct = default);
    Task<IReadOnlyList<AdContactSummary>> ListContactsAsync(string ouDn, bool subtree, CancellationToken ct = default);

    /// <summary>Все OU поддерева (для выбора OU в формах).</summary>
    Task<IReadOnlyList<AdOuNode>> ListAllOusAsync(string baseDn, CancellationToken ct = default);

    /// <summary>Поиск объектов по подстроке (cn/sAMAccountName/displayName/mail) в поддереве.</summary>
    Task<IReadOnlyList<AdSearchResult>> SearchAsync(string baseDn, string term, CancellationToken ct = default);
}
