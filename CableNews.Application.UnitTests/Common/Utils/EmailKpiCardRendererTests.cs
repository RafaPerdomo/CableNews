namespace CableNews.Application.UnitTests.Common.Utils;

using CableNews.Application.Common.Models;
using CableNews.Application.Common.Utils;
using Shouldly;

[TestFixture]
public class EmailKpiCardRendererTests
{
    [Test]
    public void RenderKpiBar_HighSentiment_ShouldUseGreenColor()
    {
        var metrics = new PrMetricsReport
        {
            ShareOfVoice = 45,
            CompetitorShareOfVoice = 30,
            AverageSentiment = 85,
            TotalArticles = 120,
            RelevantArticles = 18,
            NexansMentions = 5,
            TopCompetitor = "Prysmian"
        };

        var html = EmailKpiCardRenderer.RenderKpiBar(metrics, "#E1251B");

        html.ShouldContain("#27ae60");
        html.ShouldContain("45%");
        html.ShouldContain("Prysmian");
    }

    [Test]
    public void RenderKpiBar_LowSentiment_ShouldUseRedColor()
    {
        var metrics = new PrMetricsReport
        {
            ShareOfVoice = 10,
            CompetitorShareOfVoice = 60,
            AverageSentiment = 25,
            TotalArticles = 50,
            RelevantArticles = 3,
            NexansMentions = 1,
            TopCompetitor = "Centelsa"
        };

        var html = EmailKpiCardRenderer.RenderKpiBar(metrics, "#E1251B");

        html.ShouldContain("#e74c3c");
        html.ShouldContain("10%");
    }

    [Test]
    public void RenderGlossary_ShouldContainAllMetricDefinitions()
    {
        var html = EmailKpiCardRenderer.RenderGlossary();

        html.ShouldContain("Share of Voice");
        html.ShouldContain("Noticias en Radar");
        html.ShouldContain("Selección Ejecutiva");
        html.ShouldContain("Sentimiento");
        html.ShouldNotBeNullOrWhiteSpace();
    }
}
