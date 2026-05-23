namespace FoLabelMaker.Core.Merging;

public sealed class LabelMergeReport
{
    public required string MetadataRootPath { get; init; }
    public required string ModelRootPath { get; init; }
    public required string ModelName { get; init; }
    public required string TargetLabelFileId { get; init; }
    public required bool Applied { get; init; }
    public IList<LabelMergeMapping> Mappings { get; set; } = [];
    public IList<string> ChangedFiles { get; set; } = [];
    public IList<string> ValidationErrors { get; init; } = [];
}

public sealed class LabelMergeMapping
{
    public required string SourceReference { get; init; }
    public required string TargetReference { get; init; }
    public required string Text { get; init; }
    public required string Reason { get; init; }
}
