namespace FoLabelMaker.Core.Scanning;

public sealed class MetadataFile
{
    public required string FilePath { get; init; }
    public required string ElementType { get; init; }
    public required string ElementName { get; init; }
    public bool HasInvalidXml { get; init; }
    public string? InvalidXmlError { get; init; }
}
