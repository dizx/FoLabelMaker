namespace FoLabelMaker.Core.Configuration;

public sealed class OpenAiOptions
{
    public string? ApiKey { get; init; }
    public string ApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; init; } = "gpt-5-mini";
    public string CacheFilePath { get; init; } = Path.Combine(Environment.CurrentDirectory, ".fo-labelmaker-ai-cache.json");
}
