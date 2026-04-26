namespace FoLabelMaker.Core.Planning;

public sealed class LabelChange
{
    public required LabelChangeType ChangeType { get; init; }
    public required string SourceFilePath { get; init; }
    public required string ElementType { get; init; }
    public required string ElementName { get; init; }
    public required string PropertyOrMethod { get; init; }
    public required string OriginalText { get; init; }
    public required string LabelId { get; init; }
    public required string ReplacementText { get; init; }
    public required string GeneratedLabelReference { get; init; }
    public string? XmlPath { get; init; }
    public string? XmlElementName { get; init; }
    public string? CDataMarker { get; init; }
    public LabelReuseKind ReuseKind { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
}

public enum LabelReuseKind
{
    None,
    ExistingLabel,
    PlannedLabel,
}

public enum LabelChangeType
{
    ReplaceMetadataValue,
    ReplaceXppLiteral,
    AddMissingMetadataText,
}
