namespace CableNews.Infrastructure.Configuration;

public class GeminiConfig
{
    public string ModelId { get; init; } = "gemini-1.5-pro";
    public required string ApiKey { get; init; }
}

public class SmtpConfig
{
    public string Host { get; init; } = "smtp.gmail.com";
    public int Port { get; init; } = 587;
    public required string Username { get; init; }
    public required string Password { get; init; }
    public List<string> Recipients { get; init; } = [];
}
