using System.Security.Cryptography;
using AdManager.Application;
using Microsoft.EntityFrameworkCore;

namespace AdManager.Infrastructure.Data;

/// <summary>Обёртка-документ для списка локальных УЗ (JSON-хранение в AppState).</summary>
public sealed class LocalUserList
{
    public List<LocalUser> Items { get; set; } = new();
}

/// <summary>Локальные УЗ панели в БД (ADR-0007): весь список — один JSON-документ.</summary>
public sealed class EfLocalUserStore : EfAppStateStore<LocalUserList>, ILocalUserStore
{
    public EfLocalUserStore(IDbContextFactory<AdManagerDbContext> factory)
        : base(factory, "localusers") { }

    public async Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken ct = default)
        => (await LoadCoreAsync(ct)).Items;

    public async Task<LocalUser?> GetByNameAsync(string userName, CancellationToken ct = default)
        => (await LoadCoreAsync(ct)).Items
            .FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));

    public async Task<LocalUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => (await LoadCoreAsync(ct)).Items.FirstOrDefault(u => u.Id == id);

    public async Task SaveAsync(LocalUser user, CancellationToken ct = default)
    {
        var list = await LoadCoreAsync(ct);
        list.Items.RemoveAll(u => u.Id == user.Id);
        list.Items.Add(user);
        await SaveCoreAsync(list, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var list = await LoadCoreAsync(ct);
        list.Items.RemoveAll(u => u.Id == id);
        await SaveCoreAsync(list, ct);
    }
}

/// <summary>
/// Аутентификация локальных УЗ (ADR-0007). Хэш — PBKDF2 (Rfc2898, SHA-256, 100k итераций),
/// формат "pbkdf2:v1:iter:base64(salt):base64(hash)". Без внешних пакетов.
/// </summary>
public sealed class LocalAuthService : ILocalAuthService
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "pbkdf2:v1:";

    private readonly ILocalUserStore _store;

    public LocalAuthService(ILocalUserStore store) => _store = store;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var parts = stored.Substring(Prefix.Length).Split(':');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var iter)) return false;
        byte[] salt, expected;
        try { salt = Convert.FromBase64String(parts[1]); expected = Convert.FromBase64String(parts[2]); }
        catch { return false; }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public async Task<LocalUser?> ValidateAsync(string userName, string password, CancellationToken ct = default)
    {
        var user = await _store.GetByNameAsync(userName, ct);
        if (user is null || user.Disabled) return null;
        return Verify(password, user.PasswordHash) ? user : null;
    }

    public async Task EnsureSeedAdminAsync(CancellationToken ct = default)
    {
        var existing = await _store.ListAsync(ct);
        if (existing.Count > 0) return; // уже есть локальные УЗ — не сидируем

        // ADR-0007: встроенный admin/admin при установке. Член встроенной роли Administrators
        // (сид роли+назначения — в Web bootstrap). Смена пароля не принуждается.
        var admin = new LocalUser
        {
            UserName = "admin",
            DisplayName = "Administrator (local)",
            IsSuperAdmin = true,
            IsBuiltin = true,
            PasswordHash = HashPassword("admin"),
        };
        await _store.SaveAsync(admin, ct);
    }
}
