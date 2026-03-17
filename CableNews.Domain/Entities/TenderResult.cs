namespace CableNews.Domain.Entities;

public class TenderResult
{
    public string TenderId { get; init; } = default!;
    public string Title { get; init; } = default!;
    public string Entity { get; init; } = default!;
    public string Url { get; init; } = default!;
    public decimal? EstimatedValue { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset? Deadline { get; init; }
    public string CountryCode { get; init; } = default!;
}
