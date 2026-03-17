namespace CableNews.Infrastructure.Services;

using CableNews.Application.Common.Interfaces;
using CableNews.Application.Common.Models;
using CableNews.Domain.Entities;
using CableNews.Domain.Enums;

public class PrMetricsService : IPrMetricsService
{
    public Task<PrMetricsReport> CalculateMetricsAsync(
        List<ArticleAnalysis> analyses,
        int totalScannedCount,
        CountryConfig country,
        CancellationToken cancellationToken)
    {
        if (analyses.Count == 0)
        {
            return Task.FromResult(new PrMetricsReport());
        }

        var totalArticles = analyses.Count;
        
        var mentionsByCompetitor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var comp in country.KeyCompetitors)
        {
            mentionsByCompetitor[comp] = 0;
        }

        int nexansMentions = 0;
        int totalCompetitorMentions = 0;
        int sentimentSum = 0;
        bool crisis = false;
        
        var categoryBreakdown = new Dictionary<string, int>();

        foreach (var analysis in analyses)
        {
            sentimentSum += analysis.SentimentScore;
            
            if (analysis.MentionedBrands.Count > 0)
            {
                nexansMentions++;
            }
            
            if (analysis.IsCrisisIndicator)
            {
                crisis = true;
            }

            foreach (var comp in analysis.MentionedCompetitors)
            {
                var knownComp = country.KeyCompetitors.FirstOrDefault(c => c.Equals(comp, StringComparison.OrdinalIgnoreCase) || comp.Contains(c, StringComparison.OrdinalIgnoreCase));
                var key = knownComp ?? comp;
                
                if (mentionsByCompetitor.ContainsKey(key))
                {
                    mentionsByCompetitor[key]++;
                    totalCompetitorMentions++;
                }
                else
                {
                    mentionsByCompetitor[key] = 1;
                    totalCompetitorMentions++;
                }
            }

            if (!string.IsNullOrEmpty(analysis.Category))
            {
                if (categoryBreakdown.ContainsKey(analysis.Category))
                    categoryBreakdown[analysis.Category]++;
                else
                    categoryBreakdown[analysis.Category] = 1;
            }
        }

        double averageSentiment = (double)sentimentSum / totalArticles;
        
        double totalIndustryMentions = nexansMentions + totalCompetitorMentions;
        double shareOfVoice = totalIndustryMentions > 0 ? (nexansMentions / totalIndustryMentions) * 100 : 100;
        
        var topComp = mentionsByCompetitor.OrderByDescending(x => x.Value).FirstOrDefault();
        double compShareOfVoice = totalIndustryMentions > 0 ? (topComp.Value / totalIndustryMentions) * 100 : 0;

        var report = new PrMetricsReport
        {
            TotalArticles = totalScannedCount,
            RelevantArticles = analyses.Count,
            AverageSentiment = averageSentiment,
            NexansMentions = nexansMentions,
            CrisisDetected = crisis,
            ShareOfVoice = shareOfVoice,
            CompetitorShareOfVoice = compShareOfVoice,
            TopCompetitor = topComp.Key ?? "Ninguno",
            CompetitorMentions = mentionsByCompetitor,
            CategoryBreakdown = categoryBreakdown
        };

        return Task.FromResult(report);
    }
}
