using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CableNews.Application.Common.Interfaces;
using CableNews.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CableNews.Infrastructure.Services.Tenders;

public class SecopTenderProvider : ITenderProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SecopTenderProvider> _logger;

    public string ProviderName => "SECOP-II";

    public SecopTenderProvider(HttpClient httpClient, ILogger<SecopTenderProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<TenderResult>> FetchTendersAsync(
        CountryConfig country,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (country.Code != "CO") return [];

        var keywords = new[] { "cable", "eléctrico", "transmisión", "subestación", "fibra óptica", "instalaciones eléctricas" };
        var results = new List<TenderResult>();

        foreach (var keyword in keywords)
        {
            var sinceStr = since.ToString("yyyy-MM-ddTHH:mm:ss.000");
            var whereClause = $"objeto_del_contrato like '%{keyword}%' AND fecha_de_firma >= '{sinceStr}'";
            var url = $"https://www.datos.gov.co/resource/jbjy-vk9h.json?$where={Uri.EscapeDataString(whereClause)}&$limit=10&$order=fecha_de_firma DESC";

            try
            {
                var tenders = await _httpClient.GetFromJsonAsync<List<SecopRecord>>(url, cancellationToken);
                if (tenders is null) continue;

                foreach (var t in tenders)
                {
                    results.Add(new TenderResult
                    {
                        TenderId = t.ProcesoDeCompra ?? "",
                        Title = t.ObjetoDelContrato ?? "",
                        Entity = t.NombreEntidad ?? "",
                        Url = t.UrlProceso ?? $"https://community.secop.gov.co/Public/Tendering/OpportunityDetail/Index?noticeUID={t.ProcesoDeCompra}",
                        EstimatedValue = t.ValorDelContrato,
                        Currency = "COP",
                        PublishedAt = t.FechaDeFirma ?? DateTimeOffset.UtcNow,
                        CountryCode = "CO"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SECOP query failed for {Keyword}: {Msg}", keyword, ex.Message);
            }
        }

        return results.DistinctBy(t => t.TenderId).ToList();
    }

    private record SecopRecord
    {
        [JsonPropertyName("proceso_de_compra")]
        public string? ProcesoDeCompra { get; init; }
        
        [JsonPropertyName("objeto_del_contrato")]
        public string? ObjetoDelContrato { get; init; }
        
        [JsonPropertyName("nombre_entidad")]
        public string? NombreEntidad { get; init; }
        
        [JsonPropertyName("urlproceso")]
        public string? UrlProceso { get; init; }
        
        [JsonPropertyName("valor_del_contrato")]
        public decimal? ValorDelContrato { get; init; }
        
        [JsonPropertyName("fecha_de_firma")]
        public DateTimeOffset? FechaDeFirma { get; init; }
    }
}
