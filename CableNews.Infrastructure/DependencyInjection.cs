namespace CableNews.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CableNews.Application.Common.Interfaces;
using CableNews.Infrastructure.Configuration;
using CableNews.Infrastructure.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmtpConfig>(configuration.GetSection("Smtp"));
        services.Configure<GeminiConfig>(configuration.GetSection("Gemini"));

        services.AddHttpClient<INewsFeedProvider, GoogleNewsFeedProvider>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            });
        services.AddHttpClient<ILlmSummarizerService, GeminiProLlmService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        
        services.AddTransient<IEmailService, GmailSmtpEmailService>();

        return services;
    }
}
