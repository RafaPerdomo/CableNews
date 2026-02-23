namespace CableNews.Application.Common.Models;

using CableNews.Domain.Entities;

public class CategoryConfig
{
    public string Name { get; init; } = string.Empty;
    public List<string> Keywords { get; init; } = [];
}

public class NewsAgentConfig
{
    public string BaseQueryTemplate { get; init; } = string.Empty;
    public string DefaultLanguage { get; init; } = "es";
    public string Timezone { get; init; } = "America/Bogota";
    public int LookbackHours { get; init; } = 24;
    public int MaxArticlesPerCountry { get; init; } = 120;
    
    public List<CategoryConfig> Categories { get; init; } = [];
    public List<CountryConfig> Countries { get; init; } = [];
}
