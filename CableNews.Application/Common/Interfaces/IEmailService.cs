namespace CableNews.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendNewsletterAsync(string htmlContent, string countryName, string localBrand, string brandColor, CancellationToken cancellationToken);
}
