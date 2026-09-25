using AdManager.Application;
using AdManager.Infrastructure.Data;
using Xunit;

namespace AdManager.Integration.Tests;

/// <summary>
/// Тесты envelope-шифрования секретов (ADR-0006): DpapiKeyring + EnvelopeSecretProtector.
/// Keyring пишет ключ во временный каталог (DPAPI machine-scope), чистим после.
/// Windows-only (DPAPI) — проект таргетит net8.0-windows.
/// </summary>
public sealed class SecretProtectionTests : IDisposable
{
    private readonly string _dir;

    public SecretProtectionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "admgr-keyring-test-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private EnvelopeSecretProtector NewProtector(out DpapiKeyring keyring)
    {
        keyring = new DpapiKeyring(_dir);
        return new EnvelopeSecretProtector(keyring);
    }

    [Fact]
    public void Protect_then_Unprotect_roundtrips()
    {
        var p = NewProtector(out _);
        const string secret = "P@ssw0rd-Кириллица-😀";
        var token = p.Protect(secret);

        Assert.NotEqual(secret, token);            // зашифровано
        Assert.True(p.IsProtected(token));         // самоописывающий токен
        Assert.Equal(secret, p.Unprotect(token));  // расшифровка совпала
    }

    [Fact]
    public void Protect_produces_different_ciphertext_each_time()
    {
        var p = NewProtector(out _);
        var a = p.Protect("same");
        var b = p.Protect("same");
        Assert.NotEqual(a, b); // случайный nonce -> разный шифротекст
        Assert.Equal("same", p.Unprotect(a));
        Assert.Equal("same", p.Unprotect(b));
    }

    [Fact]
    public void Unprotect_passes_through_legacy_plaintext()
    {
        var p = NewProtector(out _);
        // значение без префикса-токена = старое открытое (обратная совместимость миграции)
        Assert.Equal("legacy-plain", p.Unprotect("legacy-plain"));
        Assert.False(p.IsProtected("legacy-plain"));
    }

    [Fact]
    public void Protect_is_idempotent_on_already_encrypted()
    {
        var p = NewProtector(out _);
        var once = p.Protect("x");
        var twice = p.Protect(once);          // не шифруем дважды
        Assert.Equal(once, twice);
        Assert.Equal("x", p.Unprotect(twice));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmpty_passthrough(string? value)
    {
        var p = NewProtector(out _);
        Assert.Equal(value, p.Protect(value));
        Assert.Equal(value, p.Unprotect(value));
    }

    [Fact]
    public void Key_persists_across_instances()
    {
        // первый протектор шифрует и роняет ключ на диск
        var p1 = NewProtector(out var k1);
        var token = p1.Protect("persist-me");
        var fp1 = k1.KeyFingerprint();

        // новый keyring из того же каталога читает тот же ключ -> расшифровывает
        var k2 = new DpapiKeyring(_dir);
        var p2 = new EnvelopeSecretProtector(k2);
        Assert.Equal(fp1, k2.KeyFingerprint());
        Assert.Equal("persist-me", p2.Unprotect(token));
    }

    [Fact]
    public void Rotate_changes_key_and_old_key_decrypts_reencrypt()
    {
        var p = NewProtector(out var keyring);
        var token = p.Protect("rotate-me");
        var oldFp = keyring.KeyFingerprint();

        var oldKey = keyring.Rotate();
        var newFp = keyring.KeyFingerprint();

        Assert.NotEqual(oldFp, newFp);                       // ключ сменился
        Assert.NotEqual(oldKey, keyring.ExportKey());        // и байты другие

        // старый токен старым ключом ещё читается (для пере-шифровки миграцией)
        var oldKeyring = new StaticKeyForTest(oldKey);
        var oldProtector = new EnvelopeSecretProtector(oldKeyring);
        Assert.Equal("rotate-me", oldProtector.Unprotect(token));

        // после ротации новый Protect читается новым ключом
        var fresh = p.Protect("after-rotate");
        Assert.Equal("after-rotate", p.Unprotect(fresh));
    }

    // --- декоратор EncryptedSettingsStore ---

    [Fact]
    public async Task EncryptedStore_stores_ciphertext_returns_plaintext()
    {
        var inner = new InMemorySettingsStore();
        var protector = NewProtector(out _);
        var store = new EncryptedSettingsStore(inner, protector);

        var s = new AppSettings();
        s.Smtp.Password = "smtp-secret";
        s.Https.PfxPassword = "pfx-secret";
        s.OperationalCredential.Password = "op-secret";
        await store.SaveAsync(s);

        // в нижележащем сторе (== в БД) секреты зашифрованы
        Assert.True(protector.IsProtected(inner.Saved!.Smtp.Password));
        Assert.True(protector.IsProtected(inner.Saved!.Https.PfxPassword));
        Assert.True(protector.IsProtected(inner.Saved!.OperationalCredential.Password));

        // наружу через Load — расшифрованы
        var loaded = await store.LoadAsync();
        Assert.Equal("smtp-secret", loaded.Smtp.Password);
        Assert.Equal("pfx-secret", loaded.Https.PfxPassword);
        Assert.Equal("op-secret", loaded.OperationalCredential.Password);
    }

    [Fact]
    public async Task EncryptedStore_does_not_mutate_callers_object()
    {
        var inner = new InMemorySettingsStore();
        var store = new EncryptedSettingsStore(inner, NewProtector(out _));

        var s = new AppSettings();
        s.Smtp.Password = "keep-plain";
        await store.SaveAsync(s);

        // объект вызывающего остаётся с plaintext (в памяти работаем с ним дальше)
        Assert.Equal("keep-plain", s.Smtp.Password);
    }

    [Fact]
    public async Task EncryptedStore_reads_legacy_plaintext_from_db()
    {
        // в БД лежит старое НЕзашифрованное значение (до внедрения шифрования)
        var inner = new InMemorySettingsStore();
        inner.Saved = new AppSettings();
        inner.Saved.Smtp.Password = "old-plain-secret";
        var store = new EncryptedSettingsStore(inner, NewProtector(out _));

        var loaded = await store.LoadAsync();
        Assert.Equal("old-plain-secret", loaded.Smtp.Password); // passthrough, не падаем
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        public AppSettings? Saved;
        public Task<AppSettings> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(Saved ?? new AppSettings());
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    /// <summary>Мини-keyring поверх фиксированного ключа — для проверки, что старый
    /// ключ после ротации ещё расшифровывает старые токены.</summary>
    private sealed class StaticKeyForTest : DpapiKeyring
    {
        private readonly byte[] _key;
        public StaticKeyForTest(byte[] key) : base(Path.Combine(Path.GetTempPath(), "admgr-static-" + Guid.NewGuid().ToString("N")))
            => _key = key;
        public override byte[] GetKey() => _key;
    }
}
