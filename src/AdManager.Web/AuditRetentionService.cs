using AdManager.Application;
using AdManager.Application.Abstractions;

namespace AdManager.Web;

/// <summary>
/// Retention аудита (ADR-план): раз в сутки удаляет записи старше AuditRetentionDays
/// (настройка в Settings, по умолчанию 365; 0 = не чистить).
/// </summary>
public sealed class AuditRetentionService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditRetentionService> _log;

    public AuditRetentionService(IServiceScopeFactory scopeFactory, ILogger<AuditRetentionService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // небольшая задержка на старте, чтобы не конкурировать с инициализацией
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                var settings = await sp.GetRequiredService<ISettingsStore>().LoadAsync(stoppingToken);
                var days = settings.AuditRetentionDays;
                if (days > 0)
                {
                    var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
                    var removed = await sp.GetRequiredService<IAuditLog>().PurgeOlderThanAsync(cutoff, stoppingToken);
                    if (removed > 0) _log.LogInformation("Audit retention: purged {N} entries older than {Days} days.", removed, days);
                }
            }
            catch (Exception ex) { _log.LogError(ex, "Audit retention purge failed"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
