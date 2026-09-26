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

/// <summary>Оператор фильтра Refine Result (эталон ADManager Plus).</summary>
public enum RefineOperator { Is, Contains, IsNot, NotContains }

/// <summary>Условие уточнения результата отчёта (Refine Result). Field — имя колонки
/// отчёта или спец-поле "OU Name". Применяется поверх строк отчёта в автоматизации.</summary>
public sealed class RefineCondition
{
    public string Field { get; set; } = "";
    public RefineOperator Operator { get; set; } = RefineOperator.Is;
    public string Value { get; set; } = "";

    public bool Matches(string? cell)
    {
        var c = cell ?? "";
        var v = Value ?? "";
        return Operator switch
        {
            RefineOperator.Is          => string.Equals(c, v, StringComparison.OrdinalIgnoreCase),
            RefineOperator.IsNot       => !string.Equals(c, v, StringComparison.OrdinalIgnoreCase),
            RefineOperator.Contains    => c.Contains(v, StringComparison.OrdinalIgnoreCase),
            RefineOperator.NotContains => !c.Contains(v, StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }
}

/// <summary>Спец-поле Refine, вычисляемое из DN (не колонка отчёта).</summary>
public static class RefineFields
{
    public const string OuName = "OU Name";

    /// <summary>Имя непосредственной родительской OU из DN ("…,OU=Sales,DC=…" → "Sales").</summary>
    public static string OuNameOf(string dn)
    {
        foreach (var part in dn.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase)) return p.Substring(3);
        }
        return "";
    }
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
