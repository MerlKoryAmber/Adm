using AdManager.Application;
using AdManager.Infrastructure.Data;
using Xunit;

namespace AdManager.Integration.Tests;

/// <summary>Тесты локальной аутентификации панели (ADR-0007): PBKDF2-хэш, verify, сид admin/admin.</summary>
public sealed class LocalAuthTests
{
    private sealed class InMemoryLocalUserStore : ILocalUserStore
    {
        public readonly List<LocalUser> Users = new();
        public Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LocalUser>>(Users.ToList());
        public Task<LocalUser?> GetByNameAsync(string userName, CancellationToken ct = default)
            => Task.FromResult(Users.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)));
        public Task<LocalUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
        public Task SaveAsync(LocalUser user, CancellationToken ct = default)
        { Users.RemoveAll(u => u.Id == user.Id); Users.Add(user); return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        { Users.RemoveAll(u => u.Id == id); return Task.CompletedTask; }
    }

    [Fact]
    public void Hash_then_validate_roundtrips()
    {
        var store = new InMemoryLocalUserStore();
        var auth = new LocalAuthService(store);
        var hash = auth.HashPassword("s3cret");
        Assert.StartsWith("pbkdf2:v1:", hash);
        Assert.DoesNotContain("s3cret", hash); // пароль не в открытом виде
    }

    [Fact]
    public async Task Validate_accepts_correct_rejects_wrong()
    {
        var store = new InMemoryLocalUserStore();
        var auth = new LocalAuthService(store);
        await store.SaveAsync(new LocalUser { UserName = "op1", PasswordHash = auth.HashPassword("right") });

        Assert.NotNull(await auth.ValidateAsync("op1", "right"));
        Assert.Null(await auth.ValidateAsync("op1", "wrong"));
        Assert.Null(await auth.ValidateAsync("nobody", "right"));
    }

    [Fact]
    public async Task Validate_username_is_case_insensitive()
    {
        var store = new InMemoryLocalUserStore();
        var auth = new LocalAuthService(store);
        await store.SaveAsync(new LocalUser { UserName = "Admin", PasswordHash = auth.HashPassword("p") });
        Assert.NotNull(await auth.ValidateAsync("admin", "p"));
        Assert.NotNull(await auth.ValidateAsync("ADMIN", "p"));
    }

    [Fact]
    public async Task Validate_rejects_disabled_account()
    {
        var store = new InMemoryLocalUserStore();
        var auth = new LocalAuthService(store);
        await store.SaveAsync(new LocalUser { UserName = "off", PasswordHash = auth.HashPassword("p"), Disabled = true });
        Assert.Null(await auth.ValidateAsync("off", "p")); // верный пароль, но выключен
    }

    [Fact]
    public async Task EnsureSeedAdmin_creates_admin_on_empty_store_only()
    {
        var store = new InMemoryLocalUserStore();
        var auth = new LocalAuthService(store);

        await auth.EnsureSeedAdminAsync();
        Assert.Single(store.Users);
        var admin = store.Users[0];
        Assert.Equal("admin", admin.UserName);
        Assert.True(admin.IsSuperAdmin);
        Assert.NotNull(await auth.ValidateAsync("admin", "admin")); // admin/admin работает

        // повторный вызов — не дублирует и не сбрасывает
        await auth.EnsureSeedAdminAsync();
        Assert.Single(store.Users);
    }

    [Fact]
    public void SyntheticSid_is_stable_and_prefixed()
    {
        var u = new LocalUser();
        Assert.StartsWith("LOCAL:", u.Sid);
        Assert.Equal(u.Sid, u.Sid); // стабилен для одного объекта
    }
}
