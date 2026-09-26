using AdManager.Application;
using AdManager.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdManager.Infrastructure.Automation;

/// <summary>
/// Планировщик автоматизаций (эталон ADManager Plus, без cron): BackgroundService тикает
/// раз в минуту, запускает due-задачи по человекочитаемому расписанию (МСК). Объекты берёт
/// из отчёта (IReportService), применяет задачу к каждому. Операции идут через
/// AdManagementService → RBAC → аудит (актор = Automation: &lt;имя&gt;).
/// </summary>
public sealed class AutomationScheduler : BackgroundService, IAutomationScheduler
{
    private readonly IAutomationStore _store;
    private readonly IServiceProvider _sp;
    private readonly RbacOptions _rbac;
    private readonly ILogger<AutomationScheduler> _log;

    public AutomationScheduler(IAutomationStore store, IServiceProvider sp, RbacOptions rbac, ILogger<AutomationScheduler> log)
    {
        _store = store; _sp = sp; _rbac = rbac; _log = log;
    }

    public async Task RegisterAsync(AutomationDefinition definition, CancellationToken ct = default)
    {
        var data = await _store.LoadAsync(ct);
        data.Definitions.RemoveAll(d => d.Id == definition.Id);
        definition.ModifiedUtc = DateTime.UtcNow;
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
        var result = await RunAsync(def, ct);
        def.LastRunMsk = NowMsk; def.LastRunResult = result;
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
        var now = NowMsk;
        var changed = false;

        foreach (var def in data.Definitions.Where(d => d.Enabled))
        {
            if (!IsDue(def, now)) continue;
            var result = await RunAsync(def, ct);
            def.LastRunMsk = now; def.LastRunResult = result;
            changed = true;
        }

        if (changed) await _store.SaveAsync(data, ct);
    }

    /// <summary>Пора ли запускать по расписанию (относительно LastRunMsk).</summary>
    internal static bool IsDue(AutomationDefinition def, DateTime now)
    {
        var s = def.Schedule;
        var last = def.LastRunMsk;
        switch (s.Kind)
        {
            case ScheduleKind.Hourly:
                var every = Math.Max(1, s.EveryHours);
                return last is null || (now - last.Value).TotalHours >= every;

            case ScheduleKind.Daily:
                return DueAtTime(now, last, d => d.Date.AddHours(s.Hour).AddMinutes(s.Minute));

            case ScheduleKind.Weekly:
                if (now.DayOfWeek != s.DayOfWeek) return false;
                return DueAtTime(now, last, d => d.Date.AddHours(s.Hour).AddMinutes(s.Minute));

            case ScheduleKind.Monthly:
                if (now.Day != s.DayOfMonth) return false;
                return DueAtTime(now, last, d => d.Date.AddHours(s.Hour).AddMinutes(s.Minute));

            case ScheduleKind.Once:
                return s.RunAtMsk is { } at && now >= at && last is null;

            default: return false;
        }
    }

    /// <summary>Сегодняшнее целевое время наступило и ещё не запускали сегодня после него.</summary>
    private static bool DueAtTime(DateTime now, DateTime? last, Func<DateTime, DateTime> targetOf)
    {
        var target = targetOf(now);
        if (now < target) return false;
        return last is null || last.Value < target;
    }

    private async Task<string> RunAsync(AutomationDefinition def, CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var sp = scope.ServiceProvider;
        var mgmt = sp.GetRequiredService<AdManagementService>();
        var reports = sp.GetRequiredService<IReportService>();
        var actor = new TechnicianContext(_rbac.AutomationSid, "automation", $"Automation: {def.Name}");

        // 1) объекты из отчёта-источника
        if (string.IsNullOrEmpty(def.SourceReportKey))
            return "No source report configured";
        var report = await reports.RunAsync(def.SourceReportKey, ct);
        var targets = report.Rows.Select(r => r.Dn).ToList();
        if (targets.Count == 0) return "0 objects (report empty)";

        // 2) задача к каждому объекту
        int ok = 0, fail = 0;
        foreach (var dn in targets)
        {
            var r = await ApplyTaskAsync(def, mgmt, sp, actor, dn, ct);
            if (r) ok++; else fail++;
        }
        var summary = $"{ok} ok, {fail} failed of {targets.Count}";
        _log.LogInformation("automation '{Name}' ({Task}): {Summary}", def.Name, def.TaskType, summary);
        return summary;
    }

    private async Task<bool> ApplyTaskAsync(AutomationDefinition def, AdManagementService mgmt, IServiceProvider sp, TechnicianContext actor, string dn, CancellationToken ct)
    {
        try
        {
            switch (def.TaskType)
            {
                case AutomationTaskTypes.AddToGroup:
                {
                    var group = def.Parameters.GetValueOrDefault("groupDn");
                    if (string.IsNullOrEmpty(group)) return false;
                    var r = await mgmt.ManageGroupMembershipAsync(actor, group, new[] { dn }, Array.Empty<string>(), ct);
                    return r.Success;
                }
                case AutomationTaskTypes.RemoveFromGroup:
                {
                    var group = def.Parameters.GetValueOrDefault("groupDn");
                    if (string.IsNullOrEmpty(group)) return false;
                    var r = await mgmt.ManageGroupMembershipAsync(actor, group, Array.Empty<string>(), new[] { dn }, ct);
                    return r.Success;
                }
                case AutomationTaskTypes.DisableUsers:
                    return (await mgmt.SetEnabledAsync(actor, dn, false, ct)).Success;
                case AutomationTaskTypes.EnableUsers:
                    return (await mgmt.SetEnabledAsync(actor, dn, true, ct)).Success;
                case AutomationTaskTypes.MoveUsers:
                {
                    var ou = def.Parameters.GetValueOrDefault("targetOu");
                    if (string.IsNullOrEmpty(ou)) return false;
                    return (await mgmt.MoveAsync(actor, dn, ou, ct)).Success;
                }
                case AutomationTaskTypes.UnlockUsers:
                    return (await mgmt.UnlockAsync(actor, dn, ct)).Success;
                case AutomationTaskTypes.HideFromAddressLists:
                {
                    var ex = sp.GetService<ExchangeManagementService>();
                    if (ex is null) return false;
                    var r = await ex.SetMailboxPropertiesAsync(actor, dn, new MailboxProperties(HiddenFromAddressLists: true), ct);
                    return r.Success;
                }
                default:
                    _log.LogWarning("automation '{Name}': unknown task {Task}", def.Name, def.TaskType);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "automation '{Name}' task failed on {Dn}", def.Name, dn);
            return false;
        }
    }

    private static DateTime NowMsk => DateTime.UtcNow.AddHours(3);
}
