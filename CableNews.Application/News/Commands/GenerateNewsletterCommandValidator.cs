namespace CableNews.Application.News.Commands;

using FluentValidation;

public class GenerateNewsletterCommandValidator : AbstractValidator<GenerateNewsletterCommand>
{
    public GenerateNewsletterCommandValidator()
    {
        RuleFor(v => v.Config).NotNull();
        RuleFor(v => v.Config.Countries).NotEmpty().WithMessage("At least one country must be configured.").When(v => v.Config is not null);
        RuleFor(v => v.Config.BaseQueryTemplate).NotEmpty().WithMessage("Base query template is required.").When(v => v.Config is not null);
    }
}
