namespace CableNews.Application.Common.Models;

using CableNews.Domain.Entities;

public record AnalyzedArticle(Article Article, ArticleAnalysis Analysis);
