namespace CableNews.Domain.Entities;

using CableNews.Domain.Common;
using CableNews.Domain.Enums;

public class Article : BaseEntity
{
    public required string Hash { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string? Summary { get; init; }
    public DateTime PublishedAt { get; init; }
    public ArticleCategory Category { get; init; } = ArticleCategory.General;
    public required string CountryCode { get; init; }
}
