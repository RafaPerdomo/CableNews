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

        bodyContent = bodyContent.Replace("<h2>", $"<div style=\"background-color:#1a1a2e; border-top:4px solid {color}; padding:12px 15px; margin:30px 0 15px 0;\"><h2 style=\"margin:0; font-size:14px; font-weight:bold; text-transform:uppercase; letter-spacing:1px; color:#ffffff; line-height:1.2;\">");
        bodyContent = bodyContent.Replace("</h2>", "</h2></div>");
        bodyContent = bodyContent.Replace("<ul>", "<ul style=\"margin:0 0 20px 0; padding:0 0 0 20px;\">");
        bodyContent = bodyContent.Replace("<li>", "<li style=\"margin-bottom:12px; font-size:14px; line-height:1.6; color:#1a1a2e;\">");
        bodyContent = bodyContent.Replace("<p>", "<p style=\"font-size:14px; line-height:1.6; margin:0 0 15px 0; color:#1a1a2e;\">");
        bodyContent = bodyContent.Replace("<a href=", $"<a style=\"color:{color}; font-weight:bold; text-decoration:none; border-bottom:1px solid {color};\" href=");
        bodyContent = bodyContent.Replace("<strong>", "<strong style=\"color:#000000;\">");

        var styledHtml = $$"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            </head>
            <body style="margin:0; padding:0; background-color:#f0f2f5; font-family:'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; color:#1a1a2e;">
              <table border="0" cellpadding="0" cellspacing="0" width="100%" style="background-color:#f0f2f5; padding:20px 0;">
                <tr>
                  <td align="center">
                    <!--[if (gte mso 9)|(IE)]>
                    <table align="center" border="0" cellspacing="0" cellpadding="0" width="650">
                    <tr>
                    <td align="center" valign="top" width="650">
                    <![endif]-->
                    <table border="0" cellpadding="0" cellspacing="0" width="100%" style="max-width:650px; background-color:#ffffff; border:1px solid #e2e8f4; text-align:left;">
                      <tr>
                        <td style="background-color:{{color}}; padding:25px 35px;">
                          <p style="font-size:11px; font-weight:bold; letter-spacing:2px; text-transform:uppercase; color:#ffffff; margin:0 0 5px 0; opacity:0.9;">{{brandLabel}}</p>
                          <h1 style="margin:0; font-size:22px; font-weight:bold; color:#ffffff; line-height:1.3;">&#128225; Reporte Ejecutivo &mdash; {{countryName}}</h1>
                          <p style="color:#ffffff; font-size:13px; margin:8px 0 0 0; opacity:0.9;">{{dateStr}}</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:25px 35px;">
                          {{bodyContent}}
                        </td>
                      </tr>
                    </table>
                    <!--[if (gte mso 9)|(IE)]>
                    </td>
                    </tr>
                    </table>
                    <![endif]-->
                    
                    <table border="0" cellpadding="0" cellspacing="0" width="100%" style="max-width:650px;">
                      <tr>
                        <td align="center" style="padding:20px; font-size:12px; color:#888888;">
                          CableNews Agent &mdash; Uso interno &mdash; Confidencial
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
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
