namespace CableNews.Infrastructure.Services;

using Ardalis.GuardClauses;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using CableNews.Application.Common.Interfaces;
using CableNews.Infrastructure.Configuration;

public class GmailSmtpEmailService : IEmailService
{
    private readonly SmtpConfig _config;
    private readonly ILogger<GmailSmtpEmailService> _logger;

    public GmailSmtpEmailService(IOptions<SmtpConfig> config, ILogger<GmailSmtpEmailService> logger)
    {
        _config = Guard.Against.Null(config.Value);
        _logger = logger;
    }

    public async Task SendNewsletterAsync(string htmlContent, string countryName, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Cable News Agent", _config.Username));
        
        if (_config.Recipients == null || _config.Recipients.Count == 0)
        {
            _logger.LogWarning("No recipients configured. Email will be sent to the sender account ({Sender}).", _config.Username);
            message.To.Add(new MailboxAddress("Executive Review", _config.Username));
        }
        else
        {
            foreach (var recipient in _config.Recipients)
            {
                message.To.Add(new MailboxAddress("Subscriber", recipient));
            }
        }

        message.Subject = $"📰 CableNews Report – {countryName} – {DateTime.Now:yyyy-MM-dd}";

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlContent };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_config.Host, _config.Port, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(_config.Username, _config.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Email sent successfully to {Recipient}", _config.Username);
    }
}
