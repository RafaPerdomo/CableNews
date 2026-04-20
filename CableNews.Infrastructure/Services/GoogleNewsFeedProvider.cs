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
        var days = Math.Max(1, agentConfig.LookbackHours / 24);
        var locationSuffix = BuildLocationSuffix(countryConfig);
        var queries = new List<string>();

        void AddQueryGroup(IEnumerable<string> terms, int maxDays, string? locationOverride = null)
        {
            var validTerms = terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (validTerms.Count == 0) return;

            var loc = locationOverride ?? locationSuffix;

            foreach (var chunk in validTerms.Chunk(8))
            {
                var joined = string.Join(" OR ", chunk);
                queries.Add($"({joined}){loc} when:{maxDays}d");
            }
        }

        AddQueryGroup(countryConfig.DemandDrivers, 7);
        AddQueryGroup(countryConfig.Institutions, 7);
        AddQueryGroup(countryConfig.Operators, 7);
        AddQueryGroup(countryConfig.MacroSignals, 7);
        AddQueryGroup(countryConfig.ExtraEntities, 7);
        AddQueryGroup(countryConfig.SalesIntelligence, 7);

        AddQueryGroup(countryConfig.DemandDrivers, days);
        AddQueryGroup(countryConfig.Institutions, days);
        AddQueryGroup(countryConfig.Operators, days);
        AddQueryGroup(countryConfig.MacroSignals, days);
        AddQueryGroup(countryConfig.ExtraEntities, days);
        AddQueryGroup(countryConfig.SalesIntelligence, days);

        if (IsAmericasOrGlobal(countryConfig))
        {
            AddCompetitorQueries(countryConfig, queries, days);
            AddGlobalIndustryQueries(countryConfig, queries);
        }

        AddBrandQueries(countryConfig, queries, locationSuffix, days);

        var articleMap = new Dictionary<string, Article>(StringComparer.Ordinal);

        foreach (var query in queries)
        {
            var lang = countryConfig.Code == "BR" ? "pt" : agentConfig.DefaultLanguage;
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://news.google.com/rss/search?q={encodedQuery}&hl={lang}&gl={countryConfig.Code}";

            if (url.Length > 2048)
            {
                _logger.LogWarning("Skipping query for {Country}: URL length {Length} exceeds limit", countryConfig.Name, url.Length);
                continue;
            }

            _logger.LogInformation("Fetching RSS for {Country}. URL length: {Length}", countryConfig.Name, url.Length);

            try
            {
                var response = await _httpClient.GetStringAsync(url, cancellationToken);
                var xdoc = XDocument.Parse(response);
                var items = xdoc.Descendants("item").Take(100).ToList();
                int added = 0;

                foreach (var item in items)
                {
                    var article = ParseRssItem(item, countryConfig.Code);
                    if (article is null) continue;

                    if (!articleMap.ContainsKey(article.Hash))
                    {
                        articleMap[article.Hash] = article;
                        added++;
                    }
                }

                _logger.LogInformation("Query returned {Added} new articles (total: {Total})", added, articleMap.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error fetching RSS for {Country}: {Msg}", countryConfig.Name, ex.Message);
            }

            await Task.Delay(1500, cancellationToken);
        }

        await FetchStaticRssFeeds(countryConfig, articleMap, cancellationToken);

        var brandKeywords = new[] { "Nexans", "Centelsa", "Indeco", "Madeco", "Ficap", "Incable" };

        return articleMap.Values
            .OrderByDescending(a => brandKeywords.Any(b => a.Title.Contains(b, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(a => a.PublishedAt)
            .Take(agentConfig.MaxArticlesPerCountry)
            .ToList();
    }

    public async Task<List<Article>> ResolveUrlsAsync(List<Article> articles, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Resolving {Count} Google redirect URLs...", articles.Count);

        var resolved = new Article[articles.Count];
        var semaphore = new SemaphoreSlim(5);
        var tasks = articles.Select(async (article, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var realUrl = await ResolveGoogleRedirectAsync(article.Url, cancellationToken);
                resolved[index] = new Article
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
        return resolved.ToList();
    }

    private static bool IsAmericasOrGlobal(CountryConfig config) =>
        config.IsGlobal || config.Code is "AMERICAS";

    private static string BuildLocationSuffix(CountryConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.LocationQuery))
            return $" {config.LocationQuery}";

        return config.IsGlobal ? string.Empty : $" location:{config.Name}";
    }

    private static void AddCompetitorQueries(CountryConfig config, List<string> queries, int days)
    {
        var searchTerms = config.CompetitorSearchTerms.Count > 0
            ? config.CompetitorSearchTerms
            : config.KeyCompetitors;

        var validTerms = searchTerms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (validTerms.Count == 0) return;

        foreach (var chunk in validTerms.Chunk(4))
        {
            var joined = string.Join(" OR ", chunk.Select(c => c.Contains(' ') ? $"\"{c}\"" : c));
            queries.Add($"({joined}) when:7d");
            queries.Add($"({joined}) when:{days}d");
        }

        var actionTerms = new[] { "contract", "awarded", "tender", "investment", "acquisition", "factory", "plant" };
        foreach (var comp in config.KeyCompetitors.Take(6).Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            var shortName = comp.Split('(')[0].Trim().Split(' ')[0];
            var actions = string.Join(" OR ", actionTerms);
            queries.Add($"({shortName}) AND ({actions}) when:7d");
        }
    }

    private static void AddGlobalIndustryQueries(CountryConfig config, List<string> queries)
    {
        var industryTerms = new[]
        {
            "\"cable industry\" OR \"wire and cable\" OR \"power cable\" OR \"cable manufacturer\"",
            "\"submarine cable\" OR \"HVDC cable\" OR \"offshore wind cable\" OR \"subsea cable\"",
            "\"high voltage cable\" OR \"medium voltage cable\" OR \"fiber optic cable deployment\"",
            "\"energy transition\" OR \"grid modernization\" OR \"grid expansion\" investment",
            "\"data center\" AND (cable OR infrastructure OR construction)",
            "\"copper price\" OR \"aluminium price\" OR \"LME copper\" OR \"copper futures\""
        };

        foreach (var term in industryTerms)
        {
            queries.Add($"({term}) when:7d");
        }
    }

    private static void AddBrandQueries(CountryConfig config, List<string> queries, string locationSuffix, int days)
    {
        if (string.IsNullOrWhiteSpace(config.LocalNexansBrand)) return;

        var brandTerm = $"(Nexans OR \"{config.LocalNexansBrand}\")";
        if (IsAmericasOrGlobal(config))
        {
            queries.Add($"{brandTerm} when:7d");
            queries.Add($"{brandTerm} when:{days}d");
        }
        else
        {
            queries.Add($"{brandTerm}{locationSuffix} when:7d");
            queries.Add($"{brandTerm}{locationSuffix} when:{days}d");
        }

        var allBrands = new[]
        {
            "Nexans", "\"Centelsa by Nexans\"", "Centelsa",
            "\"INDECO by Nexans\"", "\"Madeco by Nexans\"", "Madeco",
            "\"Ficap by Nexans\"", "Ficap", "\"Nexans Brasil\"",
            "\"Nexans Colombia\"", "\"Nexans Peru\"", "\"Nexans Chile\""
        };
        var crossBrandQuery = string.Join(" OR ", allBrands);
        queries.Add($"({crossBrandQuery}) when:7d");
        queries.Add($"({crossBrandQuery}) when:{days}d");
    }

    private async Task FetchStaticRssFeeds(CountryConfig config, Dictionary<string, Article> articleMap, CancellationToken cancellationToken)
    {
        foreach (var feedUrl in config.ExtraRssFeeds)
        {
            try
            {
                _logger.LogInformation("Fetching static RSS feed {Url} for {Country}", feedUrl, config.Name);
                var response = await _httpClient.GetStringAsync(feedUrl, cancellationToken);
                var xdoc = XDocument.Parse(response);
                var items = xdoc.Descendants("item").Take(50).ToList();
                int added = 0;

                foreach (var item in items)
                {
                    var article = ParseRssItem(item, config.Code);
                    if (article is null) continue;

                    if (!articleMap.ContainsKey(article.Hash))
                    {
                        articleMap[article.Hash] = article;
                        added++;
                    }
                }

                _logger.LogInformation("Static feed {Url} added {Count} articles for {Country}", feedUrl, added, config.Name);
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to fetch static RSS {Url}: {Msg}", feedUrl, ex.Message);
            }
        }
    }

    private static Article? ParseRssItem(XElement item, string countryCode)
    {
        var title = item.Element("title")?.Value ?? string.Empty;
        var googleLink = item.Element("link")?.Value ?? string.Empty;
        var description = item.Element("description")?.Value ?? string.Empty;
        var pubDateStr = item.Element("pubDate")?.Value;

        DateTime.TryParse(pubDateStr, out var pubDate);

        if (pubDate != default)
        {
            var timeSpan = DateTime.UtcNow - pubDate.ToUniversalTime();
            if (timeSpan.TotalDays > 35 || timeSpan.TotalHours < -48)
                return null;
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(googleLink))
            return null;

        var articleUrl = ExtractUrlFromDescription(description) ?? googleLink;
        var hash = ComputeSha256(title);

        return new Article
        {
            Hash = hash,
            Title = title,
            Url = articleUrl,
            PublishedAt = pubDate,
            CountryCode = countryCode,
            Summary = string.Empty
        };
    }

    private static bool IsGoogleRedirectUrl(string url) =>
        url.Contains("google.com/rss/articles") || url.Contains("news.google.com");

    private async Task<string> ResolveGoogleRedirectAsync(string googleUrl, CancellationToken cancellationToken)
    {
        if (!IsGoogleRedirectUrl(googleUrl))
            return googleUrl;

        try
        {
            var base64Str = googleUrl
                .Split(new[] { "articles/", "read/" }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()
                ?.Split('?')
                .FirstOrDefault();

            if (string.IsNullOrEmpty(base64Str))
                return googleUrl;

            var articlePageUrl = $"https://news.google.com/articles/{base64Str}";
            string? signature = null;
            string? timestamp = null;

            foreach (var fetchUrl in new[] { articlePageUrl, googleUrl })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, fetchUrl);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");
                request.Headers.Add("Accept", "text/html,application/xhtml+xml");

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.RequestMessage?.RequestUri is not null &&
                    !response.RequestMessage.RequestUri.Host.Contains("google.com"))
                    return response.RequestMessage.RequestUri.ToString();

                var html = await response.Content.ReadAsStringAsync(cancellationToken);

                signature = ExtractAttribute(html, "data-n-a-sg=\"");
                timestamp = ExtractAttribute(html, "data-n-a-ts=\"");

                if (!string.IsNullOrEmpty(signature) && !string.IsNullOrEmpty(timestamp))
                    break;

                var extracted = ExtractUrlFromHtml(html);
                if (extracted is not null)
                    return extracted;
            }

            if (!string.IsNullOrEmpty(signature) && !string.IsNullOrEmpty(timestamp))
            {
                var payloadInner = $"[\"garturlreq\",[[\"X\",\"X\",[\"X\",\"X\"],null,null,1,1,\"US:en\",null,1,null,null,null,null,null,0,1],\"X\",\"X\",1,[1,1,1],1,1,null,0,0,null,0],\"{base64Str}\",{timestamp},\"{signature}\"]";
                var reqData = $"[[[\"Fbv4je\",{System.Text.Json.JsonSerializer.Serialize(payloadInner)},null,\"generic\"]]]";

                var encodedReqData = "f.req=" + Uri.EscapeDataString(reqData);
                var content = new StringContent(encodedReqData, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

                using var batchRequest = new HttpRequestMessage(HttpMethod.Post, "https://news.google.com/_/DotsSplashUi/data/batchexecute")
                {
                    Content = content
                };
                batchRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36");

                var batchResponse = await _httpClient.SendAsync(batchRequest, cancellationToken);
                var batchJsonStr = await batchResponse.Content.ReadAsStringAsync(cancellationToken);

                var decodedUrl = ParseBatchExecuteResponse(batchJsonStr);
                if (decodedUrl != null)
                {
                    _logger.LogDebug("Resolved Google URL via batchexecute: {Url}", decodedUrl);
                    return decodedUrl;
                }

                _logger.LogDebug("batchexecute returned no URL. Response snippet: {Snippet}", batchJsonStr.Length > 200 ? batchJsonStr[..200] : batchJsonStr);
            }
            else
            {
                _logger.LogWarning("Could not extract signature/timestamp for Google URL: {Url}", googleUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error resolving Google News URL: {Url}. Msg: {Msg}", googleUrl, ex.Message);
        }

        return googleUrl;
    }

    private static string? ExtractAttribute(string html, string attributeName)
    {
        var index = html.IndexOf(attributeName, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var start = index + attributeName.Length;
        var end = html.IndexOf('"', start);
        if (end < 0) return null;
        return html[start..end];
    }

    private static string? ParseBatchExecuteResponse(string responseText)
    {
        try
        {
            var jsonStart = responseText.IndexOf("[[[");
            if (jsonStart < 0) jsonStart = responseText.IndexOf("[[");
            if (jsonStart < 0) return null;

            var jsonStr = responseText[jsonStart..].Trim();
            using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                var items = element.EnumerateArray().ToList();
                if (items.Count < 3) continue;
                var innerJsonStr = items[2].ValueKind == System.Text.Json.JsonValueKind.String
                    ? items[2].GetString()
                    : null;
                if (string.IsNullOrEmpty(innerJsonStr) || !innerJsonStr.StartsWith("[")) continue;
                using var innerDoc = System.Text.Json.JsonDocument.Parse(innerJsonStr);
                var innerItems = innerDoc.RootElement.EnumerateArray().ToList();
                if (innerItems.Count >= 2 && innerItems[1].ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var url = innerItems[1].GetString();
                    if (!string.IsNullOrEmpty(url) && url.StartsWith("http"))
                        return url;
                }
            }
        }
        catch { }
        return null;
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
