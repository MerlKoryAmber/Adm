using AdManager.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdManager.Infrastructure.Automation;

/// <summary>
/// Фоновый напоминатель об истечении пароля: раз в час проверяет, наступил ли
/// заданный час прогона (МСК) и не гоняли ли уже сегодня; если политика включена —
/// рассылает письма пользователям с приближающимся истечением пароля.
/// </summary>
public sealed class PasswordExpiryNotifier : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<PasswordExpiryNotifier> _log;

    public PasswordExpiryNotifier(IServiceProvider sp, ILogger<PasswordExpiryNotifier> log)
    {
        _sp = sp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "password-expiry tick failed"); }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
        var settings = await store.LoadAsync(ct);
        var policy = settings.PasswordExpiry;
        if (!policy.Enabled) return;

        var nowMsk = MskTime.Now;
        if (nowMsk.Hour != policy.RunHourMsk) return;                 // не тот час
        if (settings.LastRunUtc is { } last &&
            last.ToUniversalTime().Date == DateTime.UtcNow.Date) return; // уже гоняли сегодня

        await RunAsync(scope.ServiceProvider, settings, store, ct);
    }

    /// <summary>Ручной прогон (кнопка на странице). Возвращает человекочитаемый итог.</summary>
    public async Task<string> RunNowAsync(CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
        var settings = await store.LoadAsync(ct);
        return await RunAsync(scope.ServiceProvider, settings, store, ct);
    }

    private async Task<string> RunAsync(IServiceProvider sp, AppSettings settings, ISettingsStore store, CancellationToken ct)
    {
        var expiry = sp.GetRequiredService<IPasswordExpiryService>();
        var email = sp.GetRequiredService<IEmailSender>();
        var policy = settings.PasswordExpiry;

        // Триггер на каждый порог дней (первый совпавший).
        var byDay = policy.Triggers
            .GroupBy(t => t.DaysBefore)
            .ToDictionary(g => g.Key, g => g.First());
        var maxDays = byDay.Count > 0 ? byDay.Keys.Max() : 0;
        var candidates = await expiry.ListExpiringAsync(maxDays, ct);

        int sent = 0, skipped = 0, failed = 0;
        foreach (var u in candidates)
        {
            if (!byDay.TryGetValue(u.DaysLeft, out var trigger)) continue; // шлём только в дни-триггеры
            if (string.IsNullOrWhiteSpace(u.Mail)) { skipped++; continue; }

            var subject = Fill(trigger.Subject, u);
            var body = Fill(trigger.Body, u);
            var r = await email.SendAsync(u.Mail!, subject, body, ct);
            if (r.Success) sent++; else { failed++; _log.LogWarning("expiry mail to {Mail} failed: {Msg}", u.Mail, r.Message); }
        }

        var result = $"{MskTime.Now:yyyy-MM-dd HH:mm} MSK — sent {sent}, skipped(no email) {skipped}, failed {failed} (of {candidates.Count} expiring).";
        settings.LastRunUtc = DateTime.UtcNow;
        settings.LastRunResult = result;
        await store.SaveAsync(settings, ct);
        _log.LogInformation("password-expiry run: {Result}", result);
        return result;
    }

    private static string Fill(string template, ExpiringUser u) => template
        .Replace("{name}", u.DisplayName)
        .Replace("{days}", u.DaysLeft.ToString())
        .Replace("{date}", u.ExpiresUtc.ToString("yyyy-MM-dd"));
}
