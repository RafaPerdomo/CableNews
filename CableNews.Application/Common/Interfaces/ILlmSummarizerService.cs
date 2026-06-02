namespace CableNews.Application.Common.Interfaces;

using CableNews.Domain.Entities;
using CableNews.Application.Common.Models;

public interface ILlmSummarizerService
{
    Task<string> SummarizeArticlesAsync(
        List<AnalyzedArticle> articles, 
        PrMetricsReport metrics,
        List<TenderResult> tenders,
        CountryConfig countryConfig, 
        CancellationToken cancellationToken);
}
