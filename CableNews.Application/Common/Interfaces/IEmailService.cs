namespace CableNews.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendNewsletterAsync(string htmlContent, string countryName, CancellationToken cancellationToken);
}
