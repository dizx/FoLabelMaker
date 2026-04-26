using System.Xml;
using System.Xml.Linq;
using FoLabelMaker.Core.Metadata;
using FoLabelMaker.Core.Reporting;

namespace FoLabelMaker.Core.Scanning;

public sealed class MetadataScanner
{
    private static readonly string[] SupportedFolders = ["AxClass", "AxTable", "AxForm", "AxMenuItemDisplay", "AxMenuItemOutput", "AxMenuItemAction", "AxEdt", "AxEnum", "AxReport"];
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
                    var document = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, cancellationToken);
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
                            report.IgnoredCandidates.Add(candidate);
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
}

public sealed record ScanResult(string ModelRootPath, IReadOnlyList<MetadataFile> MetadataFiles, ScanReport Report);
