namespace FoLabelMaker.Core.Scanning;

public sealed class TextCandidate
{
    public required string SourceFilePath { get; init; }
    public required string ElementType { get; init; }
    public required string ElementName { get; init; }
    public required string PropertyOrMethod { get; init; }
    public required TextCandidateKind Kind { get; init; }
    public required string OriginalText { get; init; }
    public string? ExistingLabelReference { get; init; }
    public double Confidence { get; init; }
    public bool IsUserFacing { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public string? ReplacementToken { get; init; }
    public int? LineNumber { get; init; }
    public string? XmlPath { get; init; }
    public string? XmlElementName { get; init; }
    public string? CDataMarker { get; init; }
}
