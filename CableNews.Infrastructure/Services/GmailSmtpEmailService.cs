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

    public async Task SendNewsletterAsync(string htmlContent, string countryName, string localBrand, string brandColor, CancellationToken cancellationToken)
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
                message.To.Add(new MailboxAddress("Subscriber", recipient));
        }

        message.Subject = $"📰 CableNews Report – {countryName} – {DateTime.Now:yyyy-MM-dd}";

        var bodyContent = System.Text.RegularExpressions.Regex.Replace(
            htmlContent, "<h1[^>]*>.*?</h1>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        var dateStr = DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-CO"));
        var brandLabel = string.IsNullOrWhiteSpace(localBrand) || localBrand == countryName ? "Nexans" : localBrand;
        var color = string.IsNullOrWhiteSpace(brandColor) ? "#E1251B" : brandColor;

        var styledHtml = $$"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
            <meta charset="UTF-8">
            <style>
              body { font-family: 'Segoe UI', Arial, sans-serif; background:#f0f2f5; margin:0; padding:0; color:#1a1a2e; }
              .wrapper { max-width:960px; margin:20px auto; background:#fff; border-radius:10px; box-shadow:0 2px 14px rgba(0,0,0,0.08); overflow:hidden; }
              .header { background:{{color}}; color:#fff; padding:28px 40px 22px; }
              .brand-line { font-size:11px; font-weight:700; letter-spacing:2.5px; text-transform:uppercase; color:rgba(255,255,255,0.75); margin:0 0 8px; }
              .header h1 { margin:0 0 6px; font-size:22px; font-weight:700; letter-spacing:0.3px; }
              .header p { color:rgba(255,255,255,0.65); font-size:12px; margin:0; }
              .content { padding:0 40px 32px; }
              h2 { font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:1.2px; color:#fff; background:#1a1a2e; margin:24px -40px 12px; padding:10px 40px; border-left:4px solid {{color}}; }
              ul { margin:0; padding:6px 0 4px 16px; }
              li { margin-bottom:10px; font-size:13.5px; line-height:1.65; border-left:3px solid #e2e8f4; padding:5px 10px 5px 12px; }
              a { color:{{color}}; font-weight:600; text-decoration:none; }
              strong { color:#1a1a2e; }
              p { font-size:13.5px; line-height:1.7; margin:8px 0; }
              .footer { text-align:center; font-size:11px; color:#aaa; padding:14px; background:#f0f2f5; border-top:1px solid #e2e8f4; }
            </style>
            </head>
            <body>
            <div class="wrapper">
              <div class="header">
                <p class="brand-line">{{brandLabel}}</p>
                <h1>&#128225; Reporte Ejecutivo &mdash; {{countryName}}</h1>
                <p>{{dateStr}}</p>
              </div>
              <div class="content">
                {{bodyContent}}
              </div>
              <div class="footer">CableNews Agent &mdash; Uso interno &mdash; Confidencial</div>
            </div>
            </body>
            </html>
            """;

        var bodyBuilder = new BodyBuilder { HtmlBody = styledHtml };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_config.Host, _config.Port, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(_config.Username, _config.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Email sent successfully to {Recipient}", _config.Username);
    }
}
