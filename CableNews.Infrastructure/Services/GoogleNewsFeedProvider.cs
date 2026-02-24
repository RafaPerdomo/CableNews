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
        var locationSuffix = countryConfig.IsGlobal ? string.Empty : $" location:{countryConfig.Name}";
        var queries = new List<string>();

        void AddQueryGroup(IEnumerable<string> terms)
        {
            var validTerms = terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (validTerms.Count == 0) return;

            // Batch into chunks of 8: long single-query strings get truncated by Google,
            // smaller batches return more distinct results.
            foreach (var chunk in validTerms.Chunk(8))
            {
                var joined = string.Join(" OR ", chunk);
                queries.Add($"({joined}){locationSuffix} when:{days}d");
            }
        }

        AddQueryGroup(countryConfig.DemandDrivers);
        AddQueryGroup(countryConfig.Institutions);
        AddQueryGroup(countryConfig.Operators);
        AddQueryGroup(countryConfig.MacroSignals);
        AddQueryGroup(countryConfig.ExtraEntities);
        AddQueryGroup(countryConfig.SalesIntelligence);
        AddQueryGroup(countryConfig.KeyCompetitors);

        if (!string.IsNullOrWhiteSpace(countryConfig.LocalNexansBrand))
        {
            var brandTerm = $"(Nexans OR \"{countryConfig.LocalNexansBrand}\")";
            if (countryConfig.IsGlobal)
                queries.Add($"{brandTerm} when:{days}d");
            else
                queries.Add($"{brandTerm}{locationSuffix} when:{days}d");
        }

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

                    if (pubDate != default)
                    {
                        var timeSpan = DateTime.UtcNow - pubDate.ToUniversalTime();
                        if (timeSpan.TotalDays > 35 || timeSpan.TotalHours < -48)
                            continue;
                    }

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

        // Fetch static industry/procurement RSS feeds (no query building, direct URL)
        foreach (var feedUrl in countryConfig.ExtraRssFeeds)
        {
            try
            {
                _logger.LogInformation("Fetching static RSS feed {Url} for {Country}", feedUrl, countryConfig.Name);
                var response = await _httpClient.GetStringAsync(feedUrl, cancellationToken);
                var xdoc = XDocument.Parse(response);
                var items = xdoc.Descendants("item").Take(50).ToList();
                int added = 0;

                foreach (var item in items)
                {
                    var title = item.Element("title")?.Value ?? string.Empty;
                    var link = item.Element("link")?.Value ?? string.Empty;
                    var pubDateStr = item.Element("pubDate")?.Value;

                    DateTime.TryParse(pubDateStr, out var pubDate);

                    if (pubDate != default)
                    {
                        var timeSpan = DateTime.UtcNow - pubDate.ToUniversalTime();
                        if (timeSpan.TotalDays > 35 || timeSpan.TotalHours < -48)
                            continue;
                    }

                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;

                    var hash = ComputeSha256(title);
                    if (!allArticles.Any(a => a.Hash == hash))
                    {
                        allArticles.Add(new Article
                        {
                            Hash = hash,
                            Title = title,
                            Url = link,
                            PublishedAt = pubDate,
                            CountryCode = countryConfig.Code,
                            Summary = string.Empty
                        });
                        added++;
                    }
                }

                _logger.LogInformation("Static feed {Url} added {Count} articles for {Country}", feedUrl, added, countryConfig.Name);
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to fetch static RSS {Url}: {Msg}", feedUrl, ex.Message);
            }
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
            var base64Str = googleUrl
                .Split(new[] { "articles/", "read/" }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()
                ?.Split('?')
                .FirstOrDefault();

            if (string.IsNullOrEmpty(base64Str))
                return googleUrl;

            // Step 1: Fetch signature using the articles/ URL (not rss/articles/)
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

                // Fallback: try extracting URL directly from older redirect HTML
                var extracted = ExtractUrlFromHtml(html);
                if (extracted is not null)
                    return extracted;
            }

            // Step 2: Call batchexecute RPC if we have credentials
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
            // Skip leading garbage like ")]}'" and find the JSON
            var jsonStart = responseText.IndexOf("[[[");
            if (jsonStart < 0) jsonStart = responseText.IndexOf("[[");
            if (jsonStart < 0) return null;
            
            var jsonStr = responseText[jsonStart..].Trim();
            using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);

            // Structure: [ ['wrb.fr', 'Fbv4je', '{inner_json}', ...], ['di',...], ... ]
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                var items = element.EnumerateArray().ToList();
                if (items.Count < 3) continue;
                // item[0] should be 'wrb.fr', item[2] is the inner JSON string
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
