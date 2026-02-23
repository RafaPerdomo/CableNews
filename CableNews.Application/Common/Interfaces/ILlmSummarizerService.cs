namespace CableNews.Application.Common.Interfaces;

using CableNews.Domain.Entities;

public interface ILlmSummarizerService
{
    Task<string> SummarizeArticlesAsync(List<Article> articles, string countryName, CancellationToken cancellationToken);
}
