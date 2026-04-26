using System.Text.RegularExpressions;

namespace FoLabelMaker.Core.Xpp;

public sealed partial class XppStringLiteralScanner
{
    public IReadOnlyList<XppStringLiteralMatch> Scan(string xppSource)
    {
        var matches = new List<XppStringLiteralMatch>();
        foreach (Match match in StringLiteralRegex().Matches(xppSource))
        {
            var fullValue = match.Value;
            if (fullValue.Length < 2)
            {
                continue;
            }

            var innerValue = fullValue[1..^1].Replace("\\\"", "\"");
            matches.Add(new XppStringLiteralMatch(match.Index, match.Length, fullValue, innerValue));
        }

        return matches;
    }

    [GeneratedRegex("\"(?:\\\\.|[^\"\\\\])*\"")]
    private static partial Regex StringLiteralRegex();
}

public sealed record XppStringLiteralMatch(int StartIndex, int Length, string FullLiteral, string InnerText);
