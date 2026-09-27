using AdManager.Application;
using AdManager.Application.Abstractions;

namespace AdManager.Web;

/// <summary>
/// In-memory кэш данных Exchange (singleton): список почтовых баз.
/// Опрос Exchange дорогой (remote PowerShell), поэтому список баз тянется по расписанию
/// (ExchangeDataRefresher, интервал из Settings) и по требованию (RefreshDatabasesAsync).
/// Пустой список, пока не прогрет / Exchange недоступен.
/// </summary>
public sealed class ExchangeDataCache : IExchangeDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExchangeDataCache> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile IReadOnlyList<MailboxDatabase> _databases = Array.Empty<MailboxDatabase>();

    public ExchangeDataCache(IServiceScopeFactory scopeFactory, ILogger<ExchangeDataCache> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public IReadOnlyList<MailboxDatabase> Databases => _databases;
    public DateTime? DatabasesRefreshedUtc { get; private set; }

    public async Task RefreshDatabasesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ex = scope.ServiceProvider.GetRequiredService<IExchangeService>();
            var list = await ex.ListDatabasesAsync(ct);
            if (list.Count > 0) _databases = list;      // не затираем кэш пустым при недоступности
            DatabasesRefreshedUtc = DateTime.UtcNow;

            // записать отметку времени в настройки (для отображения в Settings)
            try
            {
                var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
                var s = await store.LoadAsync(ct);
                s.ExchangeCache.DatabasesLastRefreshUtc = DateTimeOffset.UtcNow.UtcDateTime;
                await store.SaveAsync(s, ct);
            }
            catch { /* отметка времени не критична */ }

            _log.LogInformation("Exchange DB list cache refreshed: {Count} databases.", _databases.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Exchange DB list cache refresh failed");
        }
        finally { _lock.Release(); }
    }
}

/// <summary>Фоновое обновление кэша данных Exchange по расписанию из Settings
/// (список баз — раз в ExchangeCache.DatabaseListRefreshHours часов, по умолчанию 24).</summary>
public sealed class ExchangeDataRefresher : BackgroundService
{
    private readonly IExchangeDataCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExchangeDataRefresher> _log;

    public ExchangeDataRefresher(IExchangeDataCache cache, IServiceScopeFactory scopeFactory, ILogger<ExchangeDataRefresher> log)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var hours = 24;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
                var s = await store.LoadAsync(stoppingToken);
                hours = Math.Max(1, s.ExchangeCache.DatabaseListRefreshHours);
            }
            catch { /* дефолт 24ч */ }

            try { await _cache.RefreshDatabasesAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Exchange scheduled refresh failed"); }

            try { await Task.Delay(TimeSpan.FromHours(hours), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
