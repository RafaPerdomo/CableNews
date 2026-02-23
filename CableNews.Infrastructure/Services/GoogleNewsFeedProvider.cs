namespace CableNews.Infrastructure.Services;

using Microsoft.Extensions.Logging;
using CableNews.Application.Common.Interfaces;
using CableNews.Application.Common.Models;
using CableNews.Domain.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

public class GoogleNewsFeedProvider : INewsFeedProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleNewsFeedProvider> _logger;

    public GoogleNewsFeedProvider(HttpClient httpClient, ILogger<GoogleNewsFeedProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<Article>> FetchNewsAsync(CountryConfig countryConfig, NewsAgentConfig agentConfig, CancellationToken cancellationToken)
    {
        var queries = new List<string>();

        void AddQueryGroup(IEnumerable<string> terms)
        {
            var validTerms = terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (validTerms.Count > 0)
            {
                var joined = string.Join(" OR ", validTerms);
                queries.Add($"({joined}) location:{countryConfig.Name} when:1d");
            }
        }

        AddQueryGroup(countryConfig.DemandDrivers);
        AddQueryGroup(countryConfig.Institutions);
        AddQueryGroup(countryConfig.Operators);
        AddQueryGroup(countryConfig.MacroSignals);
        AddQueryGroup(countryConfig.ExtraEntities);
        AddQueryGroup(countryConfig.SalesIntelligence);

        var allArticles = new List<Article>();

        foreach (var query in queries)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://news.google.com/rss/search?q={encodedQuery}&hl={agentConfig.DefaultLanguage}-{countryConfig.Code}&gl={countryConfig.Code}&ceid={countryConfig.Code}:{agentConfig.DefaultLanguage}-{countryConfig.Code}";

            _logger.LogInformation("Fetching RSS group for {Country}. URL length: {Length}", countryConfig.Name, url.Length);

            try
            {
                var response = await _httpClient.GetStringAsync(url, cancellationToken);
                var xdoc = XDocument.Parse(response);
                
                var items = xdoc.Descendants("item").Take(100).ToList();
                int skippedTitle = 0;

                foreach (var item in items)
                {
                    var title = item.Element("title")?.Value ?? string.Empty;
                    var googleLink = item.Element("link")?.Value ?? string.Empty;
                    var description = item.Element("description")?.Value ?? string.Empty;
                    var pubDateStr = item.Element("pubDate")?.Value;
                    
                    DateTime.TryParse(pubDateStr, out var pubDate);

                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(googleLink)) 
                    {
                        skippedTitle++;
                        continue;
                    }

                    var articleUrl = ExtractUrlFromDescription(description) ?? googleLink;

                    var hash = ComputeSha256(title);

                    if (!allArticles.Any(a => a.Hash == hash))
                    {
                        allArticles.Add(new Article
                        {
                            Hash = hash,
                            Title = title,
                            Url = articleUrl,
                            PublishedAt = pubDate,
                            CountryCode = countryConfig.Code,
                            Summary = string.Empty
                        });
                    }
                }
                
                _logger.LogInformation("Subgroup returned {Count} valid internal RSS items and skipped {TitleCount} errors.", 
                   allArticles.Count, skippedTitle);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error fetching or parsing news subgroup for {Country} (Likely Rate limit). Msg: {Msg}", countryConfig.Name, ex.Message);
            }

            await Task.Delay(1500, cancellationToken);
        }

        var orderedArticles = allArticles
            .OrderByDescending(a => a.PublishedAt)
            .Take(agentConfig.MaxArticlesPerCountry)
            .ToList();

        _logger.LogInformation("Resolving {Count} Google redirect URLs for {Country}...", orderedArticles.Count, countryConfig.Name);

        var resolvedArticles = new Article[orderedArticles.Count];
        var semaphore = new SemaphoreSlim(5);
        var tasks = orderedArticles.Select(async (article, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var realUrl = await ResolveGoogleRedirectAsync(article.Url, cancellationToken);
                resolvedArticles[index] = new Article
                {
                    Hash = article.Hash,
                    Title = article.Title,
                    Url = realUrl,
                    PublishedAt = article.PublishedAt,
                    CountryCode = article.CountryCode,
                    Summary = article.Summary
                };
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return resolvedArticles.ToList();
    }

    private static bool IsGoogleRedirectUrl(string url) =>
        url.Contains("google.com/rss/articles") || url.Contains("news.google.com");

    private async Task<string> ResolveGoogleRedirectAsync(string googleUrl, CancellationToken cancellationToken)
    {
        if (!IsGoogleRedirectUrl(googleUrl))
            return googleUrl;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, googleUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept", "text/html");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.RequestMessage?.RequestUri is not null &&
                !response.RequestMessage.RequestUri.Host.Contains("google.com"))
                return response.RequestMessage.RequestUri.ToString();

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var extracted = ExtractUrlFromHtml(html);
            if (extracted is not null)
                return extracted;
        }
        catch { }

        return googleUrl;
    }

    private static string? ExtractUrlFromHtml(string html)
    {
        var patterns = new[]
        {
            "data-n-au=\"",
            "href=\"https://",
            "href=\"http://"
        };

        foreach (var pattern in patterns)
        {
            var index = html.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            var urlStart = pattern.StartsWith("href=") 
                ? index + "href=\"".Length 
                : index + pattern.Length;

            var urlEnd = html.IndexOf('"', urlStart);
            if (urlEnd <= urlStart) continue;

            var url = html[urlStart..urlEnd];
            if (!url.Contains("google.com") && Uri.TryCreate(url, UriKind.Absolute, out _))
                return System.Net.WebUtility.HtmlDecode(url);
        }

        return null;
    }

    private static string? ExtractUrlFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var decoded = System.Net.WebUtility.HtmlDecode(description);
        var hrefIndex = decoded.IndexOf("href=", StringComparison.OrdinalIgnoreCase);
        if (hrefIndex < 0)
            return null;

        var quoteChar = decoded.Length > hrefIndex + 5 ? decoded[hrefIndex + 5] : '"';
        var urlStart = hrefIndex + 6;
        var urlEnd = decoded.IndexOf(quoteChar, urlStart);
        if (urlEnd <= urlStart)
            return null;

        var url = decoded[urlStart..urlEnd];
        if (!url.Contains("google.com") && Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        return null;
    }

    private static string ComputeSha256(string rawData)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
