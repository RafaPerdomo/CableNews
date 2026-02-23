using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CableNews.Application;
using CableNews.Application.Common.Models;
using CableNews.Infrastructure;
using CableNews.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Register configuration mappings
builder.Services.Configure<NewsAgentConfig>(builder.Configuration.GetSection("NewsAgent"));

// Register layers
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Register Worker
builder.Services.AddHostedService<NewsAgentWorker>();

var host = builder.Build();
host.Run();
