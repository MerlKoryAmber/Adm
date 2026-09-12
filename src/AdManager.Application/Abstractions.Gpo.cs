using AdManager.Domain;

namespace AdManager.Application.Abstractions;

/// <summary>Объект групповой политики (groupPolicyContainer).</summary>
public sealed record GpoSummary(
    string Id,               // {GUID} с фигурными скобками
    string DisplayName,
    string Dn,
    int Version,
    bool UserSettingsDisabled,
    bool ComputerSettingsDisabled,
    DateTime? WhenChanged);

/// <summary>Линк GPO на scope (домен/OU): gPLink-запись.</summary>
public sealed record GpoLink(
    string ScopeDn,
    string ScopeName,
    string GpoId,
    string GpoDisplayName,
    int Order,               // порядок в gPLink (1 — первый)
    bool Enforced,
    bool Enabled);

/// <summary>Чтение групповых политик и их линков (read-only, под operational identity).</summary>
public interface IGpoDirectory
{
    Task<IReadOnlyList<GpoSummary>> ListGposAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GpoLink>> ListLinksAsync(string scopeDn, CancellationToken ct = default);
    /// <summary>Все линки по домену и всем OU (для отчётов linked/unlinked).</summary>
    Task<IReadOnlyList<GpoLink>> ListAllLinksAsync(CancellationToken ct = default);
}

/// <summary>Управление линками GPO (write). Правит атрибут gPLink на scope через LDAP.</summary>
public interface IGpoService
{
    Task<OperationResult> LinkAsync(string scopeDn, string gpoId, CancellationToken ct = default);
    Task<OperationResult> UnlinkAsync(string scopeDn, string gpoId, CancellationToken ct = default);
    Task<OperationResult> SetLinkOptionsAsync(string scopeDn, string gpoId, bool enforced, bool enabled, CancellationToken ct = default);
}
