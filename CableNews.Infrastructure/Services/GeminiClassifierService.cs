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
using System.Text.Json.Serialization;

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
            "Macro y Regulación"
        };
        
        var nexansBrands = new[] { "Nexans", "Centelsa", "Indeco", "Madeco", "Ficap", "Incable" };
        var competitorsList = country.KeyCompetitors.Count > 0 ? string.Join(", ", country.KeyCompetitors) : "Prysmian, etc.";

        var prompt = $$"""
        Analyze each article and return a JSON array. For each article:
        - "hash": the precise article hash from the input
        - "sentiment": "Positive", "Neutral", or "Negative"
        - "sentimentScore": integer 0 to 100
        - "category": one of [{{string.Join(", ", validCategories)}}]
        - "mentionedBrands": array of Nexans brands mentioned ({{string.Join(", ", nexansBrands)}})
        - "mentionedCompetitors": array of competitors mentioned (like {{competitorsList}})
        - "isCrisisIndicator": true if negative news directly about Nexans or its subsidiaries
        - "isRelevant": false if the article has no relation to cable/energy/mining/construction/infrastructure industry

        Return ONLY the JSON array. No markdown, no HTML, no explanation. Just the raw array `[{...}]`.
        """;

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

        const int maxRetries = 2;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
                var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Gemini Classifier API Error: {StatusCode} - {Response}", response.StatusCode, jsonResponse);
                    return [];
                }

                using var doc = JsonDocument.Parse(jsonResponse);
                
                var text = doc.RootElement
                              .GetProperty("candidates")[0]
                              .GetProperty("content")
                              .GetProperty("parts")[0]
                              .GetProperty("text").GetString();

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
                _logger.LogError(ex, "Exception calling Gemini API for classification");
            }
        }

        return [];
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
