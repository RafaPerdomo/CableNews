namespace CableNews.Application.Common.Interfaces;

using CableNews.Application.Common.Models;
using CableNews.Domain.Entities;

public interface IPrMetricsService
{
    Task<PrMetricsReport> CalculateMetricsAsync(
        List<ArticleAnalysis> analyses,
        int totalScannedCount,
        CountryConfig country,
        CancellationToken cancellationToken);
}
