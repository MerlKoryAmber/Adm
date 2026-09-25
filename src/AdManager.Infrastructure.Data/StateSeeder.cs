using AdManager.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AdManager.Infrastructure.Data;

/// <summary>
/// Одноразовый импорт состояния из файловых сторов (App_Data/*.json) в БД (ADR-0006).
/// Срабатывает только когда целевой EF-стор ПУСТ и на диске есть непустой файл —
/// идемпотентно: повторный старт с уже наполненной БД ничего не делает.
/// Settings идут через зарегистрированный ISettingsStore (декоратор) — секреты
/// при импорте шифруются envelope.
/// </summary>
public static class StateSeeder
{
    public static async Task SeedFromFilesAsync(IServiceProvider services, string appDataDir, ILogger logger, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        await SeedRbacAsync(sp, appDataDir, logger, ct);
        await SeedAutomationAsync(sp, appDataDir, logger, ct);
        await SeedTemplatesAsync(sp, appDataDir, logger, ct);
        await SeedSettingsAsync(sp, appDataDir, logger, ct);
    }

    private static bool FileHasContent(string path)
        => File.Exists(path) && new FileInfo(path).Length > 2; // "{}" и пустой — не считаем

    private static async Task SeedRbacAsync(IServiceProvider sp, string dir, ILogger log, CancellationToken ct)
    {
        var ef = sp.GetService<IRbacStore>();
        if (ef is not EfRbacStore) return; // не Ef-режим
        var path = Path.Combine(dir, "rbac.json");
        if (!FileHasContent(path)) return;

        var current = await ef.LoadAsync(ct);
        if (current.Roles.Count > 0 || current.Scopes.Count > 0 || current.Assignments.Count > 0) return; // БД не пуста

        var file = new FileRbacStore(path);
        var data = await file.LoadAsync(ct);
        if (data.Roles.Count == 0 && data.Scopes.Count == 0 && data.Assignments.Count == 0) return;

        await ef.SaveAsync(data, ct);
        log.LogInformation("Seeded RBAC from {Path}: {Roles} roles, {Scopes} scopes, {Assignments} assignments.",
            path, data.Roles.Count, data.Scopes.Count, data.Assignments.Count);
    }

    private static async Task SeedAutomationAsync(IServiceProvider sp, string dir, ILogger log, CancellationToken ct)
    {
        var ef = sp.GetService<IAutomationStore>();
        if (ef is not EfAutomationStore) return;
        var path = Path.Combine(dir, "automation.json");
        if (!FileHasContent(path)) return;

        var current = await ef.LoadAsync(ct);
        if (current.Definitions.Count > 0) return;

        var file = new FileAutomationStore(path);
        var data = await file.LoadAsync(ct);
        if (data.Definitions.Count == 0) return;

        await ef.SaveAsync(data, ct);
        log.LogInformation("Seeded Automation from {Path}: {N} definitions.", path, data.Definitions.Count);
    }

    private static async Task SeedTemplatesAsync(IServiceProvider sp, string dir, ILogger log, CancellationToken ct)
    {
        var ef = sp.GetService<IUserTemplateStore>();
        if (ef is not EfUserTemplateStore) return;
        var path = Path.Combine(dir, "user-templates.json");
        if (!FileHasContent(path)) return;

        var current = await ef.ListAsync(ct);
        if (current.Count > 0) return;

        var file = new FileUserTemplateStore(path);
        var items = await file.ListAsync(ct);
        if (items.Count == 0) return;

        foreach (var t in items) await ef.SaveAsync(t, ct);
        log.LogInformation("Seeded Templates from {Path}: {N} templates.", path, items.Count);
    }

    private static async Task SeedSettingsAsync(IServiceProvider sp, string dir, ILogger log, CancellationToken ct)
    {
        // ISettingsStore зарегистрирован как EncryptedSettingsStore(EfSettingsStore).
        // Импортируем только в Ef-режиме и только если БД-настройки пусты (дефолт).
        var store = sp.GetService<ISettingsStore>();
        if (store is not EncryptedSettingsStore) return;
        var path = Path.Combine(dir, "app-settings.json");
        if (!FileHasContent(path)) return;

        var current = await store.LoadAsync(ct);
        if (IsSettingsConfigured(current)) return; // уже что-то есть в БД

        var file = new FileSettingsStore(path);
        var data = await file.LoadAsync(ct);
        if (!IsSettingsConfigured(data)) return;

        await store.SaveAsync(data, ct); // декоратор зашифрует секреты
        log.LogInformation("Seeded Settings from {Path} (secrets encrypted).", path);
    }

    /// <summary>Считаем настройки заданными, если сконфигурирован хоть один блок.</summary>
    private static bool IsSettingsConfigured(AppSettings s)
        => s.Smtp.IsConfigured
           || !string.IsNullOrWhiteSpace(s.Https.PfxPath)
           || !string.IsNullOrWhiteSpace(s.Https.Thumbprint)
           || s.Https.RequireHttps
           || s.OperationalCredential.IsStored
           || s.PasswordExpiry.Enabled;
}
