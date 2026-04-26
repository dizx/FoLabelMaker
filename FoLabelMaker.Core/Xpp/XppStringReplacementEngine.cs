using FoLabelMaker.Core.Planning;

namespace FoLabelMaker.Core.Xpp;

public sealed class XppStringReplacementEngine
{
    public string Replace(string cdataText, IReadOnlyList<LabelChange> changes)
    {
        var updatedText = cdataText;
        foreach (var change in changes.Where(change => !string.IsNullOrWhiteSpace(change.CDataMarker)))
        {
            updatedText = updatedText.Replace(change.CDataMarker!, change.ReplacementText, StringComparison.Ordinal);
        }

        return updatedText;
    }
}
