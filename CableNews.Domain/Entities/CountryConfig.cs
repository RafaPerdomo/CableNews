namespace CableNews.Domain.Entities;

using CableNews.Domain.Common;

public class CountryConfig : BaseEntity
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    
    public string LocalNexansBrand { get; init; } = string.Empty;
    public string BrandColor { get; init; } = "#E1251B";
    public bool IsGlobal { get; init; } = false;
    public string LocationQuery { get; init; } = string.Empty;
    public List<string> ExtraRssFeeds { get; init; } = [];
    public List<string> KeyCompetitors { get; init; } = new();
    public List<string> DemandDrivers { get; init; } = new();
    public List<string> Institutions { get; init; } = [];
    public List<string> Operators { get; init; } = [];
    public List<string> MacroSignals { get; init; } = [];
    public List<string> ExtraEntities { get; init; } = [];
    public List<string> SalesIntelligence { get; init; } = [];
}
