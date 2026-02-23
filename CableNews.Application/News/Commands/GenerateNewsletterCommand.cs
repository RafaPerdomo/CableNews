namespace CableNews.Application.News.Commands;

using MediatR;
using CableNews.Application.Common.Models;

public record GenerateNewsletterCommand(NewsAgentConfig Config) : IRequest<bool>;
