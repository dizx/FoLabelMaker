namespace FoLabelMaker.Core.Scanning;

public enum TextCandidateKind
{
    MetadataProperty,
    XppStringLiteral,
    ExistingLabelReference,
    MissingTextProposal,
    ImprovementSuggestion,
}
