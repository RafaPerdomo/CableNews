namespace CableNews.Application.News.Commands;

using FluentValidation;

public class GenerateNewsletterCommandValidator : AbstractValidator<GenerateNewsletterCommand>
{
    public GenerateNewsletterCommandValidator()
    {
        RuleFor(v => v.Config).NotNull();
        RuleFor(v => v.Config.Countries).NotEmpty().WithMessage("At least one country must be configured.");
        RuleFor(v => v.Config.BaseQueryTemplate).NotEmpty().WithMessage("Base query template is required.");
    }
}
