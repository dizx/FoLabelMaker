namespace FoLabelMaker.Core.Ai;

public sealed class TextImprovementRequest
{
    public required string SourceFilePath { get; init; }
    public required string Text { get; init; }
    public string? Context { get; init; }
}

public sealed class TextImprovementResult
{
    public required string SourceFilePath { get; init; }
    public required string OriginalText { get; init; }
    public required string SuggestedText { get; init; }
    public required double Confidence { get; init; }
    public required string Reason { get; init; }
}
