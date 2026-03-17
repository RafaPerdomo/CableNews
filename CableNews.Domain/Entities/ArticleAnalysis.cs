namespace CableNews.Domain.Entities;

using CableNews.Domain.Enums;

public class ArticleAnalysis
{
    public string ArticleHash { get; init; } = default!;
    public SentimentType Sentiment { get; init; }
    public int SentimentScore { get; init; }
    public List<string> MentionedBrands { get; init; } = [];
    public List<string> MentionedCompetitors { get; init; } = [];
    public bool IsCrisisIndicator { get; init; }
    public string Category { get; init; } = default!;
}
