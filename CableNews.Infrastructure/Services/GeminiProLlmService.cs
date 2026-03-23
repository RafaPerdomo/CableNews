namespace CableNews.Infrastructure.Services;

using Ardalis.GuardClauses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CableNews.Application.Common.Interfaces;
using CableNews.Application.Common.Models;
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

    public async Task<string> SummarizeArticlesAsync(
        List<Article> articles, 
        PrMetricsReport metrics,
        List<CableNews.Domain.Entities.TenderResult> tenders,
        CountryConfig countryConfig, 
        CancellationToken cancellationToken)
    {
        if (articles.Count == 0 && tenders.Count == 0) return string.Empty;

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_config.ModelId}:generateContent?key={_config.ApiKey}";

        var articlesText = new StringBuilder($"Noticias para {countryConfig.Name}:\n\n");
        foreach (var article in articles)
        {
            var dateStr = article.PublishedAt != default ? article.PublishedAt.ToString("yyyy-MM-dd") : "Fecha desconocida";
            articlesText.AppendLine($"- [{dateStr}] {article.Title} - URL: {article.Url}");
        }

        var competitors = string.Join(", ", countryConfig.KeyCompetitors.Take(6));
        var systemInstruction = countryConfig.IsGlobal
            ? $@"You are a senior analyst specializing in the global cable and energy infrastructure industry.
Your task is to read the supplied news articles and produce an Executive Intelligence Report on Nexans Group worldwide, in HTML format.

CRITICAL RULES:
1. STRICT RELEVANCE: Your company sells ELECTRICAL CABLES, TELECOM CABLES, and MINING/INFRASTRUCTURE SOLUTIONS. Only include news that directly impacts this business (tenders, new infrastructure projects, power grids, mining expansion, data centers, or competitor moves). EXCLUDE ENTIRELY generic health news (e.g. dengue), generic politics, and generic natural disasters (e.g. earthquakes) unless they explicitly destroyed key power/telecom infrastructure.
2. Include ALL relevant articles about Nexans, its subsidiaries (Centelsa, Madeco, Indeco, etc.), the global cable industry, or commodity prices.
3. Include competitor moves from: {competitors}.
4. DATE: Always show the article date in brackets [YYYY-MM-DD].
5. DEDUPLICATION: If multiple articles describe the same event, keep only the most recent.
6. FACTS ONLY: Only use information explicitly stated in the articles. Do not invent data.
7. EMPTY SECTIONS: If a category has no relevant news, do NOT generate its <h2> tag. The only mandatory sections are: Competitor Intelligence, Nexans Worldwide, and Strategic Recommendations.

Classify articles into these categories (only those with at least one RELEVANT article):
- Energy & Grid Infrastructure
- Renewables & Offshore Wind
- Telecom & Data Centers
- Mining & Industrial
- Commodities & Supply Chain
- M&A / Corporate
- 🏢 Competitor Intelligence (ALWAYS INCLUDE. Cover: {competitors}. If none, write: No significant competitor activity this period.)
- 📰 Nexans Worldwide (ALWAYS INCLUDE. Cover Nexans Group, all subsidiaries and brands. If none, write: No Nexans mentions this period.)
- 🎯 Commercial Opportunities (Only if concrete: awarded contracts, tenders, announced projects with investment figures)

ORDERING: Within each category, sort by commercial impact: 🔴 first, then 🟡, then 🟢.

HTML FORMAT (strict, valid HTML):
<h1>Nexans Group – Global Intelligence Report</h1>
One <h2> per category, news as <ul><li>.

Formato por noticia (OBLIGATORIO incluir la fecha antes del enlace):
[emoji semáforo] <strong>Título:</strong> Resumen ejecutivo (máximo 2 líneas, sin exageraciones). [YYYY-MM-DD] <a href='URL'>Enlace</a>.

Traffic light (commercial relevance for a cable manufacturer):
- 🔴 High: Direct sales opportunity (contract awarded, tender open, plant construction, new transmission line, announced investment)
- 🟡 Medium: Industry context or regulation that could trigger future demand
- 🟢 Low: Market signal to monitor

AT THE END:
Add <h2>Global Strategic Recommendations – Nexans Intelligence</h2>.
Act as Global Marketing Director. Provide 1-2 paragraphs on what actions the commercial teams in LATAM (Colombia, Peru, Chile, Ecuador) should take based on the global signals observed.

OUTPUT: Return ONLY the HTML. No markdown. No extra text outside the HTML."
            : $@"Eres un analista experto de nivel ejecutivo especializado en energía, minería e infraestructura.
Tu tarea es leer las noticias suministradas y generar un Reporte Ejecutivo en HTML para la gerencia de {countryConfig.Name}.

REGLAS CRÍTICAS:
1. RELEVANCIA ESTRICTA: Tu empresa vende CABLES ELÉCTRICOS, DE TELECOMUNICACIONES y SOLUCIONES PARA INFRAESTRUCTURA Y MINERÍA. Solo incluye noticias que impacten directamente este negocio (licitaciones, nuevos proyectos de infraestructura, energía, minería, data centers, o movimientos de competidores). EXCLUYE TOTALMENTE noticias sobre salud (ej. dengue, virus), política general sin impacto en infraestructura, farándula, o desastres naturales genéricos (ej. sismos) a menos que hayan destruido infraestructura clave.
2. DEDUPLICACIÓN: Si múltiples noticias describen el mismo evento, incluye solo la más reciente.
3. SOLO HECHOS: Solo usa información explícita en las noticias. No infieras ni inventes datos.
4. CATEGORÍAS VACÍAS: Si una categoría no tiene noticias relevantes, NO incluyas el tag <h2> de esa categoría. Omite completamente el bloque HTML de esa sección. Las únicas secciones obligatorias son: Movimientos de la Competencia, Nexans en {countryConfig.Name}, y Recomendaciones.

Categorías Estratégicas para agrupar (usa <h2> para el título, color #B91C1C):
1. Energía y Redes (cables, transmisión, electrificación, energías renovables, subestaciones).
2. Construcción y Edificación (vivienda, cables BTD, edificios inteligentes).
3. Infraestructura Pública (vías, metro, aeropuertos, gasoductos, proyectos estatales).
4. Telecom y Data Centers (fibra óptica, nodos, operadores, centros de datos).
5. Licitaciones y CAPEX (proyectos adjudicados, créditos para obras, expansiones de competidores o clientes). AQUI DEBES INCLUIR LAS LICITACIONES COMPROBADAS PROPORCIONADAS EN EL NODO <LICITACIONES>.
6. Macro y Regulación (leyes, tarifas energéticas, materias primas como cobre/aluminio).
7. Movimientos de la Competencia (Prysmian, Condumex, Phelps Dodge, etc).

INSTRUCCIONES EXTRA PARA LAS LICITACIONES:
He proveído una lista adicional en el tag `<LICITACIONES>`. DEBES incluirlas todas de forma destacada dentro de la categoría ""Licitaciones y CAPEX"". 
Si vienen licitaciones, dales formato indicando la Entidad, el Monto Estimado (si existe) y un botón/enlace a la fuente de la Licitación. Trata cada licitación como Oportunidad Comercial Alta 🔴.
- 🏢 Movimientos de la Competencia (SIEMPRE INCLUYE. Busca menciones a: {competitors}. Si no hay noticias, escribe: Sin noticias significativas de la competencia en este período.)
- 🎯 Oportunidades Comerciales (Solo si hay eventos verificables: licitaciones abiertas, adjudicaciones, cierres financieros, nuevos proyectos)

ORDEN:
- Dentro de cada categoría, ordena las noticias por fecha, de la más reciente a la más antigua.
- FOCO LOCAL ESTRICTO: Prioriza y analiza el impacto de estas noticias específicamente para el mercado y la industria de {countryConfig.Name}.
- INCLUSION TOTAL: Las noticias enviadas ya fueron rigurosamente pre-filtradas. DEBES INCLUIR EXACTAMENTE EL 100% de las noticias en el reporte, sin omitir ni recortar ninguna, garantizando que el listado coincida con los KPI calculados previamente.
- EXHAUSTIVIDAD DE MARCA: Asegúrate de destacar claramente las menciones a Nexans, Centelsa, Indeco, Madeco, Ficap o Incable. NO omitas ninguna noticia que mencione a la marca.

Formato HTML estricto (HTML válido y bien formado):
<h1>Reporte Ejecutivo: {countryConfig.Name}</h1>
Bajo el título del país, crea un <h2> por Categoría (Ej: <h2>Energía y Redes</h2>). NO agregues atributos style a este h2.
Bajo cada categoría, lista las noticias usando <ul><li>.
Usa 🟢, 🟡, o 🔴 al inicio de cada <li> según el sentimiento.
Formato de cada noticia: <li>[Emoji] <strong>Título</strong>: Resumen breve pero con datos duros. [Fecha] <a href='URL'>Enlace</a></li>

Semáforo (relevancia comercial para un vendedor de cables):
- 🔴 Alta: Oportunidad de venta directa (ej. licitación abierta, adjudicación confirmada, construcción de planta, nueva subestación o nueva línea de transmisión, data center anunciado con inversión/contratación)
- 🟡 Media: Contexto de industria, regulación o inversión que podría desencadenar demanda futura
- 🟢 Baja: Señal de mercado a monitorear (si la señal no tiene relación con infraestructura/energía/minería, DESCÁRTALA)

AL INICIO (ANTES DE LAS CATEGORÍAS):
El sistema ha calculado un puntaje de ""Sentimiento"" de la marca de {(int)metrics.AverageSentiment} sobre 100 y detectado {metrics.NexansMentions} menciones de la marca. 
Genera un bloque destacando el puntaje:
<div style='background-color: #1a1a2e; color: white; padding: 15px; border-radius: 4px; margin-bottom: 20px;'>
    <h3 style='color: white; margin-top: 0;'>ANÁLISIS DE SENTIMIENTO ({(int)metrics.AverageSentiment}/100)</h3>
    <p style='margin-bottom: 0;'>{{Explicación de 2-3 líneas sobre el puntaje basada en los artículos}}</p>
</div>

AL FINAL:
Agrega <h2>Recomendaciones Estratégicas del Gerente de Marketing – {countryConfig.Name}</h2>.
Asume el rol de Gerente de Marketing para {countryConfig.Name} y proporciona 1-2 párrafos sobre cómo las noticias representan oportunidades o riesgos para nuestra empresa (fabricante de cables y soluciones eléctricas/telecom) y qué acciones de prospección sugieres.

REGLAS PARA RECOMENDACIONES:
- Sé MUY específico, mencionando únicamente empresas, proyectos o licitaciones que aparezcan en el reporte.
- Si no hay suficiente información para recomendar acciones concretas, dilo explícitamente y sugiere qué señales monitorear mañana.

SALIDA:
- Devuelve SOLO el HTML.
- NO uses markdown.
- NO agregues saludos, títulos extra fuera del HTML ni explicaciones adicionales.";
        
        var articlesJson = JsonSerializer.Serialize(articles.Select(a => new 
        {
            a.Title,
            a.Url,
            a.PublishedAt,
            a.Summary
        }));

        var tendersJson = JsonSerializer.Serialize(tenders.Select(t => new 
        {
            t.Title,
            t.Entity,
            t.EstimatedValue,
            t.Currency,
            t.Url
        }));

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemInstruction } }
            },
            contents = new[]
            {
                new { parts = new[] { new { text = $"<ARTICULOS>\n{articlesJson}\n</ARTICULOS>\n\n<LICITACIONES>\n{tendersJson}\n</LICITACIONES>" } } }
            },
            generationConfig = new { temperature = 0.2 }
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
