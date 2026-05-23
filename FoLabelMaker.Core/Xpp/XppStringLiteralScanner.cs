namespace FoLabelMaker.Core.Xpp;

public sealed class XppStringLiteralScanner
{
    public IReadOnlyList<XppStringLiteralMatch> Scan(string xppSource)
    {
        var matches = new List<XppStringLiteralMatch>();
        var index = 0;
        while (index < xppSource.Length)
        {
            var current = xppSource[index];
            var next = index + 1 < xppSource.Length ? xppSource[index + 1] : '\0';

            if (current == '/' && next == '/')
            {
                index = SkipLineComment(xppSource, index + 2);
                continue;
            }

            if (current == '/' && next == '*')
            {
                index = SkipBlockComment(xppSource, index + 2);
                continue;
            }

            if (current is not ('\'' or '"'))
            {
                index++;
                continue;
            }

            var literal = ReadStringLiteral(xppSource, index, current);
            if (literal is null)
            {
                index++;
                continue;
            }

            matches.Add(literal);
            index = literal.StartIndex + literal.Length;
        }

        return matches;
    }

    private static XppStringLiteralMatch? ReadStringLiteral(string source, int startIndex, char quote)
    {
        var index = startIndex + 1;
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '\\' && index + 1 < source.Length)
            {
                index += 2;
                continue;
            }

            if (current != quote)
            {
                index++;
                continue;
            }

            if (index + 1 < source.Length && source[index + 1] == quote)
            {
                index += 2;
                continue;
            }

            var length = index - startIndex + 1;
            var fullLiteral = source.Substring(startIndex, length);
            var innerText = fullLiteral[1..^1]
                .Replace("\\\"", "\"")
                .Replace("\\'", "'")
                .Replace(new string(quote, 2), quote.ToString());
            return new XppStringLiteralMatch(startIndex, length, fullLiteral, innerText);
        }

        return null;
    }

    private static int SkipLineComment(string source, int index)
    {
        while (index < source.Length && source[index] is not ('\r' or '\n'))
        {
            index++;
        }

        return index;
    }

    private static int SkipBlockComment(string source, int index)
    {
        while (index + 1 < source.Length)
        {
            if (source[index] == '*' && source[index + 1] == '/')
            {
                return index + 2;
            }

            index++;
        }

        return source.Length;
    }
}

public sealed record XppStringLiteralMatch(int StartIndex, int Length, string FullLiteral, string InnerText);
