namespace FoLabelMaker.Core.Labels;

public sealed class LabelReuseMatcher
{
    public LabelEntry? FindExact(string text, IEnumerable<LabelEntry> entries) => entries.FirstOrDefault(entry => string.Equals(entry.Text, text, StringComparison.Ordinal));

    public LabelEntry? FindSimilar(string text, IEnumerable<LabelEntry> entries)
    {
        var normalizedText = Normalize(text);
        return entries.FirstOrDefault(entry => Normalize(entry.Text) == normalizedText);
    }

    private static string Normalize(string value) => string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
