using FoLabelMaker.Core.Reporting;

namespace FoLabelMaker.Core.Planning;

public sealed class LabelChangePlan
{
    public required string MetadataRootPath { get; init; }
    public required string ModelRootPath { get; init; }
    public required string ModelName { get; init; }
    public required string BaseLanguage { get; init; }
    public required string LabelPrefix { get; init; }
    public IList<LabelChange> Changes { get; init; } = [];
    public IList<LabelFileChange> LabelFileChanges { get; init; } = [];
    public IList<string> ValidationErrors { get; init; } = [];
    public ScanReport? ScanReport { get; init; }
}

public sealed class LabelFileChange
{
    public required string FilePath { get; init; }
    public required string Language { get; init; }
    public required string LabelId { get; init; }
    public required string Text { get; init; }
    public bool IsNewFile { get; init; }
}
