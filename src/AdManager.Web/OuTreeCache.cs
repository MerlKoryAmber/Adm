using AdManager.Application.Abstractions;

namespace AdManager.Web;

/// <summary>
/// In-memory кэш полного дерева OU (singleton). Строит дерево через IAdDirectory,
/// фильтрует системные контейнеры, отдаёт из памяти. Обновляется по расписанию
/// (OuTreeRefresher) и по требованию (RefreshAsync). При пустом кэше — грузит по запросу.
/// </summary>
public sealed class OuTreeCache : IOuTreeProvider
{
    // Системные OU-имена (RDN, регистронезависимо), куда делегировать scope нельзя.
    // Контейнеры Builtin/Computers/Users/… — не OU (class=container), в дерево OU и так не попадают;
    // здесь — well-known OU, главный из которых Domain Controllers.
    private static readonly HashSet<string> SystemOuNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Domain Controllers",
        "Microsoft Exchange Security Groups",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _baseDn;
    private readonly ILogger<OuTreeCache> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile IReadOnlyList<AdOuNode>? _tree;

    public OuTreeCache(IServiceScopeFactory scopeFactory, string baseDn, ILogger<OuTreeCache> log)
    {
        _scopeFactory = scopeFactory;
        _baseDn = baseDn;
        _log = log;
    }

    public async Task<IReadOnlyList<AdOuNode>> GetTreeAsync(CancellationToken ct = default)
    {
        var cached = _tree;
        if (cached != null) return cached;
        await RefreshAsync(ct);
        return _tree ?? Array.Empty<AdOuNode>();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dir = scope.ServiceProvider.GetRequiredService<IAdDirectory>();
            var all = await dir.ListAllOusAsync(_baseDn, ct);
            _tree = Filter(all);
            _log.LogInformation("OU tree cache refreshed: {Count} OUs (after system filter).", _tree.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OU tree cache refresh failed");
        }
        finally { _lock.Release(); }
    }

    /// <summary>Убрать системные OU и их поддеревья.</summary>
    private static IReadOnlyList<AdOuNode> Filter(IReadOnlyList<AdOuNode> all)
    {
        var systemDns = all
            .Where(o => SystemOuNames.Contains(o.Name))
            .Select(o => o.Dn)
            .ToList();

        bool IsUnderSystem(string dn) =>
            systemDns.Any(sd => dn.Equals(sd, StringComparison.OrdinalIgnoreCase)
                                || dn.EndsWith("," + sd, StringComparison.OrdinalIgnoreCase));

        return all.Where(o => !IsUnderSystem(o.Dn)).ToList();
    }
}

/// <summary>Фоновое обновление кэша дерева OU раз в час (расписание + on-demand).</summary>
public sealed class OuTreeRefresher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly IOuTreeProvider _provider;
    private readonly ILogger<OuTreeRefresher> _log;

    public OuTreeRefresher(IOuTreeProvider provider, ILogger<OuTreeRefresher> log)
    {
        _provider = provider;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _provider.RefreshAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "OU tree scheduled refresh failed"); }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
