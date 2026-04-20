namespace CableNews.Infrastructure.Services;

using Ardalis.GuardClauses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CableNews.Application.Common.Interfaces;
using CableNews.Domain.Entities;
using CableNews.Domain.Enums;
using CableNews.Infrastructure.Configuration;
using System.Net.Http.Json;
using System.Text.Json;

public class GeminiClassifierService : ILlmClassifierService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiConfig _config;
    private readonly ILogger<GeminiClassifierService> _logger;

    public GeminiClassifierService(HttpClient httpClient, IOptions<GeminiConfig> config, ILogger<GeminiClassifierService> logger)
    {
        _httpClient = httpClient;
        _config = Guard.Against.Null(config.Value);
        _logger = logger;
    }

    public async Task<List<ArticleAnalysis>> ClassifyArticlesAsync(
        List<Article> articles,
        CountryConfig country,
        CancellationToken cancellationToken)
    {
        if (articles.Count == 0) return [];

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_config.ModelId}:generateContent?key={_config.ApiKey}";

        var validCategories = new[]
        {
            "Energía y Redes",
            "Renovables e Hidrógeno",
            "Construcción y Edificación",
            "Infraestructura Pública",
            "Telecom y Data Centers",
            "Licitaciones y CAPEX",
            "Macro y Regulación",
            "Competencia",
            "Industria Global del Cable"
        };

        var nexansBrands = new[] { "Nexans", "Centelsa", "Indeco", "Madeco", "Ficap", "Incable" };
        var competitorsList = country.KeyCompetitors.Count > 0 ? string.Join(", ", country.KeyCompetitors) : "Prysmian, Southwire, Leoni, etc.";

        var isGlobal = country.IsGlobal || country.Code == "AMERICAS";

        var prompt = isGlobal
            ? BuildGlobalClassifierPrompt(validCategories, nexansBrands, competitorsList)
            : BuildLocalClassifierPrompt(validCategories, nexansBrands, competitorsList, country.Name);

        var articlesJson = JsonSerializer.Serialize(
            articles.Select(a => new { a.Hash, a.Title, Date = a.PublishedAt.ToString("yyyy-MM-dd") }));

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = prompt } }
            },
            contents = new[]
            {
                new { parts = new[] { new { text = articlesJson } } }
            }
        };

        const int maxRetries = 3;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
                var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retrySeconds = ExtractRetryDelay(jsonResponse);
                    if (attempt < maxRetries)
                    {
                        _logger.LogWarning("Gemini Classifier rate limit (429). Waiting {Seconds}s. Retry {Attempt}/{Max}",
                            retrySeconds, attempt + 1, maxRetries);
                        await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cancellationToken);
                        continue;
                    }
                    _logger.LogError("Gemini Classifier rate limit exceeded after {Max} retries", maxRetries);
                    return [];
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Gemini Classifier API Error: {StatusCode} - {Response}", response.StatusCode, jsonResponse);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)), cancellationToken);
                        continue;
                    }
                    return [];
                }

                using var doc = JsonDocument.Parse(jsonResponse);

                if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                    candidates.GetArrayLength() == 0)
                {
                    _logger.LogWarning("Gemini Classifier returned no candidates");
                    return [];
                }

                var firstCandidate = candidates[0];
                if (!firstCandidate.TryGetProperty("content", out var content) ||
                    !content.TryGetProperty("parts", out var parts) ||
                    parts.GetArrayLength() == 0)
                {
                    _logger.LogWarning("Gemini Classifier returned empty content/parts");
                    return [];
                }

                var text = parts[0].GetProperty("text").GetString();
                var cleanJson = text?.Replace("```json", "").Replace("```", "").Trim() ?? "[]";

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dtos = JsonSerializer.Deserialize<List<ArticleAnalysisDto>>(cleanJson, options);

                if (dtos == null) return [];

                return dtos.Where(d => d.IsRelevant).Select(d => new ArticleAnalysis
                {
                    ArticleHash = d.Hash ?? string.Empty,
                    Sentiment = Enum.TryParse<SentimentType>(d.Sentiment, true, out var s) ? s : SentimentType.Neutral,
                    SentimentScore = d.SentimentScore,
                    Category = validCategories.Contains(d.Category) ? d.Category! : "Macro y Regulación",
                    MentionedBrands = d.MentionedBrands ?? [],
                    MentionedCompetitors = d.MentionedCompetitors ?? [],
                    IsCrisisIndicator = d.IsCrisisIndicator
                })
                .Where(a => !string.IsNullOrEmpty(a.ArticleHash))
                .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception calling Gemini Classifier API (attempt {Attempt})", attempt);
                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)), cancellationToken);
                    continue;
                }
            }
        }

        return [];
    }

    private static string BuildGlobalClassifierPrompt(string[] categories, string[] brands, string competitors)
    {
        return $$"""
        You are classifying news articles for a GLOBAL cable & electrical infrastructure intelligence report.
        Your company is Nexans, a global cable manufacturer.

        Analyze each article and return a JSON array. For each article:
        - "hash": the precise article hash from the input
        - "sentiment": "Positive", "Neutral", or "Negative"
        - "sentimentScore": integer 0 to 100
        - "category": one of [{{string.Join(", ", categories)}}]
        - "mentionedBrands": array of Nexans brands mentioned ({{string.Join(", ", brands)}})
        - "mentionedCompetitors": array of competitors mentioned (like {{competitors}})
        - "isCrisisIndicator": true if negative news directly about Nexans or its subsidiaries
        - "isRelevant": true if the article matches ANY of these:
          1. Mentions any Nexans brand or subsidiary
          2. Mentions any cable industry competitor ({{competitors}})
          3. Covers submarine cables, high-voltage cables, power cables, fiber optic cables
          4. Describes energy infrastructure projects, grid expansion, offshore wind
          5. Covers copper/aluminium prices, supply chain, or raw materials for cables
          6. Announces tenders, contract awards, or CAPEX in energy/telecom/infrastructure
          7. Covers data center construction or expansion
          8. General cable industry or wire & cable market news
          EXCLUDE ENTIRELY: health news, generic politics, sports, entertainment, generic natural disasters

        Return ONLY the JSON array. No markdown, no explanation. Just `[{...}]`.
        """;
    }

    private static string BuildLocalClassifierPrompt(string[] categories, string[] brands, string competitors, string countryName)
    {
        return $$"""
        You are classifying news articles for a cable & electrical infrastructure report focused on {{countryName}}.
        Your company is Nexans, a cable manufacturer with local operations in this country.

        Analyze each article and return a JSON array. For each article:
        - "hash": the precise article hash from the input
        - "sentiment": "Positive", "Neutral", or "Negative"
        - "sentimentScore": integer 0 to 100
        - "category": one of [{{string.Join(", ", categories)}}]
        - "mentionedBrands": array of Nexans brands mentioned ({{string.Join(", ", brands)}})
        - "mentionedCompetitors": array of competitors mentioned (like {{competitors}})
        - "isCrisisIndicator": true if negative news directly about Nexans or its subsidiaries
        - "isRelevant": true if the article matches ANY of these:
          1. Mentions any Nexans brand or subsidiary ({{string.Join(", ", brands)}})
          2. Describes energy, infrastructure, mining, or construction projects in {{countryName}} or its region
          3. Covers tenders, contract awards, public works, or CAPEX in {{countryName}}
          4. Covers electricity grid, transmission lines, substations, renewable energy in {{countryName}}
          5. Covers data centers, telecom infrastructure, or fiber optic deployment in {{countryName}}
          6. Covers copper/aluminium prices, regulations, or tariffs affecting the cable industry
          7. Construction, real estate, or housing projects with potential cable demand
          8. Mining projects or expansions in {{countryName}}
          EXCLUDE ENTIRELY: health news (dengue, virus), generic politics without infrastructure impact, sports, entertainment, celebrity news, natural disasters unless they destroyed power/telecom infrastructure

        Be INCLUSIVE: if in doubt, mark as relevant. Better to include borderline articles than miss industry intelligence.

        Return ONLY the JSON array. No markdown, no explanation. Just `[{...}]`.
        """;
    }

    private static int ExtractRetryDelay(string jsonResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var details = doc.RootElement.GetProperty("error").GetProperty("details");
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("retryDelay", out var delay))
                {
                    var delayStr = delay.GetString() ?? "60s";
                    var numericPart = new string(delayStr.Where(c => char.IsDigit(c)).ToArray());
                    return int.TryParse(numericPart, out var seconds) ? seconds + 5 : 65;
                }
            }
        }
        catch { }
        return 65;
    }

    private class ArticleAnalysisDto
    {
        public string? Hash { get; set; }
        public string? Sentiment { get; set; }
        public int SentimentScore { get; set; }
        public string? Category { get; set; }
        public List<string>? MentionedBrands { get; set; }
        public List<string>? MentionedCompetitors { get; set; }
        public bool IsCrisisIndicator { get; set; }
        public bool IsRelevant { get; set; }
    }
}
