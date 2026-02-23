namespace CableNews.Worker;

using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using CableNews.Application.Common.Models;
using CableNews.Application.News.Commands;

public class NewsAgentWorker : BackgroundService
{
    private readonly ILogger<NewsAgentWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly NewsAgentConfig _config;

    public NewsAgentWorker(
        ILogger<NewsAgentWorker> logger,
        IServiceProvider serviceProvider,
        IOptions<NewsAgentConfig> config)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("News Agent Worker execution triggered at: {time}", DateTimeOffset.Now);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var command = new GenerateNewsletterCommand(_config);
            var success = await mediator.Send(command, stoppingToken);

            if (success)
            {
                _logger.LogInformation("News Agent Worker finished successfully.");
            }
            else
            {
                _logger.LogWarning("News Agent Worker finished but no newsletter was sent.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A fatal error occurred during news agent execution.");
            Environment.ExitCode = 1;
        }
        finally
        {
            var hostApplicationLifetime = _serviceProvider.GetRequiredService<IHostApplicationLifetime>();
            hostApplicationLifetime.StopApplication();
        }
    }
}
