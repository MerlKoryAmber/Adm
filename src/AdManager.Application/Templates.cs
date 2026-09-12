namespace AdManager.Application;

/// <summary>Поле каталога (для раскладки в шаблоне). Key — ldap-атрибут или спец-контрол (__...).</summary>
public sealed record FieldDef(string Key, string Label, string Category);

/// <summary>Каталог доступных полей, сгруппированных по категориям (как Field Tray в ADManager).</summary>
public static class FieldCatalog
{
    public static readonly IReadOnlyList<FieldDef> All = new List<FieldDef>
    {
        // General
        new("givenName", "First name", "General"),
        new("initials", "Initials", "General"),
        new("sn", "Last name", "General"),
        new("displayName", "Display name", "General"),
        new("description", "Description", "General"),
        new("physicalDeliveryOfficeName", "Office", "General"),
        new("telephoneNumber", "Telephone", "General"),
        new("mail", "Email", "General"),
        new("wWWHomePage", "Web page", "General"),
        new("employeeID", "Employee ID", "General"),
        // Account
        new("sAMAccountName", "Logon name", "Account"),
        new("userPrincipalName", "User principal name", "Account"),
        new("__password", "Password", "Account"),
        new("__enabled", "Enabled", "Account"),
        new("__mustChange", "Must change password", "Account"),
        new("__pwdNeverExpires", "Password never expires", "Account"),
        // Address
        new("streetAddress", "Street", "Address"),
        new("postOfficeBox", "P.O. Box", "Address"),
        new("l", "City", "Address"),
        new("st", "State/Province", "Address"),
        new("postalCode", "Zip/Postal code", "Address"),
        new("co", "Country/Region", "Address"),
        new("c", "Country code (ISO2)", "Address"),
        // Telephones
        new("homePhone", "Home phone", "Telephones"),
        new("pager", "Pager", "Telephones"),
        new("mobile", "Mobile", "Telephones"),
        new("facsimileTelephoneNumber", "Fax", "Telephones"),
        new("ipPhone", "IP phone", "Telephones"),
        new("info", "Notes", "Telephones"),
        // Organization
        new("title", "Title", "Organization"),
        new("department", "Department", "Organization"),
        new("company", "Company", "Organization"),
        new("manager", "Manager", "Organization"),
        new("employeeNumber", "Employee number", "Organization"),
        new("employeeType", "Employee type", "Organization"),
        new("division", "Division", "Organization"),
        // Exchange (только одно-значные атрибуты — proxyAddresses multi-valued, не поддерживаем в текстовой раскладке)
        new("mailNickname", "Exchange alias", "Exchange"),
        new("targetAddress", "External email (targetAddress)", "Exchange"),
        // Profile
        new("profilePath", "Profile path", "Profile"),
        new("scriptPath", "Logon script", "Profile"),
        new("homeDirectory", "Home folder", "Profile"),
        new("homeDrive", "Home drive", "Profile"),
        new("userWorkstations", "Log on to (workstations)", "Profile"),
    };

    public static readonly IReadOnlyList<string> Categories =
        All.Select(f => f.Category).Distinct().ToList();

    public static string Label(string key) => All.FirstOrDefault(f => f.Key == key)?.Label ?? key;

    /// <summary>Спец-контролы (не текстовые ldap-атрибуты).</summary>
    public static bool IsSpecial(string key) => key.StartsWith("__", StringComparison.Ordinal) || key == "sAMAccountName";

    /// <summary>Булевы спец-контролы (чекбоксы).</summary>
    public static bool IsBool(string key) => key is "__enabled" or "__mustChange" or "__pwdNeverExpires";

    /// <summary>Поля, к которым применимо авто-именование (из имени/фамилии).</summary>
    public static bool SupportsNaming(string key) => key is "sAMAccountName" or "userPrincipalName" or "displayName";
}

/// <summary>Правила авто-именования (logon/UPN/display из givenName+sn), как в ADManager.</summary>
public static class NamingRules
{
    public static readonly (string Value, string Label)[] Options =
    {
        ("", "(manual)"),
        ("firstlast", "First+Last — JohnSmith"),
        ("firstlast_lower", "firstlast — johnsmith"),
        ("first.last", "first.last — john.smith"),
        ("flast", "f+Last — jsmith"),
        ("first_last_space", "First Last — John Smith"),
        ("first", "First — John"),
    };

    public static string? Apply(string? rule, string? first, string? last)
    {
        if (string.IsNullOrEmpty(rule)) return null;
        first = (first ?? "").Trim();
        last = (last ?? "").Trim();
        var s = rule switch
        {
            "firstlast" => first + last,
            "firstlast_lower" => (first + last).ToLowerInvariant(),
            "first.last" => string.Join(".", new[] { first, last }.Where(x => x.Length > 0)),
            "flast" => (first.Length > 0 ? first[..1] : "") + last,
            "first_last_space" => string.Join(" ", new[] { first, last }.Where(x => x.Length > 0)),
            "first" => first,
            _ => "",
        };
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}

/// <summary>Поле в шаблоне: ключ + предзаполнение/обязательность/правило именования.</summary>
public sealed class TemplateField
{
    public string Key { get; set; } = "";
    public string? Default { get; set; }   // предзаполнение (для булевых: "true"/"false")
    public bool Required { get; set; }
    public string? Naming { get; set; }     // правило NamingRules для sAMAccountName/userPrincipalName/displayName
}

public sealed class TemplateTab
{
    public string Title { get; set; } = "";
    public List<TemplateField> Fields { get; set; } = new();
}

/// <summary>Шаблон формы пользователя: набор вкладок и полей в них (для create/modify).</summary>
public sealed class UserTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "Create"; // Create | Modify
    public List<TemplateTab> Tabs { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Дефолтные раскладки формы, когда шаблон не выбран (совпадают со статическими вкладками).</summary>
public static class TemplateDefaults
{
    private static TemplateTab Tab(string title, params string[] keys) =>
        new() { Title = title, Fields = keys.Select(k => new TemplateField { Key = k }).ToList() };

    public static List<TemplateTab> Create() => new()
    {
        Tab("General", "givenName", "initials", "sn", "displayName", "description", "physicalDeliveryOfficeName", "telephoneNumber", "mail", "wWWHomePage"),
        Tab("Account", "sAMAccountName", "userPrincipalName", "__password", "__enabled", "__mustChange"),
        Tab("Address", "streetAddress", "postOfficeBox", "l", "st", "postalCode", "co", "c"),
        Tab("Telephones", "homePhone", "pager", "mobile", "facsimileTelephoneNumber", "ipPhone", "info"),
        Tab("Organization", "title", "department", "company", "manager"),
        Tab("Profile", "profilePath", "scriptPath", "homeDirectory", "homeDrive"),
    };

    public static List<TemplateTab> Modify() => new()
    {
        Tab("General", "givenName", "initials", "sn", "displayName", "description", "physicalDeliveryOfficeName", "telephoneNumber", "mail", "wWWHomePage"),
        Tab("Account", "userPrincipalName", "__enabled", "__pwdNeverExpires", "__mustChange", "__password"),
        Tab("Address", "streetAddress", "postOfficeBox", "l", "st", "postalCode", "co", "c"),
        Tab("Telephones", "homePhone", "pager", "mobile", "facsimileTelephoneNumber", "ipPhone", "info"),
        Tab("Organization", "title", "department", "company", "manager"),
        Tab("Profile", "profilePath", "scriptPath", "homeDirectory", "homeDrive"),
    };
}

public interface IUserTemplateStore
{
    Task<List<UserTemplate>> ListAsync(CancellationToken ct = default);
    Task<UserTemplate?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(UserTemplate template, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
