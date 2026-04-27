namespace FoLabelMaker.Core.Configuration;

public sealed class AppSettings
{
    public LabelMakerSettings LabelMaker { get; init; } = new();
    public OpenAiSettings OpenAi { get; init; } = new();
}

public sealed class LabelMakerSettings
{
    public string? MetadataRootPath { get; init; }
    public string? ModelName { get; init; }
    public string? LabelPrefix { get; init; }
    public string? BaseLanguage { get; init; }
    public IReadOnlyList<string>? TargetLanguages { get; init; }
    public bool? ReuseSimilarLabels { get; init; }
    public bool? OverwriteTranslations { get; init; }
}

public sealed class OpenAiSettings
{
    public string? ApiKey { get; init; }
    public string? Model { get; init; }
    public string? ApiKeyEnvironmentVariable { get; init; }
    public string? BaseUrl { get; init; }
    public string? CacheFilePath { get; init; }
}
