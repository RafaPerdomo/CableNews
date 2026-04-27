namespace CableNews.Application.UnitTests.News.Commands;

using CableNews.Application.Common.Models;
using CableNews.Application.News.Commands;
using CableNews.Domain.Entities;
using FluentValidation.TestHelper;

[TestFixture]
public class GenerateNewsletterCommandValidatorTests
{
    private GenerateNewsletterCommandValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new GenerateNewsletterCommandValidator();
    }

    [Test]
    public void Validate_NullConfig_ShouldHaveValidationError()
    {
        var command = new GenerateNewsletterCommand(null!);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Config);
    }

    [Test]
    public void Validate_EmptyCountries_ShouldHaveValidationError()
    {
        var config = new NewsAgentConfig
        {
            BaseQueryTemplate = "cable eléctrico",
            Countries = []
        };
        var command = new GenerateNewsletterCommand(config);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Config.Countries)
              .WithErrorMessage("At least one country must be configured.");
    }

    [Test]
    public void Validate_MissingBaseQueryTemplate_ShouldHaveValidationError()
    {
        var config = new NewsAgentConfig
        {
            BaseQueryTemplate = string.Empty,
            Countries = [new CountryConfig { Code = "CO", Name = "Colombia" }]
        };
        var command = new GenerateNewsletterCommand(config);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Config.BaseQueryTemplate)
              .WithErrorMessage("Base query template is required.");
    }
}
