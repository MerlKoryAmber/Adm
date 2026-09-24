namespace AdManager.Infrastructure.Data;

/// <summary>Строка-документ состояния приложения: Key (rbac/settings/automation/templates) → JSON-тело.
/// Модель хранилища по ADR-0006 (JSON-колонки): конфиги грузятся целиком, реляционная нормализация не нужна.</summary>
public sealed class AppStateEntry
{
    public string Key { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
