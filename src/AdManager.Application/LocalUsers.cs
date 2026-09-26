namespace AdManager.Application;

/// <summary>
/// Локальная УЗ панели (ADR-0007): вход по логину/паролю, не из AD.
/// SID синтетический ("LOCAL:&lt;guid&gt;") — вписывается в модель RBAC/аудита по SID.
/// Пароль — хэш PBKDF2 (необратим), не шифруется envelope.
/// </summary>
public sealed class LocalUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Disabled { get; set; }
    public bool IsSuperAdmin { get; set; }
    /// <summary>Встроенная УЗ (сид admin при установке). Нельзя удалять/отключать.</summary>
    public bool IsBuiltin { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Стабильный синтетический SID для RBAC/аудита.</summary>
    public string Sid => "LOCAL:" + Id.ToString("N");
}

/// <summary>Хранилище локальных УЗ панели (EF, в БД по ADR-0006).</summary>
public interface ILocalUserStore
{
    Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken ct = default);
    Task<LocalUser?> GetByNameAsync(string userName, CancellationToken ct = default);
    Task<LocalUser?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(LocalUser user, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Аутентификация локальных УЗ панели: проверка пароля, хэширование, сид admin/admin.
/// </summary>
public interface ILocalAuthService
{
    /// <summary>Проверить логин/пароль. Возвращает УЗ при успехе, иначе null (в т.ч. если Disabled).</summary>
    Task<LocalUser?> ValidateAsync(string userName, string password, CancellationToken ct = default);
    /// <summary>Хэш пароля (PBKDF2) для сохранения в LocalUser.PasswordHash.</summary>
    string HashPassword(string password);
    /// <summary>Создать сид-УЗ admin/admin (супер-админ), если таблица локальных УЗ пуста (ADR-0007).</summary>
    Task EnsureSeedAdminAsync(CancellationToken ct = default);
}
