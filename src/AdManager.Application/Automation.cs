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

/// <summary>Известные типы автоматизаций (по образцу ADManager, без апрувов).</summary>
public static class AutomationTaskTypes
{
    /// <summary>Отключить неактивных пользователей. Параметры: ou, days.</summary>
    public const string DisableInactiveUsers = "DisableInactiveUsers";

    /// <summary>Ничего не делает (для проверки планировщика).</summary>
    public const string Noop = "Noop";

    public static readonly string[] All = { DisableInactiveUsers, Noop };
}
