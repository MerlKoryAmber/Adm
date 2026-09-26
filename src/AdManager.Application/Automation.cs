using AdManager.Application.Abstractions;

namespace AdManager.Application;

/// <summary>Данные автоматизаций (определения + время последнего запуска).</summary>
public sealed class AutomationData
{
    public List<AutomationDefinition> Definitions { get; set; } = new();
    public Dictionary<string, DateTime> LastRunUtc { get; set; } = new();
}

/// <summary>Хранилище автоматизаций.</summary>
public interface IAutomationStore
{
    Task<AutomationData> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AutomationData data, CancellationToken ct = default);
}

/// <summary>Тип расписания (человекочитаемый, НЕ cron — эталон ADManager Plus).</summary>
public enum ScheduleKind { Hourly, Daily, Weekly, Monthly, Once }

/// <summary>
/// Расписание запуска. Поля используются в зависимости от Kind:
///  Hourly  — EveryHours (каждые N часов);
///  Daily   — Hour:Minute (ежедневно в это время МСК);
///  Weekly  — DayOfWeek + Hour:Minute;
///  Monthly — DayOfMonth + Hour:Minute;
///  Once    — RunAtMsk (однократно в дату/время).
/// </summary>
public sealed class AutomationSchedule
{
    public ScheduleKind Kind { get; set; } = ScheduleKind.Daily;
    public int EveryHours { get; set; } = 8;
    public int Hour { get; set; } = 0;
    public int Minute { get; set; } = 0;
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
    public int DayOfMonth { get; set; } = 1;
    public DateTime? RunAtMsk { get; set; }

    /// <summary>Человекочитаемая сводка (колонка Time Summary в списке).</summary>
    public string Summary() => Kind switch
    {
        ScheduleKind.Hourly  => $"For Each {EveryHours} hour",
        ScheduleKind.Daily   => $"Daily {Hour} : {Minute:00}",
        ScheduleKind.Weekly  => $"Weekly {DayOfWeek} {Hour} : {Minute:00}",
        ScheduleKind.Monthly => $"Monthly on {DayOfMonth} at {Hour} : {Minute:00}",
        ScheduleKind.Once    => RunAtMsk is { } d ? $"Once: {d:yyyy-MM-dd} at {d:HH:mm}" : "Once",
        _ => "",
    };
}

/// <summary>Задачи автоматизации (по образцу ADManager Plus; отмеченный набор).</summary>
public static class AutomationTaskTypes
{
    public const string AddToGroup = "AddToGroup";
    public const string RemoveFromGroup = "RemoveFromGroup";
    public const string DisableUsers = "DisableUsers";
    public const string EnableUsers = "EnableUsers";
    public const string MoveUsers = "MoveUsers";
    public const string UnlockUsers = "UnlockUsers";
    public const string HideFromAddressLists = "HideFromAddressLists";

    /// <summary>Человекочитаемое имя задачи (колонка Request Type).</summary>
    public static string Label(string type) => type switch
    {
        AddToGroup => "Add To Group",
        RemoveFromGroup => "Remove from Group",
        DisableUsers => "Disable users",
        EnableUsers => "Enable users",
        MoveUsers => "Move Users",
        UnlockUsers => "Unlock Users",
        HideFromAddressLists => "Hide from Exchange address lists",
        _ => type,
    };

    public static readonly (string Type, string Label)[] All =
    {
        (AddToGroup, "Add To Group"),
        (RemoveFromGroup, "Remove from Group"),
        (DisableUsers, "Disable users"),
        (EnableUsers, "Enable users"),
        (MoveUsers, "Move Users"),
        (UnlockUsers, "Unlock Users"),
        (HideFromAddressLists, "Hide from Exchange address lists"),
    };
}
