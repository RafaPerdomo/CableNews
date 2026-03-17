namespace CableNews.Application.Common.Models;

public record PrMetricsReport
{
    public double ShareOfVoice { get; init; }
    public double CompetitorShareOfVoice { get; init; }
    public string TopCompetitor { get; init; } = default!;
    public double AverageSentiment { get; init; }
    public int TotalArticles { get; init; }
    public int RelevantArticles { get; init; }
    public int NexansMentions { get; init; }
    public bool CrisisDetected { get; init; }
    public Dictionary<string, int> CompetitorMentions { get; init; } = new();
    public Dictionary<string, int> CategoryBreakdown { get; init; } = new();
}
