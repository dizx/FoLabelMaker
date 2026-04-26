namespace FoLabelMaker.Core.Labels;

public sealed class LabelIdGenerator
{
    public string GenerateNextId(string entryIdPrefix, IEnumerable<LabelEntry> existingEntries)
    {
        var normalizedPrefix = entryIdPrefix.TrimStart('@');
        var maxNumericSuffix = existingEntries
            .Select(entry => entry.Id)
            .Where(id => id.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(id => id[normalizedPrefix.Length..])
            .Select(suffix => int.TryParse(suffix, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{normalizedPrefix}{maxNumericSuffix + 1:0000}";
    }
}
