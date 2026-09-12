namespace AdManager.Application;

/// <summary>Страна для селектора Country/region: ISO2 (c), название (co), числовой код (countryCode).</summary>
public sealed record Country(string Iso2, string Name, int Numeric);

/// <summary>Компактный справочник стран (ADUC связывает c/co/countryCode одним выбором).</summary>
public static class Countries
{
    public static readonly IReadOnlyList<Country> All = new List<Country>
    {
        new("", "(not set)", 0),
        new("AU", "Australia", 36), new("AT", "Austria", 40), new("BE", "Belgium", 56),
        new("BR", "Brazil", 76), new("CA", "Canada", 124), new("CN", "China", 156),
        new("CZ", "Czech Republic", 203), new("DK", "Denmark", 208), new("FI", "Finland", 246),
        new("FR", "France", 250), new("DE", "Germany", 276), new("GR", "Greece", 300),
        new("HK", "Hong Kong", 344), new("HU", "Hungary", 348), new("IN", "India", 356),
        new("IE", "Ireland", 372), new("IL", "Israel", 376), new("IT", "Italy", 380),
        new("JP", "Japan", 392), new("KZ", "Kazakhstan", 398), new("KR", "Korea, Republic of", 410),
        new("LV", "Latvia", 428), new("LT", "Lithuania", 440), new("LU", "Luxembourg", 442),
        new("MX", "Mexico", 484), new("NL", "Netherlands", 528), new("NZ", "New Zealand", 554),
        new("NO", "Norway", 578), new("PL", "Poland", 616), new("PT", "Portugal", 620),
        new("RO", "Romania", 642), new("RU", "Russian Federation", 643), new("SA", "Saudi Arabia", 682),
        new("RS", "Serbia", 688), new("SG", "Singapore", 702), new("SK", "Slovakia", 703),
        new("SI", "Slovenia", 705), new("ZA", "South Africa", 710), new("ES", "Spain", 724),
        new("SE", "Sweden", 752), new("CH", "Switzerland", 756), new("TR", "Türkiye", 792),
        new("UA", "Ukraine", 804), new("AE", "United Arab Emirates", 784),
        new("GB", "United Kingdom", 826), new("US", "United States", 840),
    };

    public static Country? ByIso2(string? iso2) =>
        string.IsNullOrEmpty(iso2) ? null : All.FirstOrDefault(c => string.Equals(c.Iso2, iso2, StringComparison.OrdinalIgnoreCase));
}
