namespace CableNews.Application.Common.Interfaces;

using CableNews.Domain.Entities;

public interface ILlmClassifierService
{
    Task<List<ArticleAnalysis>> ClassifyArticlesAsync(
        List<Article> articles,
        CountryConfig country,
        CancellationToken cancellationToken);
}
