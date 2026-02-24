namespace CableNews.Application.News.Commands;

using MediatR;
using Microsoft.Extensions.Logging;
using CableNews.Application.Common.Interfaces;
using CableNews.Domain.Entities;

public class GenerateNewsletterCommandHandler : IRequestHandler<GenerateNewsletterCommand, bool>
{
    private readonly INewsFeedProvider _newsProvider;
    private readonly ILlmSummarizerService _summarizer;
    private readonly IEmailService _emailService;
    private readonly ILogger<GenerateNewsletterCommandHandler> _logger;

    public GenerateNewsletterCommandHandler(
        INewsFeedProvider newsProvider,
        ILlmSummarizerService summarizer,
        IEmailService emailService,
        ILogger<GenerateNewsletterCommandHandler> logger)
    {
        _newsProvider = newsProvider;
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

            _logger.LogInformation("Summarizing {Count} articles for {CountryName} using LLM...", articles.Count, country.Name);
            var summaryHtml = await _summarizer.SummarizeArticlesAsync(articles, country, cancellationToken);

            if (string.IsNullOrWhiteSpace(summaryHtml))
            {
                _logger.LogWarning("LLM returned an empty summary for {CountryName}. Skipping email.", country.Name);
                continue;
            }

            _logger.LogInformation("Sending executive newsletter for {CountryName} via Email...", country.Name);
            await _emailService.SendNewsletterAsync(summaryHtml, country.Name, country.LocalNexansBrand ?? country.Name, country.BrandColor ?? "#E1251B", cancellationToken);
            
            _logger.LogInformation("Newsletter for {CountryName} generated and sent successfully.", country.Name);
            anySuccess = true;
        }

        return anySuccess;
    }
}
