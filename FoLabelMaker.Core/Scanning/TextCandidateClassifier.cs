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

        if (LooksLikeIgnoredTechnicalString(trimmedText, candidateKind, propertyOrMethod, reasons))
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

    private static bool LooksLikeIgnoredTechnicalString(string text, TextCandidateKind candidateKind, string propertyOrMethod, List<string> reasons)
    {
        var isLikelyMetadataTextProperty = UserFacingPropertyNames.Contains(propertyOrMethod);
        var isXppLiteral = candidateKind == TextCandidateKind.XppStringLiteral;

        if (UrlRegex().IsMatch(text))
        {
            reasons.Add("Looks like a URL.");
            return true;
        }

        if (JsonObjectRegex().IsMatch(text))
        {
            reasons.Add("Looks like JSON.");
            return true;
        }

        if (JsonFragmentRegex().IsMatch(text))
        {
            reasons.Add("Looks like a JSON fragment.");
            return true;
        }

        if (JsonPathRegex().IsMatch(text))
        {
            reasons.Add("Looks like a JSON path.");
            return true;
        }

        if (CssOrHtmlAttributeRegex().IsMatch(text))
        {
            reasons.Add("Looks like a CSS or HTML attribute fragment.");
            return true;
        }

        if (EmbeddedXmlDocumentRegex().IsMatch(text) || EmbeddedHtmlRegex().IsMatch(text))
        {
            reasons.Add("Looks like embedded XML or HTML.");
            return true;
        }

        if (MimeTypeRegex().IsMatch(text))
        {
            reasons.Add("Looks like a MIME type.");
            return true;
        }

        if (HttpMethodRegex().IsMatch(text))
        {
            reasons.Add("Looks like an HTTP method.");
            return true;
        }

        if (HttpHeaderNameRegex().IsMatch(text))
        {
            reasons.Add("Looks like an HTTP header name.");
            return true;
        }

        if (SecretOrHashRegex().IsMatch(text))
        {
            reasons.Add("Looks like a key, token, or hash.");
            return true;
        }

        if (AlphaNumericCodeIdentifierRegex().IsMatch(text))
        {
            reasons.Add("Looks like an alphanumeric code identifier.");
            return true;
        }

        if (ApiTokenRegex().IsMatch(text))
        {
            reasons.Add("Looks like an API token.");
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

        if (isXppLiteral && LooksLikeXppTechnicalLiteral(text, reasons))
        {
            return true;
        }

        if (!isLikelyMetadataTextProperty && XppTechnicalLiteralRegex().IsMatch(text))
        {
            reasons.Add("Looks like a technical X++ string literal.");
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

    private static bool LooksLikeXppTechnicalLiteral(string text, List<string> reasons)
    {
        if (!text.Any(char.IsLetter))
        {
            reasons.Add("Contains no letters.");
            return true;
        }

        if (XmlTagRegex().IsMatch(text))
        {
            reasons.Add("Looks like an XML tag fragment.");
            return true;
        }

        if (text is "{\"}" or "\"}" or "}")
        {
            reasons.Add("Looks like a JSON delimiter fragment.");
            return true;
        }

        if (EscapedWhitespaceRegex().IsMatch(text))
        {
            reasons.Add("Looks like escaped whitespace.");
            return true;
        }

        if (FormatOnlyRegex().IsMatch(text))
        {
            reasons.Add("Looks like a format-only string.");
            return true;
        }

        if (CodeExpressionRegex().IsMatch(text))
        {
            reasons.Add("Looks like a code expression.");
            return true;
        }

        if (QueryStringRegex().IsMatch(text))
        {
            reasons.Add("Looks like a URL query string fragment.");
            return true;
        }

        if (LooksLikeCharacterWhitelist(text))
        {
            reasons.Add("Looks like a character whitelist for string filtering.");
            return true;
        }

        if (text.Contains('_') && !text.Contains(' '))
        {
            reasons.Add("Looks like a structured technical identifier.");
            return true;
        }

        if (StructuredTechnicalTextRegex().IsMatch(text))
        {
            reasons.Add("Looks like a structured technical identifier with a qualifier.");
            return true;
        }

        if (CodeSnippetRegex().IsMatch(text))
        {
            reasons.Add("Looks like a code snippet or generated code template.");
            return true;
        }

        if (text.Length <= 4 && text.All(character => char.IsUpper(character) || char.IsDigit(character)))
        {
            reasons.Add("Looks like a short technical constant.");
            return true;
        }

        return false;
    }

    private static bool LooksLikeCharacterWhitelist(string text)
    {
        return text.Contains("0123456789", StringComparison.Ordinal)
            && (text.Contains("abcdefghijklmnopqrstuvwxyz", StringComparison.OrdinalIgnoreCase)
                || text.Contains("<>=", StringComparison.Ordinal)
                || text.Contains("\\n", StringComparison.Ordinal));
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

    [GeneratedRegex("^\\s*[\\[{].*[\\]}]\\s*$")]
    private static partial Regex JsonObjectRegex();

    [GeneratedRegex("^\\s*[\\[{]?[\\s,]*\\\"[A-Za-z0-9_-]+\\\"\\s*:")]
    private static partial Regex JsonFragmentRegex();

    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*(?:\\.[A-Za-z_$][A-Za-z0-9_$]*|\\[[0-9]+\\])+$")]
    private static partial Regex JsonPathRegex();

    [GeneratedRegex("^(?:style\\s*=|[a-z-]+\\s*:\\s*[^;]+;)", RegexOptions.IgnoreCase)]
    private static partial Regex CssOrHtmlAttributeRegex();

    [GeneratedRegex("^\\s*(?:<\\?xml\\b|<Report\\b|<[A-Za-z][A-Za-z0-9:_-]*(?:\\s|>))", RegexOptions.IgnoreCase)]
    private static partial Regex EmbeddedXmlDocumentRegex();

    [GeneratedRegex("^\\s*<(?:!doctype\\s+html|html\\b|body\\b|div\\b|span\\b|table\\b)", RegexOptions.IgnoreCase)]
    private static partial Regex EmbeddedHtmlRegex();

    [GeneratedRegex("^[a-z0-9.+-]+/[a-z0-9.+-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex MimeTypeRegex();

    [GeneratedRegex("^(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)$", RegexOptions.IgnoreCase)]
    private static partial Regex HttpMethodRegex();

    [GeneratedRegex("^(Content|Content-Type|Accept|Authorization|Ocp-Apim-Subscription-Key|User-Agent|Host)$", RegexOptions.IgnoreCase)]
    private static partial Regex HttpHeaderNameRegex();

    [GeneratedRegex("^[a-f0-9]{24,}$", RegexOptions.IgnoreCase)]
    private static partial Regex SecretOrHashRegex();

    [GeneratedRegex("^[A-Za-z]+[A-Za-z0-9]*[0-9][A-Za-z0-9]*$")]
    private static partial Regex AlphaNumericCodeIdentifierRegex();

    [GeneratedRegex("^(?:API|KEY|TOKEN)-[A-Za-z0-9._:-]{20,}$", RegexOptions.IgnoreCase)]
    private static partial Regex ApiTokenRegex();

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

    [GeneratedRegex("\\b(select\\b.+\\bfrom|delete\\s+from|insert\\s+into|update\\s+\\w+\\s+set|\\bjoin\\b.+\\bon\\b)\\b", RegexOptions.IgnoreCase)]
    private static partial Regex SqlRegex();

    [GeneratedRegex("^[a-z_][A-Za-z0-9_:-]*$")]
    private static partial Regex JsonPropertyRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex CodeIdentifierRegex();

    [GeneratedRegex("^[A-Za-z0-9]+(?:\\.[A-Za-z0-9]+)+$")]
    private static partial Regex ProgIdRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_:-]*$")]
    private static partial Regex XppTechnicalLiteralRegex();

    [GeneratedRegex("^<[^>]+>$")]
    private static partial Regex XmlTagRegex();

    [GeneratedRegex("^\\?[^\\s]+=")]
    private static partial Regex QueryStringRegex();

    [GeneratedRegex("^[A-Za-z0-9]+(?:_[A-Za-z0-9]+){2,}(?:[ /][A-Za-z0-9]+)+$")]
    private static partial Regex StructuredTechnicalTextRegex();

    [GeneratedRegex("\\b(case\\s+|break;|node\\.|=\\s*node\\.|;\\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex CodeSnippetRegex();

    [GeneratedRegex("^(?:\\\\[rnt])+$")]
    private static partial Regex EscapedWhitespaceRegex();

    [GeneratedRegex("^[\\s\\r\\n\\\\nt.,:;()\\[\\]{}<>='\"_\\-/|+&]*?(?:%\\d+[\\s\\r\\n\\\\nt.,:;()\\[\\]{}<>='\"_\\-/|+&]*)+$")]
    private static partial Regex FormatOnlyRegex();

    [GeneratedRegex("^[\\s()]*[A-Za-z_][A-Za-z0-9_.]*\\s*(?:==|!=|<=|>=|<|>)")]
    private static partial Regex CodeExpressionRegex();

}

public sealed record ClassificationResult(bool IsUserFacing, double Confidence, IReadOnlyList<string> Reasons)
{
    public static ClassificationResult Ignored(string reason) => new(false, 0.0, [reason]);
}
