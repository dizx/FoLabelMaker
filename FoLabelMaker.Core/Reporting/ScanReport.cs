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
    public IList<LanguageMismatchResult> LanguageMismatches { get; init; } = [];
}

public sealed class LanguageMismatchResult
{
    public required string SourceFilePath { get; init; }
    public int? LineNumber { get; init; }
    public required string ElementType { get; init; }
    public required string ElementName { get; init; }
    public required string PropertyOrMethod { get; init; }
    public required string Text { get; init; }
    public required string ExpectedLanguage { get; init; }
    public required string DetectedLanguage { get; init; }
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
