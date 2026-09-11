using AdManager.Application;
using AdManager.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCrontab;

namespace AdManager.Infrastructure.Automation;

/// <summary>
/// Планировщик автоматизаций: BackgroundService тикает раз в минуту, запускает
/// due-задачи по cron (МСК). Каждая операция идёт через AdManagementService → RBAC → аудит
/// (актор = automation, супер-доступ). Апрув-workflow нет.
/// </summary>
public sealed class AutomationScheduler : BackgroundService, IAutomationScheduler
{
    private readonly IAutomationStore _store;
    private readonly IServiceProvider _sp;
    private readonly RbacOptions _rbac;
    private readonly ILogger<AutomationScheduler> _log;

    public AutomationScheduler(IAutomationStore store, IServiceProvider sp, RbacOptions rbac, ILogger<AutomationScheduler> log)
    {
        _store = store;
        _sp = sp;
        _rbac = rbac;
        _log = log;
    }

    public async Task RegisterAsync(AutomationDefinition definition, CancellationToken ct = default)
    {
        var data = await _store.LoadAsync(ct);
        data.Definitions.RemoveAll(d => d.Id == definition.Id);
        data.Definitions.Add(definition);
        await _store.SaveAsync(data, ct);
    }

    public async Task<IReadOnlyList<AutomationDefinition>> ListAsync(CancellationToken ct = default)
        => (await _store.LoadAsync(ct)).Definitions;

    public async Task TriggerNowAsync(Guid id, CancellationToken ct = default)
    {
        var data = await _store.LoadAsync(ct);
        var def = data.Definitions.FirstOrDefault(d => d.Id == id);
        if (def is null) return;
        await RunAsync(def, ct);
        data = await _store.LoadAsync(ct);
        data.LastRunUtc[id.ToString()] = NowMsk;
        await _store.SaveAsync(data, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "automation tick failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var data = await _store.LoadAsync(ct);
        var nowMsk = NowMsk;
        var changed = false;

        foreach (var def in data.Definitions.Where(d => d.Enabled))
        {
            var sched = CrontabSchedule.TryParse(def.CronMsk);
            if (sched is null) continue;

            var last = data.LastRunUtc.TryGetValue(def.Id.ToString(), out var lr) ? lr : nowMsk.AddMinutes(-1);
            var next = sched.GetNextOccurrence(last);
            if (next <= nowMsk)
            {
                await RunAsync(def, ct);
                data.LastRunUtc[def.Id.ToString()] = nowMsk;
                changed = true;
            }
        }

        if (changed) await _store.SaveAsync(data, ct);
    }

    private async Task RunAsync(AutomationDefinition def, CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<AdManagementService>();
        var dir = scope.ServiceProvider.GetRequiredService<IAdDirectory>();
        var actor = new TechnicianContext(_rbac.AutomationSid, "automation", $"Automation: {def.Name}");

        switch (def.TaskType)
        {
            case AutomationTaskTypes.DisableInactiveUsers:
                await DisableInactiveUsers(def, mgmt, dir, actor, ct);
                break;
            case AutomationTaskTypes.Noop:
                _log.LogInformation("automation '{Name}' noop tick", def.Name);
                break;
            default:
                _log.LogWarning("automation '{Name}': unknown task type {Type}", def.Name, def.TaskType);
                break;
        }
    }

    private static async Task DisableInactiveUsers(AutomationDefinition def, AdManagementService mgmt, IAdDirectory dir, TechnicianContext actor, CancellationToken ct)
    {
        if (!def.Parameters.TryGetValue("ou", out var ou) || string.IsNullOrEmpty(ou)) return;
        var days = def.Parameters.TryGetValue("days", out var d) && int.TryParse(d, out var dd) ? dd : 90;
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var users = await dir.ListUsersAsync(ou, subtree: true, ct);
        foreach (var u in users.Where(u => u.Enabled))
        {
            var det = await dir.GetObjectAsync(u.Dn, new[] { "lastLogonTimestamp" }, ct);
            var raw = det?.Attributes.GetValueOrDefault("lastLogonTimestamp");
            if (string.IsNullOrEmpty(raw) || !long.TryParse(raw, out var ft) || ft <= 0) continue; // не логинился — пропускаем
            var last = DateTime.FromFileTimeUtc(ft);
            if (last < cutoff) await mgmt.SetEnabledAsync(actor, u.Dn, false, ct);
        }
    }

    private static DateTime NowMsk => DateTime.UtcNow.AddHours(3);
}
