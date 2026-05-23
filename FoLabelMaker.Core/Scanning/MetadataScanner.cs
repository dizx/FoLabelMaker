using System.Xml;
using System.Xml.Linq;
using FoLabelMaker.Core.Metadata;
using FoLabelMaker.Core.Reporting;

namespace FoLabelMaker.Core.Scanning;

public sealed class MetadataScanner
{
    private static readonly string[] SupportedFolders = ["AxClass", "AxTable", "AxForm", "AxMenuItemDisplay", "AxMenuItemOutput", "AxMenuItemAction", "AxEdt", "AxEnum", "AxReport", "AxReportDesign"];
    private static readonly HashSet<string> IgnoredFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "XppMetadata",
    };

    private readonly FoMetadataTextScanner _metadataTextScanner;

    public MetadataScanner(FoMetadataTextScanner metadataTextScanner)
    {
        _metadataTextScanner = metadataTextScanner;
    }

    public async Task<ScanResult> ScanAsync(string metadataRootPath, string? modelName, CancellationToken cancellationToken)
    {
        var modelRootPath = ResolveModelRootPath(metadataRootPath, modelName);
        var report = new ScanReport();
        var metadataFiles = new List<MetadataFile>();

        foreach (var folder in SupportedFolders)
        {
            if (!Directory.Exists(modelRootPath))
            {
                break;
            }

            var artifactFolderPath = Path.Combine(modelRootPath, folder);
            if (!Directory.Exists(artifactFolderPath))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(artifactFolderPath, "*.xml", SearchOption.AllDirectories))
            {
                if (IsInIgnoredFolder(filePath))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                report.ScannedFiles.Add(filePath);
                try
                {
                    await using var stream = File.OpenRead(filePath);
                    var document = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo, cancellationToken);
                    var root = document.Root;
                    if (root is null)
                    {
                        continue;
                    }

                    var metadataDocument = new FoMetadataDocument
                    {
                        FilePath = filePath,
                        Document = document,
                        ElementType = root.Name.LocalName,
                        ElementName = root.Attribute("Name")?.Value ?? root.Element("Name")?.Value ?? Path.GetFileNameWithoutExtension(filePath),
                    };

                    metadataFiles.Add(new MetadataFile
                    {
                        FilePath = filePath,
                        ElementType = metadataDocument.ElementType,
                        ElementName = metadataDocument.ElementName,
                    });

                    foreach (var candidate in _metadataTextScanner.Scan(metadataDocument))
                    {
                        if (candidate.IsUserFacing)
                        {
                            report.DetectedCandidates.Add(candidate);
                        }
                        else
                        {
                            if (ShouldReportIgnoredCandidate(candidate))
                            {
                                report.IgnoredCandidates.Add(candidate);
                            }
                        }
                    }

                    foreach (var missingText in _metadataTextScanner.FindMissingTextCandidates(metadataDocument))
                    {
                        report.MissingTextProposals.Add(missingText);
                    }
                }
                catch (XmlException exception)
                {
                    metadataFiles.Add(new MetadataFile
                    {
                        FilePath = filePath,
                        ElementType = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty),
                        ElementName = Path.GetFileNameWithoutExtension(filePath),
                        HasInvalidXml = true,
                        InvalidXmlError = exception.Message,
                    });
                    report.ValidationErrors.Add($"Invalid XML: {filePath}: {exception.Message}");
                }
            }
        }

        return new ScanResult(modelRootPath, metadataFiles, report);
    }

    private static string ResolveModelRootPath(string metadataRootPath, string? modelName)
    {
        var normalizedRootPath = ResolveSearchRoot(metadataRootPath);

        if (LooksLikeModelRoot(normalizedRootPath))
        {
            return normalizedRootPath;
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            var detectedModelRoots = DiscoverModelRoots(normalizedRootPath);
            if (detectedModelRoots.Count == 1)
            {
                return detectedModelRoots[0];
            }

            if (detectedModelRoots.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple D365 FO models were found under '{metadataRootPath}'. Pass --model to select one. Found: {string.Join(", ", detectedModelRoots.Select(Path.GetFileName))}");
            }

            return normalizedRootPath;
        }

        var detectedRoots = DiscoverModelRoots(normalizedRootPath);
        var exactMatch = detectedRoots.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), modelName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exactMatch))
        {
            return exactMatch;
        }

        var containsMatches = detectedRoots
            .Where(path => Path.GetFileName(path).Contains(modelName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (containsMatches.Count == 1)
        {
            return containsMatches[0];
        }

        if (containsMatches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Model name '{modelName}' matched multiple models under '{normalizedRootPath}'. Matches: {string.Join(", ", containsMatches.Select(Path.GetFileName))}");
        }

        return normalizedRootPath;
    }

    private static bool LooksLikeModelRoot(string path) => SupportedFolders.Any(folder => Directory.Exists(Path.Combine(path, folder)));

    private static string ResolveSearchRoot(string inputPath)
    {
        if (!Directory.Exists(inputPath))
        {
            return inputPath;
        }

        var conventionalMetadataPath = Path.Combine(inputPath, "Metadata");
        if (Directory.Exists(conventionalMetadataPath))
        {
            return conventionalMetadataPath;
        }

        var metadataDirectory = Directory.EnumerateDirectories(inputPath, "Metadata", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return metadataDirectory ?? inputPath;
    }

    private static List<string> DiscoverModelRoots(string searchRoot)
    {
        if (!Directory.Exists(searchRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(searchRoot)
            .Select(directory => Path.Combine(directory, Path.GetFileName(directory)))
            .Where(Directory.Exists)
            .Where(LooksLikeModelRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsInIgnoredFolder(string filePath)
    {
        var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
        while (directoryInfo is not null)
        {
            if (IgnoredFolderNames.Contains(directoryInfo.Name))
            {
                return true;
            }

            directoryInfo = directoryInfo.Parent;
        }

        return false;
    }

    private static bool ShouldReportIgnoredCandidate(TextCandidate candidate)
    {
        if (candidate.Kind == TextCandidateKind.ExistingLabelReference)
        {
            return false;
        }

        if (candidate.Kind == TextCandidateKind.XppStringLiteral && candidate.Confidence <= 0.4)
        {
            return false;
        }

        var lowValueReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Text is empty.",
            "Existing label reference.",
            "Contains no letters.",
            "Number-only string.",
            "Placeholder-only string.",
            "Contains placeholders and separators only.",
            "Looks like a URL.",
            "Looks like a file path.",
            "Looks like a file extension.",
            "Looks like a GUID.",
            "Looks like JSON.",
            "Looks like a JSON fragment.",
            "Looks like a JSON path.",
            "Looks like a CSS or HTML attribute fragment.",
            "Looks like a MIME type.",
            "Looks like an HTTP method.",
            "Looks like an HTTP header name.",
            "Looks like a key, token, or hash.",
            "Looks like an alphanumeric code identifier.",
            "Looks like an API token.",
            "Looks like embedded XML or HTML.",
            "Looks like an XML tag fragment.",
            "Looks like a JSON delimiter fragment.",
            "Looks like escaped whitespace.",
            "Looks like a format-only string.",
            "Looks like a code expression.",
            "Looks like a URL query string fragment.",
            "Looks like a character whitelist for string filtering.",
            "Looks like a structured technical identifier.",
            "Looks like a structured technical identifier with a qualifier.",
            "Looks like a short technical constant.",
            "Looks like a technical X++ string literal.",
            "Looks like a technical ProgID or dotted identifier.",
            "Looks like a JSON or XML property name.",
            "Looks like a code identifier.",
            "Looks like a code snippet or generated code template.",
        };

        return !candidate.Reasons.Any(lowValueReasons.Contains);
    }
}

public sealed record ScanResult(string ModelRootPath, IReadOnlyList<MetadataFile> MetadataFiles, ScanReport Report);
