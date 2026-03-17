using CableNews.Domain.Entities;

namespace CableNews.Application.Common.Interfaces;

public interface ITenderProvider
{
    string ProviderName { get; }
    Task<List<TenderResult>> FetchTendersAsync(
        CountryConfig country,
        DateTimeOffset since,
        CancellationToken cancellationToken);
}
