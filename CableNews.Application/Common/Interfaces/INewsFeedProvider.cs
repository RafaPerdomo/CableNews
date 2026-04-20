namespace CableNews.Application.Common.Interfaces;

using CableNews.Application.Common.Models;
using CableNews.Domain.Entities;

public interface INewsFeedProvider
{
    Task<List<Article>> FetchNewsAsync(CountryConfig countryConfig, NewsAgentConfig agentConfig, CancellationToken cancellationToken);
    Task<List<Article>> ResolveUrlsAsync(List<Article> articles, CancellationToken cancellationToken);
}
