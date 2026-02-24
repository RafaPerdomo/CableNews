namespace CableNews.Infrastructure.Services;

using Ardalis.GuardClauses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CableNews.Application.Common.Interfaces;
using CableNews.Domain.Entities;
using CableNews.Infrastructure.Configuration;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;

public class GeminiProLlmService : ILlmSummarizerService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiConfig _config;
    private readonly ILogger<GeminiProLlmService> _logger;

    public GeminiProLlmService(HttpClient httpClient, IOptions<GeminiConfig> config, ILogger<GeminiProLlmService> logger)
    {
        _httpClient = httpClient;
        _config = Guard.Against.Null(config.Value);
        _logger = logger;
    }

    public async Task<string> SummarizeArticlesAsync(List<Article> articles, CountryConfig country, CancellationToken cancellationToken)
    {
        if (articles.Count == 0) return string.Empty;

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_config.ModelId}:generateContent?key={_config.ApiKey}";

        var articlesText = new StringBuilder($"Noticias para {country.Name}:\n\n");
        foreach (var article in articles)
        {
            articlesText.AppendLine($"- {article.Title} - URL: {article.Url}");
        }

        var competitorsFocus = string.IsNullOrWhiteSpace(country.LocalNexansBrand)
            ? "Nexans y sus competidores"
            : $"la marca local de Nexans ({country.LocalNexansBrand}) y competidores como {string.Join(", ", country.KeyCompetitors)}";

        var systemInstruction = $@"Eres un analista experto de nivel ejecutivo especializado en energía, minería e infraestructura.
Tu tarea es leer las noticias suministradas y generar un Reporte Ejecutivo en HTML EXCLUSIVAMENTE sobre {country.Name}.
IMPORTANTE: Solo debes incluir noticias que sean relevantes para {country.Name}. Ignora completamente noticias de otros países.

Clasifica las noticias según estas categorías (omite las que no tengan noticias):
- Energía y Redes
- Renovables e Hidrógeno
- Construcción y Edificación
- Infraestructura Pública
- Telecom y Data Centers
- Licitaciones y CAPEX
- Macro y Regulación
- 🎯 Oportunidades Comerciales (noticias sobre nuevos proyectos anunciados, adjudicaciones, cierres financieros, nuevas plantas, expansiones, licitaciones abiertas, convocatorias, o movimientos de {competitorsFocus})

Formato HTML estricto:
<h1>Reporte Ejecutivo: {country.Name}</h1>
Bajo el título del país, crea un <h2> por Categoría.
Bajo cada categoría, lista las noticias usando <ul><li>.
Formato por noticia: <strong>Título:</strong> Resumen ejecutivo (máximo 2 líneas). <a href='URL'>Enlace</a>.

Para la categoría 🎯 Oportunidades Comerciales, agrega un indicador de urgencia al inicio de cada noticia:
- 🔴 Alta: Licitación abierta, adjudicación reciente, o proyecto con cierre financiero próximo
- 🟡 Media: Nuevo proyecto anunciado o expansión confirmada
- 🟢 Baja: Movimiento de competidor o señal de mercado a monitorear

AL FINAL:
Agrega <h2>Recomendaciones Estratégicas del Gerente de Marketing – {country.Name}</h2>.
Asume el rol de Gerente de Marketing para {country.Name} y proporciona 1-2 párrafos sobre cómo las noticias representan oportunidades o riesgos para nuestra empresa (fabricante de cables y soluciones eléctricas/telecom), y qué acciones de prospección sugieres. Sé MUY específico mencionando nombres de empresas, proyectos o licitaciones del reporte.

NO inventes información. Devuelve SOLO el HTML, sin markdown ni saludos extras.";

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemInstruction } }
            },
            contents = new[]
            {
                new { parts = new[] { new { text = articlesText.ToString() } } }
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
                        _logger.LogWarning("Gemini rate limit hit (429). Waiting {Seconds}s before retry {Attempt}/{Max}...", 
                            retrySeconds, attempt + 1, maxRetries);
                        await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cancellationToken);
                        continue;
                    }
                    _logger.LogError("Gemini rate limit exceeded after {Max} retries. Skipping.", maxRetries);
                    return string.Empty;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Gemini API Error: {StatusCode} - {Response}", response.StatusCode, jsonResponse);
                    return string.Empty;
                }

                using var doc = JsonDocument.Parse(jsonResponse);
                
                var text = doc.RootElement
                              .GetProperty("candidates")[0]
                              .GetProperty("content")
                              .GetProperty("parts")[0]
                              .GetProperty("text").GetString();

                return text?.Replace("```html", "").Replace("```", "").Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception calling Gemini API for summarization");
                return string.Empty;
            }
        }

        return string.Empty;
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
}
