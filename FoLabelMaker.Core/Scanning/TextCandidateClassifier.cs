using System.Text.RegularExpressions;

namespace FoLabelMaker.Core.Scanning;

public sealed partial class TextCandidateClassifier
{
    private static readonly HashSet<string> UserFacingPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Label", "HelpText", "Text", "Caption", "Description", "DisplayLabel", "PromptText",
    };

    public ClassificationResult Classify(string text, TextCandidateKind candidateKind, string propertyOrMethod)
    {
        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return ClassificationResult.Ignored("Text is empty.");
        }

        var trimmedText = text.Trim();
        if (trimmedText.StartsWith('@'))
        {
            return ClassificationResult.Ignored("Existing label reference.");
        }

        var score = 0.0;
        if (UserFacingPropertyNames.Contains(propertyOrMethod))
        {
            score += 0.6;
            reasons.Add($"Property '{propertyOrMethod}' usually contains user-facing text.");
        }

        if (candidateKind == TextCandidateKind.XppStringLiteral)
        {
            score += 0.2;
            reasons.Add("String literal found in X++ code.");
        }

        if (LooksLikeIgnoredTechnicalString(trimmedText, propertyOrMethod, reasons))
        {
            return new ClassificationResult(false, Math.Min(score, 0.4), reasons);
        }

        if (HasUserFacingSignals(trimmedText, reasons))
        {
            score += 0.35;
        }

        if (ContainsPlaceholder(trimmedText))
        {
            score += 0.1;
            reasons.Add("Contains format placeholder that often appears in messages.");
        }

        if (trimmedText.Length >= 3)
        {
            score += 0.05;
        }

        return new ClassificationResult(score >= 0.5, Math.Min(score, 1.0), reasons);
    }

    private static bool LooksLikeIgnoredTechnicalString(string text, string propertyOrMethod, List<string> reasons)
    {
        var isLikelyMetadataTextProperty = UserFacingPropertyNames.Contains(propertyOrMethod);

        if (UrlRegex().IsMatch(text))
        {
            reasons.Add("Looks like a URL.");
            return true;
        }

        if (PathRegex().IsMatch(text))
        {
            reasons.Add("Looks like a file path.");
            return true;
        }

        if (FileExtensionRegex().IsMatch(text))
        {
            reasons.Add("Looks like a file extension.");
            return true;
        }

        if (GuidRegex().IsMatch(text))
        {
            reasons.Add("Looks like a GUID.");
            return true;
        }

        if (NumberOnlyRegex().IsMatch(text))
        {
            reasons.Add("Number-only string.");
            return true;
        }

        if (PlaceholderOnlyRegex().IsMatch(text))
        {
            reasons.Add("Placeholder-only string.");
            return true;
        }

        var placeholderStrippedText = PlaceholderRegex().Replace(text, string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(SymbolAndWhitespaceRegex().Replace(placeholderStrippedText, string.Empty)))
        {
            reasons.Add("Contains placeholders and separators only.");
            return true;
        }

        if (SqlRegex().IsMatch(text))
        {
            reasons.Add("Looks like SQL.");
            return true;
        }

        if (!isLikelyMetadataTextProperty && JsonPropertyRegex().IsMatch(text))
        {
            reasons.Add("Looks like a JSON or XML property name.");
            return true;
        }

        if (!isLikelyMetadataTextProperty && CodeIdentifierRegex().IsMatch(text) && LooksLikeTechnicalIdentifier(text))
        {
            reasons.Add("Looks like a code identifier.");
            return true;
        }

        if (ProgIdRegex().IsMatch(text))
        {
            reasons.Add("Looks like a technical ProgID or dotted identifier.");
            return true;
        }

        return false;
    }

    private static bool HasUserFacingSignals(string text, List<string> reasons)
    {
        var hasSpaces = text.Contains(' ');
        var hasSentencePunctuation = text.Contains('?') || text.Contains('!') || text.Contains(':') || text.Contains('.');
        var hasTitleCaseWords = text.Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries).Any(part => part.Length > 1 && char.IsUpper(part[0]));
        var hasLowerCaseWords = text.Any(char.IsLower) && hasSpaces;
        if (hasSpaces || hasSentencePunctuation || hasTitleCaseWords || hasLowerCaseWords)
        {
            reasons.Add("Text shape looks user-facing.");
            return true;
        }

        return false;
    }

    private static bool LooksLikeTechnicalIdentifier(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains(' '))
        {
            return false;
        }

        if (text.Contains('_'))
        {
            return true;
        }

        if (char.IsLower(text[0]))
        {
            return true;
        }

        return Regex.IsMatch(text, "[a-z][A-Z]") || Regex.IsMatch(text, "[A-Z]{2,}[a-z]");
    }

    private static bool ContainsPlaceholder(string text) => Regex.IsMatch(text, "%\\d+") || Regex.IsMatch(text, "\\{\\d+\\}");

    [GeneratedRegex("^https?://", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^(?:[A-Za-z]:\\|\\\\|/)")]
    private static partial Regex PathRegex();

    [GeneratedRegex("^\\.[A-Za-z0-9]{1,8}$")]
    private static partial Regex FileExtensionRegex();

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex GuidRegex();

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex NumberOnlyRegex();

    [GeneratedRegex("^(%\\d+|\\{\\d+\\})+$")]
    private static partial Regex PlaceholderOnlyRegex();

    [GeneratedRegex("%\\d+|\\{\\d+\\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex("[\\s:;,.()\\-_/\\[\\]{}]+")]
    private static partial Regex SymbolAndWhitespaceRegex();

    [GeneratedRegex("\\b(select|insert|update|delete|from|where|join)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex SqlRegex();

    [GeneratedRegex("^[a-z_][A-Za-z0-9_:-]*$")]
    private static partial Regex JsonPropertyRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex CodeIdentifierRegex();

    [GeneratedRegex("^[A-Za-z0-9]+(?:\\.[A-Za-z0-9]+)+$")]
    private static partial Regex ProgIdRegex();
}

public sealed record ClassificationResult(bool IsUserFacing, double Confidence, IReadOnlyList<string> Reasons)
{
    public static ClassificationResult Ignored(string reason) => new(false, 0.0, [reason]);
}
