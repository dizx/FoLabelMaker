using FoLabelMaker.Core.Ai;
using FoLabelMaker.Core.Scanning;

namespace FoLabelMaker.Core.Improvement;

public sealed class TextImprovementSuggester
{
    public IReadOnlyList<TextImprovementResult> Suggest(IReadOnlyList<TextCandidate> candidates)
    {
        var results = new List<TextImprovementResult>();
        foreach (var candidate in candidates.Where(candidate => candidate.IsUserFacing))
        {
            var trimmed = candidate.OriginalText.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (candidate.OriginalText.Contains("  ", StringComparison.Ordinal))
            {
                results.Add(Create(candidate, string.Join(' ', candidate.OriginalText.Split(' ', StringSplitOptions.RemoveEmptyEntries)), 0.9, "Contains double spaces."));
            }
            else if (!string.Equals(candidate.OriginalText, trimmed, StringComparison.Ordinal))
            {
                results.Add(Create(candidate, trimmed, 0.95, "Contains leading or trailing spaces."));
            }
            else if (trimmed.Length > 20 && !trimmed.EndsWith('.') && !trimmed.EndsWith('!') && !trimmed.EndsWith('?') && trimmed.Contains(' '))
            {
                results.Add(Create(candidate, trimmed + ".", 0.55, "Longer message may need punctuation."));
            }
        }

        return results;
    }

    private static TextImprovementResult Create(TextCandidate candidate, string suggestedText, double confidence, string reason) => new()
    {
        SourceFilePath = candidate.SourceFilePath,
        OriginalText = candidate.OriginalText,
        SuggestedText = suggestedText,
        Confidence = confidence,
        Reason = reason,
    };
}
