namespace CableNews.Application.News.Commands;

using MediatR;
using Microsoft.Extensions.Logging;
using CableNews.Application.Common.Interfaces;
using CableNews.Application.Common.Models;
using CableNews.Domain.Entities;
using System.Collections.Concurrent;

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

        var successFlags = new ConcurrentBag<bool>();
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            request.Config.Countries,
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellationToken },
            async (country, ct) =>
            {
                var countrySw = System.Diagnostics.Stopwatch.StartNew();
                _logger.LogInformation("--- Processing Newsletter for {CountryName} ({CountryCode}) ---", country.Name, country.Code);

                try
                {
                    var articles = await _newsProvider.FetchNewsAsync(country, request.Config, ct);

                    if (articles.Count == 0)
                    {
                        _logger.LogWarning("No articles fetched for {CountryName}. Skipping.", country.Name);
                        return;
                    }

                    _logger.LogInformation("Classifying {Count} articles for {CountryName} (Agent 1)...", articles.Count, country.Name);
                    var analyses = await _classifier.ClassifyArticlesAsync(articles, country, ct);

                    _logger.LogInformation("Calculating PR Metrics for {CountryName} (Agent 2)...", country.Name);
                    var metrics = await _metricsService.CalculateMetricsAsync(analyses, articles.Count, country, ct);

                    var relevantHashes = analyses.Select(a => a.ArticleHash).ToHashSet();
                    var relevantArticles = articles.Where(a => relevantHashes.Contains(a.Hash)).ToList();

                    _logger.LogInformation("Resolving URLs for {Count} relevant articles (of {Total} fetched) for {CountryName}...",
                        relevantArticles.Count, articles.Count, country.Name);
                    relevantArticles = await _newsProvider.ResolveUrlsAsync(relevantArticles, ct);

                    var tenderTasks = _tenderProviders.Select(async provider =>
                    {
                        try
                        {
                            return await provider.FetchTendersAsync(country, DateTimeOffset.UtcNow.AddDays(-30), ct);
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

                    var analysisMap = analyses.ToDictionary(a => a.ArticleHash);
                    var analyzedArticles = relevantArticles
                        .Select(art => new AnalyzedArticle(art, analysisMap[art.Hash]))
                        .ToList();

                    _logger.LogInformation("Summarizing {Count} relevant articles for {CountryName} (Agent 3)...", analyzedArticles.Count, country.Name);
                    var summaryHtml = await _summarizer.SummarizeArticlesAsync(analyzedArticles, metrics, tenders, country, ct);

                    if (string.IsNullOrEmpty(summaryHtml))
                    {
                        _logger.LogWarning("LLM returned empty summary for {CountryName}.", country.Name);
                        return;
                    }

                    var kpiHtml = CableNews.Application.Common.Utils.EmailKpiCardRenderer.RenderKpiBar(metrics, country.BrandColor ?? "#E1251B");
                    var glossaryHtml = CableNews.Application.Common.Utils.EmailKpiCardRenderer.RenderGlossary();

                    var finalHtml = summaryHtml.Replace("</h1>", $"</h1>\n{kpiHtml}\n");
                    finalHtml += glossaryHtml;

                    _logger.LogInformation("Sending executive newsletter for {CountryName} via Email...", country.Name);
                    await _emailService.SendNewsletterAsync(
                        finalHtml, 
                        country.Name, 
                        country.LocalNexansBrand ?? country.Name, 
                        country.BrandColor ?? "#E1251B", 
                        request.Config.Timezone, 
                        country.EmailRecipients, 
                        ct);

                    successFlags.Add(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating newsletter for {CountryName}", country.Name);
                }
                finally
                {
                    countrySw.Stop();
                    _logger.LogInformation("Newsletter for {CountryName} completed in {ElapsedMs}ms.", country.Name, countrySw.ElapsedMilliseconds);
                }
            });

        totalSw.Stop();
        _logger.LogInformation("All newsletters completed in {ElapsedMs}ms.", totalSw.ElapsedMilliseconds);

        return successFlags.Any(s => s);
    }
}
