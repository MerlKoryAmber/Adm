using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using AdManager.Application;

namespace AdManager.Infrastructure.Data;

/// <summary>
/// Keyring: 32-байтный AES-ключ приложения, хранится ВНЕ БД (ADR-0006).
/// Файл на диске зашифрован DPAPI machine-scope (не лежит открытым).
/// Дефолт пути — C:\ProgramData\AdManager\keyring; переопределяется ADMGR_KEYRING.
/// Ключ не в каталоге сайта: переживает деплой, не попадает в publish.
/// DPAPI — Windows-only (всё приложение хостится на Windows).
/// </summary>
[SupportedOSPlatform("windows")]
public class DpapiKeyring : IKeyring
{
    private const int KeySize = 32; // AES-256
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AdManager.Keyring.v1");

    private readonly string _keyFile;
    private readonly object _lock = new();
    private byte[]? _key;

    public DpapiKeyring(string keyDir)
    {
        Directory.CreateDirectory(keyDir);
        _keyFile = Path.Combine(keyDir, "key.dat");
    }

    /// <summary>Текущий ключ. При первом обращении — читает с диска или генерирует новый.
    /// virtual — чтобы тесты могли подменить ключ без DPAPI-файла.</summary>
    public virtual byte[] GetKey()
    {
        if (_key != null) return _key;
        lock (_lock)
        {
            if (_key != null) return _key;
            _key = File.Exists(_keyFile) ? Load() : GenerateAndPersist();
            return _key;
        }
    }

    public byte[] Rotate()
    {
        lock (_lock)
        {
            var old = GetKey();
            _key = GenerateAndPersist();
            return old;
        }
    }

    public byte[] ExportKey() => (byte[])GetKey().Clone();

    public string KeyFingerprint()
    {
        using var sha = SHA256.Create();
        var h = sha.ComputeHash(GetKey());
        // короткий отпечаток (первые 8 байт hex) — для сверки бэкапа, не сам ключ
        return Convert.ToHexString(h, 0, 8);
    }

    private byte[] GenerateAndPersist()
    {
        var key = RandomNumberGenerator.GetBytes(KeySize);
        Persist(key);
        return key;
    }

    private void Persist(byte[] key)
    {
        var protectedBytes = ProtectedData.Protect(key, Entropy, DataProtectionScope.LocalMachine);
        // атомарная запись: во временный файл, затем replace
        var tmp = _keyFile + ".tmp";
        File.WriteAllBytes(tmp, protectedBytes);
        if (File.Exists(_keyFile)) File.Replace(tmp, _keyFile, null);
        else File.Move(tmp, _keyFile);
    }

    private byte[] Load()
    {
        var protectedBytes = File.ReadAllBytes(_keyFile);
        var key = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
        if (key.Length != KeySize)
            throw new InvalidOperationException($"Keyring: unexpected key size {key.Length}, expected {KeySize}.");
        return key;
    }
}

/// <summary>
/// Envelope-шифрование секретов AES-256-GCM ключом из keyring (ADR-0006).
/// Формат токена: "enc:v1:" + base64(nonce[12] | tag[16] | ciphertext).
/// Unprotect понимает и токен, и legacy-plain (строка без префикса — вернётся как есть).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EnvelopeSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12; // GCM standard
    private const int TagSize = 16;

    private readonly DpapiKeyring _keyring;

    public EnvelopeSecretProtector(DpapiKeyring keyring) => _keyring = keyring;

    public bool IsProtected(string? value) => value != null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (IsProtected(plaintext)) return plaintext; // уже зашифровано — не двойным слоем

        var key = _keyring.GetKey();
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        // nonce | tag | ciphertext
        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    public string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!IsProtected(stored)) return stored; // legacy-plain — обратная совместимость

        var blob = Convert.FromBase64String(stored.Substring(Prefix.Length));
        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("Secret token too short / corrupted.");

        var key = _keyring.GetKey();
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[blob.Length - NonceSize - TagSize];
        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(blob, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(blob, NonceSize + TagSize, cipher, 0, cipher.Length);

        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
