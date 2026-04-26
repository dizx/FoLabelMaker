namespace FoLabelMaker.Core.Labels;

public sealed class LabelFile
{
    public required string FilePath { get; init; }
    public string? DescriptorFilePath { get; init; }
    public required string Language { get; init; }
    public required string FileId { get; init; }
    public required string EntryIdPrefix { get; init; }
    public required LabelFileFormat Format { get; init; }
    public IList<LabelEntry> Entries { get; init; } = [];
}

public enum LabelFileFormat
{
    Text,
    Xml,
}
