namespace AdManager.Application;

/// <summary>
/// Дополнительный (custom) AD-атрибут, добавленный администратором в формы/шаблоны.
/// Атрибут ДОЛЖЕН существовать в схеме AD (валидируется при создании).
/// Появляется в Field Tray шаблонов и в FieldPicker ролей (категория Custom Attributes).
/// </summary>
public sealed class CustomAttribute
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>LDAP-имя атрибута (ключ поля; совпадает с TemplateField.Key).</summary>
    public string LdapName { get; set; } = "";
    /// <summary>Человекочитаемая подпись в UI (если пусто — показываем LdapName).</summary>
    public string DisplayName { get; set; } = "";
    public string? DefaultValue { get; set; }
    /// <summary>Текст подсказки под полем (Help Card).</summary>
    public string? HelpCard { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Подпись для UI.</summary>
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? LdapName : DisplayName;
}

/// <summary>Каталог custom-атрибутов (глобальный, в БД).</summary>
public interface ICustomAttributeStore
{
    Task<IReadOnlyList<CustomAttribute>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(CustomAttribute attribute, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
