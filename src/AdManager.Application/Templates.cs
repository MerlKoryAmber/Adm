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
        // Profile
        new("profilePath", "Profile path", "Profile"),
        new("scriptPath", "Logon script", "Profile"),
        new("homeDirectory", "Home folder", "Profile"),
        new("homeDrive", "Home drive", "Profile"),
    };

    public static readonly IReadOnlyList<string> Categories =
        All.Select(f => f.Category).Distinct().ToList();

    public static string Label(string key) => All.FirstOrDefault(f => f.Key == key)?.Label ?? key;
}

public sealed class TemplateTab
{
    public string Title { get; set; } = "";
    public List<string> Fields { get; set; } = new();
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

public interface IUserTemplateStore
{
    Task<List<UserTemplate>> ListAsync(CancellationToken ct = default);
    Task<UserTemplate?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(UserTemplate template, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
