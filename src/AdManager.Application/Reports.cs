using AdManager.Application.Abstractions;

namespace AdManager.Application;

/// <summary>Тип объекта отчёта (влияет на источник данных и колонки).</summary>
public enum ReportObjectType { User, Group, Contact, Ou }

/// <summary>
/// Декларативное описание отчёта. Добавить отчёт = одна строка в каталоге.
/// Один и тот же ReportDef используют и UI (таблица+CSV), и автоматизации
/// (берут объекты отчёта как источник для действия).
/// </summary>
public sealed record ReportDef(
    string Key,
    string Category,      // "User" | "Password" | "Group" | "Contact & OU"
    string Group,         // подгруппа в категории: "General", "Account Status", "Logon", …
    string Title,
    string Hint,
    ReportObjectType ObjectType);

/// <summary>Строка отчёта: DN объекта + значения по колонкам (динамические).</summary>
public sealed record ReportRow(string Dn, IReadOnlyList<string> Cells);

/// <summary>Результат отчёта: заголовки колонок + строки.</summary>
public sealed record ReportResult(IReadOnlyList<string> Columns, IReadOnlyList<ReportRow> Rows);

/// <summary>
/// Движок отчётов. Единая точка: UI и автоматизации зовут RunAsync.
/// Каталог статический; фильтрация/колонки определяются по ключу отчёта.
/// </summary>
public interface IReportService
{
    IReadOnlyList<ReportDef> Catalog { get; }
    ReportDef? Find(string key);
    Task<ReportResult> RunAsync(string key, CancellationToken ct = default);
}
