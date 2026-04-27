namespace CableNews.Application.UnitTests.Common.Behaviours;

using CableNews.Application.Common.Behaviours;
using CableNews.Application.Common.Models;
using CableNews.Application.News.Commands;
using CableNews.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Moq;
using Shouldly;

[TestFixture]
public class ValidationBehaviourTests
{
    [Test]
    public async Task Handle_ValidRequest_ShouldCallNext()
    {
        var validators = new List<IValidator<GenerateNewsletterCommand>>
        {
            CreatePassingValidator()
        };
        var behaviour = new ValidationBehaviour<GenerateNewsletterCommand, bool>(validators);
        var config = new NewsAgentConfig
        {
            BaseQueryTemplate = "cable",
            Countries = [new CountryConfig { Code = "CO", Name = "Colombia" }]
        };
        var command = new GenerateNewsletterCommand(config);
        var nextCalled = false;

        var result = await behaviour.Handle(
            command,
            new RequestHandlerDelegate<bool>(async (ct) => { nextCalled = true; return true; }),
            CancellationToken.None);

        result.ShouldBeTrue();
        nextCalled.ShouldBeTrue();
    }

    [Test]
    public async Task Handle_InvalidRequest_ShouldThrowValidationException()
    {
        var validators = new List<IValidator<GenerateNewsletterCommand>>
        {
            CreateFailingValidator("Config.Countries", "At least one country must be configured.")
        };
        var behaviour = new ValidationBehaviour<GenerateNewsletterCommand, bool>(validators);
        var config = new NewsAgentConfig { BaseQueryTemplate = "cable", Countries = [] };
        var command = new GenerateNewsletterCommand(config);

        var exception = await Should.ThrowAsync<ValidationException>(async () =>
            await behaviour.Handle(
                command,
                new RequestHandlerDelegate<bool>(async (ct) => false),
                CancellationToken.None));

        exception.Errors.ShouldContain(e => e.PropertyName == "Config.Countries");
    }

    private static IValidator<GenerateNewsletterCommand> CreatePassingValidator()
    {
        var mock = new Mock<IValidator<GenerateNewsletterCommand>>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<GenerateNewsletterCommand>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        return mock.Object;
    }

    private static IValidator<GenerateNewsletterCommand> CreateFailingValidator(string propertyName, string errorMessage)
    {
        var mock = new Mock<IValidator<GenerateNewsletterCommand>>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<GenerateNewsletterCommand>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure(propertyName, errorMessage) }));
        return mock.Object;
    }
}
