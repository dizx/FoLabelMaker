using System.Text;
using System.Xml.Linq;
using FoLabelMaker.Core.Planning;
using FoLabelMaker.Core.Xpp;

namespace FoLabelMaker.Core.Metadata;

public sealed class FoMetadataReplacementEngine
{
    private readonly XppStringReplacementEngine _xppStringReplacementEngine;

    public FoMetadataReplacementEngine(XppStringReplacementEngine xppStringReplacementEngine)
    {
        _xppStringReplacementEngine = xppStringReplacementEngine;
    }

    public string Apply(string fileContent, IReadOnlyList<LabelChange> fileChanges)
    {
        var document = XDocument.Parse(fileContent, LoadOptions.PreserveWhitespace);
        foreach (var change in fileChanges.Where(change => change.ChangeType == LabelChangeType.ReplaceMetadataValue))
        {
            var element = document.Descendants().FirstOrDefault(candidate =>
                change.XmlElementName is not null &&
                string.Equals(candidate.Name.LocalName, change.XmlElementName, StringComparison.Ordinal) &&
                string.Equals(candidate.Value, change.OriginalText, StringComparison.Ordinal));

            if (element is not null)
            {
                element.Value = change.ReplacementText;
            }
        }

        foreach (var group in fileChanges.Where(change => change.ChangeType == LabelChangeType.ReplaceXppLiteral).GroupBy(change => change.XmlPath))
        {
            var element = document.Descendants().FirstOrDefault(candidate => string.Equals(BuildXmlPath(candidate), group.Key, StringComparison.Ordinal));
            if (element is null)
            {
                continue;
            }

            var cdataNode = element.Nodes().OfType<XCData>().FirstOrDefault();
            if (cdataNode is null)
            {
                continue;
            }

            cdataNode.Value = _xppStringReplacementEngine.Replace(cdataNode.Value, group.ToList());
        }

        using var writer = new Utf8StringWriter();
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    private static string BuildXmlPath(XElement element)
    {
        var parts = element.AncestorsAndSelf().Reverse().Select(current => current.Name.LocalName);
        return "/" + string.Join('/', parts);
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
