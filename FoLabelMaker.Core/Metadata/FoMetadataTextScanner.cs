using System.Xml.Linq;
using FoLabelMaker.Core.Scanning;
using FoLabelMaker.Core.Xpp;

namespace FoLabelMaker.Core.Metadata;

public sealed class FoMetadataTextScanner
{
    private static readonly HashSet<string> InterestingPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Label", "HelpText", "Text", "Caption", "Description", "DisplayLabel", "PromptText",
    };

    private readonly TextCandidateClassifier _classifier;
    private readonly XppStringLiteralScanner _xppStringLiteralScanner;

    public FoMetadataTextScanner(TextCandidateClassifier classifier, XppStringLiteralScanner xppStringLiteralScanner)
    {
        _classifier = classifier;
        _xppStringLiteralScanner = xppStringLiteralScanner;
    }

    public IReadOnlyList<TextCandidate> Scan(FoMetadataDocument document)
    {
        var candidates = new List<TextCandidate>();

        foreach (var element in document.Document.Descendants())
        {
            if (InterestingPropertyNames.Contains(element.Name.LocalName) && !string.IsNullOrWhiteSpace(element.Value))
            {
                candidates.Add(CreateMetadataCandidate(document, element));
            }

            foreach (var node in element.Nodes().OfType<XCData>())
            {
                candidates.AddRange(ScanXppCData(document, element, node));
            }
        }

        return candidates;
    }

    public IReadOnlyList<TextCandidate> FindMissingTextCandidates(FoMetadataDocument document)
    {
        var results = new List<TextCandidate>();
        foreach (var element in document.Document.Descendants())
        {
            if (!InterestingPropertyNames.Contains(element.Name.LocalName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(element.Value))
            {
                continue;
            }

            var owner = element.Parent;
            var elementName = owner?.Attribute("Name")?.Value ?? owner?.Element("Name")?.Value ?? document.ElementName;
            var proposal = HumanizeName(elementName);
            results.Add(new TextCandidate
            {
                SourceFilePath = document.FilePath,
                ElementType = owner?.Name.LocalName ?? document.ElementType,
                ElementName = elementName,
                PropertyOrMethod = element.Name.LocalName,
                Kind = TextCandidateKind.MissingTextProposal,
                OriginalText = proposal,
                Confidence = 0.65,
                IsUserFacing = true,
                Reasons = ["Important metadata text is missing.", "Proposed text derived from element name."],
                XmlPath = BuildXmlPath(element),
                XmlElementName = element.Name.LocalName,
            });
        }

        return results;
    }

    private TextCandidate CreateMetadataCandidate(FoMetadataDocument document, XElement element)
    {
        var classification = _classifier.Classify(element.Value, TextCandidateKind.MetadataProperty, element.Name.LocalName);
        var owner = element.Parent;
        return new TextCandidate
        {
            SourceFilePath = document.FilePath,
            ElementType = owner?.Name.LocalName ?? document.ElementType,
            ElementName = owner?.Attribute("Name")?.Value ?? owner?.Element("Name")?.Value ?? document.ElementName,
            PropertyOrMethod = element.Name.LocalName,
            Kind = element.Value.TrimStart().StartsWith('@') ? TextCandidateKind.ExistingLabelReference : TextCandidateKind.MetadataProperty,
            OriginalText = element.Value,
            ExistingLabelReference = element.Value.TrimStart().StartsWith('@') ? element.Value.Trim() : null,
            Confidence = classification.Confidence,
            IsUserFacing = classification.IsUserFacing,
            Reasons = classification.Reasons,
            XmlPath = BuildXmlPath(element),
            XmlElementName = element.Name.LocalName,
        };
    }

    private IReadOnlyList<TextCandidate> ScanXppCData(FoMetadataDocument document, XElement containerElement, XCData cdata)
    {
        var results = new List<TextCandidate>();
        foreach (var literal in _xppStringLiteralScanner.Scan(cdata.Value))
        {
            var classification = _classifier.Classify(literal.InnerText, TextCandidateKind.XppStringLiteral, containerElement.Name.LocalName);
            results.Add(new TextCandidate
            {
                SourceFilePath = document.FilePath,
                ElementType = containerElement.Name.LocalName,
                ElementName = containerElement.Attribute("Name")?.Value ?? document.ElementName,
                PropertyOrMethod = containerElement.Name.LocalName,
                Kind = TextCandidateKind.XppStringLiteral,
                OriginalText = literal.InnerText,
                Confidence = classification.Confidence,
                IsUserFacing = classification.IsUserFacing,
                Reasons = classification.Reasons,
                XmlPath = BuildXmlPath(containerElement),
                XmlElementName = containerElement.Name.LocalName,
                CDataMarker = literal.FullLiteral,
            });
        }

        return results;
    }

    private static string BuildXmlPath(XElement element)
    {
        var parts = element.AncestorsAndSelf().Reverse().Select(current => current.Name.LocalName);
        return "/" + string.Join('/', parts);
    }

    private static string HumanizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmedValue = value.Trim();
        var normalizedValue = trimmedValue.Replace('_', ' ');
        var tokens = SplitIntoNameTokens(normalizedValue);
        if (tokens.Count == 0)
        {
            return trimmedValue;
        }

        if (tokens[0].Equals("PTS", StringComparison.OrdinalIgnoreCase))
        {
            tokens.RemoveAt(0);
        }
        else if (tokens[0].StartsWith("PTS", StringComparison.OrdinalIgnoreCase) && tokens[0].Length > 3 && tokens[0][3..].All(char.IsUpper))
        {
            tokens[0] = tokens[0][3..];
        }

        if (tokens.Count == 0)
        {
            return trimmedValue;
        }

        for (var index = 0; index < tokens.Count; index++)
        {
            tokens[index] = NormalizeToken(tokens[index], index == 0);
        }

        return string.Join(' ', tokens);
    }

    private static List<string> SplitIntoNameTokens(string value)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == ' ')
            {
                FlushToken();
                continue;
            }

            if (current.Count > 0 && char.IsUpper(character))
            {
                var previousCharacter = current[^1];
                var nextCharacter = index + 1 < value.Length ? value[index + 1] : '\0';
                if (char.IsLower(previousCharacter) || (char.IsUpper(previousCharacter) && char.IsLower(nextCharacter)))
                {
                    FlushToken();
                }
            }

            current.Add(character);
        }

        FlushToken();
        return tokens;

        void FlushToken()
        {
            if (current.Count == 0)
            {
                return;
            }

            tokens.Add(new string(current.ToArray()));
            current.Clear();
        }
    }

    private static string NormalizeToken(string token, bool isFirstToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        if (token.All(char.IsUpper) && token.Length <= 5)
        {
            return token;
        }

        var lowerToken = token.ToLowerInvariant();
        return char.ToUpperInvariant(lowerToken[0]) + lowerToken[1..];
    }
}
