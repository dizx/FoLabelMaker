using FoLabelMaker.Core.Ai;
using FoLabelMaker.Core.Scanning;

namespace FoLabelMaker.Core.Reporting;

public sealed class ScanReport
{
    public IList<string> ScannedFiles { get; init; } = [];
    public IList<TextCandidate> DetectedCandidates { get; init; } = [];
    public IList<TextCandidate> IgnoredCandidates { get; init; } = [];
    public IList<string> ValidationErrors { get; init; } = [];
    public IList<TextCandidate> MissingTextProposals { get; init; } = [];
    public IList<TextImprovementResult> ImprovementSuggestions { get; init; } = [];
}

public sealed class PlanReport
{
    public required ScanReport ScanReport { get; init; }
    public IList<string> ExistingLabelsReused { get; init; } = [];
    public IList<string> DuplicateTextsConsolidated { get; init; } = [];
    public IList<string> LabelsCreated { get; init; } = [];
    public IList<string> TranslationsCreated { get; init; } = [];
    public IList<string> ChangedFiles { get; init; } = [];
    public IList<string> ValidationErrors { get; init; } = [];
}
