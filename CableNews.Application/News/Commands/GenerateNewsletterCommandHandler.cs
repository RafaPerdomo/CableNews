namespace CableNews.Application.News.Commands;

using MediatR;
using Microsoft.Extensions.Logging;
using CableNews.Application.Common.Interfaces;
using CableNews.Domain.Entities;

public class GenerateNewsletterCommandHandler : IRequestHandler<GenerateNewsletterCommand, bool>
{
    private readonly INewsFeedProvider _newsProvider;
    private readonly IEnumerable<ITenderProvider> _tenderProviders;
    private readonly ILlmClassifierService _classifier;
    private readonly IPrMetricsService _metricsService;
    private readonly ILlmSummarizerService _summarizer;
    private readonly IEmailService _emailService;
    private readonly ILogger<GenerateNewsletterCommandHandler> _logger;

    public GenerateNewsletterCommandHandler(
        INewsFeedProvider newsProvider,
        IEnumerable<ITenderProvider> tenderProviders,
        ILlmClassifierService classifier,
        IPrMetricsService metricsService,
        ILlmSummarizerService summarizer,
        IEmailService emailService,
        ILogger<GenerateNewsletterCommandHandler> logger)
    {
        _newsProvider = newsProvider;
        _tenderProviders = tenderProviders;
        _classifier = classifier;
        _metricsService = metricsService;
        _summarizer = summarizer;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<bool> Handle(GenerateNewsletterCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating newsletter for {Count} countries", request.Config.Countries.Count);

        var anySuccess = false;

        foreach (var country in request.Config.Countries)
        {
            _logger.LogInformation("--- Processing Newsletter for {CountryName} ({CountryCode}) ---", country.Name, country.Code);
            
            var articles = await _newsProvider.FetchNewsAsync(country, request.Config, cancellationToken);
            
            if (articles.Count == 0)
            {
                _logger.LogWarning("No articles fetched for {CountryName}. Skipping.", country.Name);
                continue;
            }

            _logger.LogInformation("Classifying {Count} articles with Gemini 3.1 Pro (Agent 1)...", articles.Count);
            var analyses = await _classifier.ClassifyArticlesAsync(articles, country, cancellationToken);
            
            _logger.LogInformation("Calculating PR Metrics for {CountryName} (Agent 2)...", country.Name);
            var metrics = await _metricsService.CalculateMetricsAsync(analyses, articles.Count, country, cancellationToken);

            _logger.LogInformation("Summarizing relevant articles for {CountryName} using LLM (Agent 3)...", country.Name);
            // We pass the subset of relevant articles based on the hashes returned by the classifier along with the metrics
            var relevantHashes = analyses.Select(a => a.ArticleHash).ToHashSet();
            var relevantArticles = articles.Where(a => relevantHashes.Contains(a.Hash)).ToList();
            
            _logger.LogInformation("Fetching Tenders via Open APIs for {CountryName}...", country.Name);
            var tenders = new List<TenderResult>();
            foreach (var provider in _tenderProviders)
            {
                try
                {
                    // Fetch tenders from 30 days ago to guarantee data capture for the test
                    var providerTenders = await provider.FetchTendersAsync(country, DateTimeOffset.UtcNow.AddDays(-30), cancellationToken);
                    tenders.AddRange(providerTenders);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error fetching tenders from {ProviderName} for {CountryName}", provider.ProviderName, country.Name);
                }
            }
            _logger.LogInformation("Found {Count} active Tenders for {CountryName}.", tenders.Count, country.Name);

            var summaryHtml = await _summarizer.SummarizeArticlesAsync(relevantArticles, metrics, tenders, country, cancellationToken);

            if (string.IsNullOrEmpty(summaryHtml))
            {
                _logger.LogWarning("LLM returned empty summary for {CountryName}.", country.Name);
                continue;
            }

            // Inject KPI Cards and Glossary
            var kpiHtml = CableNews.Application.Common.Utils.EmailKpiCardRenderer.RenderKpiBar(metrics, country.BrandColor ?? "#E1251B");
            var glossaryHtml = CableNews.Application.Common.Utils.EmailKpiCardRenderer.RenderGlossary();
            
            var finalHtml = summaryHtml.Replace("</h1>", $"</h1>\n{kpiHtml}\n");
            finalHtml += glossaryHtml;

            _logger.LogInformation("Sending executive newsletter for {CountryName} via Email...", country.Name);
            await _emailService.SendNewsletterAsync(finalHtml, country.Name, country.LocalNexansBrand ?? country.Name, country.BrandColor ?? "#E1251B", cancellationToken);
            
            _logger.LogInformation("Newsletter for {CountryName} generated and sent successfully.", country.Name);
            anySuccess = true;
        }

        return anySuccess;
    }
}
