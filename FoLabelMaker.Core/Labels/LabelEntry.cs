namespace FoLabelMaker.Core.Labels;

public sealed class LabelEntry
{
    public required string Id { get; init; }
    public required string Text { get; set; }
    public string? Comment { get; init; }
}
