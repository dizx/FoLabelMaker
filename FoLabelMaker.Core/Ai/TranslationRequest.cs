namespace FoLabelMaker.Core.Ai;

public sealed class TranslationRequest
{
    public required string LabelId { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public required string Text { get; init; }
    public string? Context { get; init; }
}

public sealed class TranslationResult
{
    public required string LabelId { get; init; }
    public required string TargetLanguage { get; init; }
    public required string Text { get; init; }
    public bool IsFromCache { get; init; }
    public bool IsValid { get; init; }
    public string? ValidationError { get; init; }
}
