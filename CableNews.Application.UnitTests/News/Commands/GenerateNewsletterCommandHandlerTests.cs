namespace CableNews.Application.UnitTests.News.Commands;

using CableNews.Application.Common.Interfaces;
using CableNews.Application.Common.Models;
using CableNews.Application.News.Commands;
using CableNews.Domain.Entities;
using CableNews.Domain.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;

[TestFixture]
public class GenerateNewsletterCommandHandlerTests
{
    private Mock<INewsFeedProvider> _newsProvider = null!;
    private Mock<ILlmClassifierService> _classifier = null!;
    private Mock<IPrMetricsService> _metricsService = null!;
    private Mock<ILlmSummarizerService> _summarizer = null!;
    private Mock<IEmailService> _emailService = null!;
    private Mock<ILogger<GenerateNewsletterCommandHandler>> _logger = null!;
    private GenerateNewsletterCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _newsProvider = new Mock<INewsFeedProvider>();
        _classifier = new Mock<ILlmClassifierService>();
        _metricsService = new Mock<IPrMetricsService>();
        _summarizer = new Mock<ILlmSummarizerService>();
        _emailService = new Mock<IEmailService>();
        _logger = new Mock<ILogger<GenerateNewsletterCommandHandler>>();

        _handler = new GenerateNewsletterCommandHandler(
            _newsProvider.Object,
            Enumerable.Empty<ITenderProvider>(),
            _classifier.Object,
            _metricsService.Object,
            _summarizer.Object,
            _emailService.Object,
            _logger.Object);
    }

    [Test]
    public async Task Handle_NoArticlesFetched_ShouldReturnFalse()
    {
        var config = CreateConfig();
        _newsProvider
            .Setup(x => x.FetchNewsAsync(It.IsAny<CountryConfig>(), It.IsAny<NewsAgentConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Article>());

        var result = await _handler.Handle(new GenerateNewsletterCommand(config), CancellationToken.None);

        result.ShouldBeFalse();
        _classifier.Verify(x => x.ClassifyArticlesAsync(It.IsAny<List<Article>>(), It.IsAny<CountryConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_FullPipeline_ShouldReturnTrue()
    {
        var config = CreateConfig();
        var articles = new List<Article>
        {
            new() { Hash = "abc123", Title = "Test Article", Url = "https://example.com", CountryCode = "CO", PublishedAt = DateTime.UtcNow }
        };
        var analyses = new List<ArticleAnalysis>
        {
            new() { ArticleHash = "abc123", Sentiment = SentimentType.Positive, SentimentScore = 80, Category = "Energy" }
        };
        var metrics = new PrMetricsReport
        {
            ShareOfVoice = 50, CompetitorShareOfVoice = 30, AverageSentiment = 75,
            TotalArticles = 1, RelevantArticles = 1, NexansMentions = 1, TopCompetitor = "Prysmian"
        };

        _newsProvider
            .Setup(x => x.FetchNewsAsync(It.IsAny<CountryConfig>(), It.IsAny<NewsAgentConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(articles);
        _newsProvider
            .Setup(x => x.ResolveUrlsAsync(It.IsAny<List<Article>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(articles);
        _classifier
            .Setup(x => x.ClassifyArticlesAsync(It.IsAny<List<Article>>(), It.IsAny<CountryConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(analyses);
        _metricsService
            .Setup(x => x.CalculateMetricsAsync(It.IsAny<List<ArticleAnalysis>>(), It.IsAny<int>(), It.IsAny<CountryConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);
        _summarizer
            .Setup(x => x.SummarizeArticlesAsync(It.IsAny<List<Article>>(), It.IsAny<PrMetricsReport>(), It.IsAny<List<TenderResult>>(), It.IsAny<CountryConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<h1>Newsletter</h1><p>Content</p>");

        var result = await _handler.Handle(new GenerateNewsletterCommand(config), CancellationToken.None);

        result.ShouldBeTrue();
        _emailService.Verify(x => x.SendNewsletterAsync(It.IsAny<string>(), "Colombia", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_EmptySummary_ShouldSkipCountryAndReturnFalse()
    {
        var config = CreateConfig();
        var articles = new List<Article>
        {
            new() { Hash = "abc123", Title = "Test Article", Url = "https://example.com", CountryCode = "CO", PublishedAt = DateTime.UtcNow }
        };
        var analyses = new List<ArticleAnalysis>
        {
            new() { ArticleHash = "abc123", Sentiment = SentimentType.Neutral, SentimentScore = 50, Category = "General" }
        };
        var metrics = new PrMetricsReport
        {
            ShareOfVoice = 0, CompetitorShareOfVoice = 0, AverageSentiment = 50,
            TotalArticles = 1, RelevantArticles = 1, NexansMentions = 0, TopCompetitor = "Ninguno"
        };

        _newsProvider
            .Setup(x => x.FetchNewsAsync(It.IsAny<CountryConfig>(), It.IsAny<NewsAgentConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(articles);
        _newsProvider
            .Setup(x => x.ResolveUrlsAsync(It.IsAny<List<Article>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(articles);
        _classifier
            .Setup(x => x.ClassifyArticlesAsync(It.IsAny<List<Article>>(), It.IsAny<CountryConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(analyses);
        _metricsService
            .Setup(x => x.CalculateMetricsAsync(It.IsAny<List<ArticleAnalysis>>(), It.IsAny<int>(), It.IsAny<CountryConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);
        _summarizer
            .Setup(x => x.SummarizeArticlesAsync(It.IsAny<List<Article>>(), It.IsAny<PrMetricsReport>(), It.IsAny<List<TenderResult>>(), It.IsAny<CountryConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var result = await _handler.Handle(new GenerateNewsletterCommand(config), CancellationToken.None);

        result.ShouldBeFalse();
        _emailService.Verify(x => x.SendNewsletterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static NewsAgentConfig CreateConfig()
    {
        return new NewsAgentConfig
        {
            BaseQueryTemplate = "cable eléctrico",
            Countries =
            [
                new CountryConfig
                {
                    Code = "CO",
                    Name = "Colombia",
                    LocalNexansBrand = "Centelsa",
                    BrandColor = "#E1251B"
                }
            ]
        };
    }
}
