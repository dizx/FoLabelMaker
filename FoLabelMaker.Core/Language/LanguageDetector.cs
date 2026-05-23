using System.Text.RegularExpressions;

namespace FoLabelMaker.Core.Language;

public sealed partial class LanguageDetector
{
    private static readonly string[] AmbiguousWords = ["for", "to"];
    private static readonly string[] NorwegianWords = ["og", "ikke", "til", "med", "du", "har", "er", "skal", "faktura", "fakturanummer", "fakturadato", "fakturert", "kundefaktura", "godkjenne", "leverandor", "leverandør", "feil", "fil", "velg", "send", "mottatt", "mottakk", "mangler", "forfall", "valutakode", "tabell", "detaljer", "filterfelt", "dokument", "dokumenthode", "identifikasjon", "meldingshode", "scannet", "sider", "dette", "duplicat", "kontroller", "posteres", "avvist", "periode", "stopp", "start", "merknad", "linjenr", "linenr"];
    private static readonly string[] EnglishWords = ["and", "not", "the", "with", "you", "has", "have", "invoice", "vendor", "customer", "error", "report", "server", "document", "sales", "agreement", "setup", "creditnote", "valid", "cost", "scan", "should", "here", "xml", "approve", "approval", "status", "posted", "today", "week", "month", "year", "total"];

    public string? Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalizedText = text.Trim();
        if (NorwegianCharacterRegex().IsMatch(normalizedText))
        {
            return "nb-NO";
        }

        var words = WordRegex().Matches(normalizedText.ToLowerInvariant()).Select(match => match.Value).ToArray();
        if (words.Length == 0)
        {
            return null;
        }

        var scoredWords = words.Where(word => !AmbiguousWords.Contains(word, StringComparer.OrdinalIgnoreCase)).ToArray();
        var norwegianScore = scoredWords.Count(word => NorwegianWords.Contains(word, StringComparer.OrdinalIgnoreCase));
        var englishScore = scoredWords.Count(word => EnglishWords.Contains(word, StringComparer.OrdinalIgnoreCase));
        if (norwegianScore > englishScore && norwegianScore > 0)
        {
            return "nb-NO";
        }

        if (englishScore > norwegianScore && englishScore > 0)
        {
            return "en-US";
        }

        return null;
    }

    public bool IsMismatch(string text, string expectedLanguage, out string? detectedLanguage)
    {
        detectedLanguage = Detect(text);
        if (string.IsNullOrWhiteSpace(detectedLanguage))
        {
            return false;
        }

        return !string.Equals(NormalizeLanguage(detectedLanguage), NormalizeLanguage(expectedLanguage), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLanguage(string language) => language.Split('-')[0].ToLowerInvariant() switch
    {
        "no" or "nb" or "nn" => "no",
        "en" => "en",
        "sv" => "sv",
        "da" => "da",
        _ => language.Split('-')[0].ToLowerInvariant(),
    };

    [GeneratedRegex("[æøåÆØÅ]")]
    private static partial Regex NorwegianCharacterRegex();

    [GeneratedRegex("[A-Za-zæøåÆØÅ]{2,}")]
    private static partial Regex WordRegex();
}
