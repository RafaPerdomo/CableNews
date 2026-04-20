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
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var country in request.Config.Countries)
        {
            var countrySw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("--- Processing Newsletter for {CountryName} ({CountryCode}) ---", country.Name, country.Code);

            var articles = await _newsProvider.FetchNewsAsync(country, request.Config, cancellationToken);

            if (articles.Count == 0)
            {
                _logger.LogWarning("No articles fetched for {CountryName}. Skipping.", country.Name);
                continue;
            }

            _logger.LogInformation("Classifying {Count} articles for {CountryName} (Agent 1)...", articles.Count, country.Name);
            var analyses = await _classifier.ClassifyArticlesAsync(articles, country, cancellationToken);

            _logger.LogInformation("Calculating PR Metrics for {CountryName} (Agent 2)...", country.Name);
            var metrics = await _metricsService.CalculateMetricsAsync(analyses, articles.Count, country, cancellationToken);

            var relevantHashes = analyses.Select(a => a.ArticleHash).ToHashSet();
            var relevantArticles = articles.Where(a => relevantHashes.Contains(a.Hash)).ToList();

            _logger.LogInformation("Resolving URLs for {Count} relevant articles (of {Total} fetched) for {CountryName}...",
                relevantArticles.Count, articles.Count, country.Name);
            relevantArticles = await _newsProvider.ResolveUrlsAsync(relevantArticles, cancellationToken);

            var tenderTasks = _tenderProviders.Select(async provider =>
            {
                try
                {
                    return await provider.FetchTendersAsync(country, DateTimeOffset.UtcNow.AddDays(-30), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error fetching tenders from {ProviderName} for {CountryName}", provider.ProviderName, country.Name);
                    return new List<TenderResult>();
                }
            });

            var tenderResults = await Task.WhenAll(tenderTasks);
            var tenders = tenderResults.SelectMany(t => t).ToList();
            _logger.LogInformation("Found {Count} active Tenders for {CountryName}.", tenders.Count, country.Name);

            _logger.LogInformation("Summarizing {Count} relevant articles for {CountryName} (Agent 3)...", relevantArticles.Count, country.Name);
            var summaryHtml = await _summarizer.SummarizeArticlesAsync(relevantArticles, metrics, tenders, country, cancellationToken);

            if (string.IsNullOrEmpty(summaryHtml))
            {
                _logger.LogWarning("LLM returned empty summary for {CountryName}.", country.Name);
                continue;
            }

            var kpiHtml = CableNews.Application.Common.Utils.EmailKpiCardRenderer.RenderKpiBar(metrics, country.BrandColor ?? "#E1251B");
            var glossaryHtml = CableNews.Application.Common.Utils.EmailKpiCardRenderer.RenderGlossary();

            var finalHtml = summaryHtml.Replace("</h1>", $"</h1>\n{kpiHtml}\n");
            finalHtml += glossaryHtml;

            _logger.LogInformation("Sending executive newsletter for {CountryName} via Email...", country.Name);
            await _emailService.SendNewsletterAsync(finalHtml, country.Name, country.LocalNexansBrand ?? country.Name, country.BrandColor ?? "#E1251B", cancellationToken);

            countrySw.Stop();
            _logger.LogInformation("Newsletter for {CountryName} completed in {ElapsedMs}ms.", country.Name, countrySw.ElapsedMilliseconds);
            anySuccess = true;
        }

        totalSw.Stop();
        _logger.LogInformation("All newsletters completed in {ElapsedMs}ms.", totalSw.ElapsedMilliseconds);

        return anySuccess;
    }
}
