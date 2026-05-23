using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace FoLabelMaker.Core.Labels;

public sealed partial class LabelFileReader
{
    public async Task<IReadOnlyList<LabelFile>> ReadAsync(string modelRootPath, CancellationToken cancellationToken)
    {
        var axLabelDirectories = Directory.Exists(modelRootPath)
            ? Directory.EnumerateDirectories(modelRootPath, "AxLabelFile", SearchOption.AllDirectories).ToList()
            : [];
        var labelFiles = new List<LabelFile>();
        foreach (var directory in axLabelDirectories)
        {
            foreach (var descriptorPath in Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var descriptor = await TryReadDescriptorAsync(modelRootPath, descriptorPath, cancellationToken);
                if (descriptor is null)
                {
                    continue;
                }

                labelFiles.Add(descriptor);
            }

            foreach (var filePath in Directory.EnumerateFiles(directory, "*.txt", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (labelFiles.Any(labelFile => string.Equals(labelFile.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var (fileId, language) = ParseFileIdAndLanguage(filePath);
                var entries = await ReadEntriesAsync(filePath, cancellationToken);
                labelFiles.Add(new LabelFile
                {
                    FilePath = filePath,
                    DescriptorFilePath = InferDescriptorFilePath(filePath, fileId, language),
                    Language = language,
                    FileId = fileId,
                    EntryIdPrefix = InferEntryIdPrefix(fileId, entries),
                    Format = LabelFileFormat.Text,
                    Entries = entries,
                });
            }
        }

        return labelFiles;
    }

    private static async Task<LabelFile?> TryReadDescriptorAsync(string modelRootPath, string descriptorPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(descriptorPath);
        var document = await XDocument.LoadAsync(stream, LoadOptions.PreserveWhitespace, cancellationToken);
        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "AxLabelFile", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fileId = root.Element("LabelFileId")?.Value?.Trim();
        var labelContentFileName = root.Element("LabelContentFileName")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(fileId) || string.IsNullOrWhiteSpace(labelContentFileName))
        {
            return null;
        }

        var (_, language) = ParseFileIdAndLanguage(labelContentFileName);
        var relativeUriInModelStore = root.Element("RelativeUriInModelStore")?.Value?.Trim();
        var contentFilePath = ResolveContentFilePath(modelRootPath, Path.GetDirectoryName(descriptorPath)!, relativeUriInModelStore, labelContentFileName, language);
        var entries = File.Exists(contentFilePath)
            ? await ReadEntriesAsync(contentFilePath, cancellationToken)
            : [];

        return new LabelFile
        {
            FilePath = contentFilePath,
            DescriptorFilePath = descriptorPath,
            Language = language,
            FileId = fileId,
            EntryIdPrefix = InferEntryIdPrefix(fileId, entries),
            Format = LabelFileFormat.Text,
            Entries = entries,
        };
    }

    private static async Task<List<LabelEntry>> ReadEntriesAsync(string filePath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var entries = new List<LabelEntry>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            entries.Add(new LabelEntry
            {
                Id = line[..separatorIndex].Trim(),
                Text = line[(separatorIndex + 1)..],
                LineNumber = index + 1,
            });
        }

        return entries;
    }

    private static (string FileId, string Language) ParseFileIdAndLanguage(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".label.txt", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = fileName[..^".label.txt".Length];
            return SplitBaseName(baseName);
        }

        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return SplitBaseName(withoutExtension);
    }

    private static (string FileId, string Language) SplitBaseName(string baseName)
    {
        var cultureMatch = CompositeLanguageLabelFileNameRegex().Match(baseName);
        if (cultureMatch.Success)
        {
            return (cultureMatch.Groups["fileId"].Value, cultureMatch.Groups["language"].Value);
        }

        var simpleMatch = SimpleLanguageLabelFileNameRegex().Match(baseName);
        if (!simpleMatch.Success)
        {
            return (baseName, "en-US");
        }

        return (simpleMatch.Groups["fileId"].Value, simpleMatch.Groups["language"].Value);
    }

    private static string InferEntryIdPrefix(string fileId, IReadOnlyList<LabelEntry> entries)
    {
        var firstEntry = entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Id));
        if (firstEntry is null)
        {
            return fileId;
        }

        var prefixLength = 0;
        while (prefixLength < firstEntry.Id.Length && !char.IsDigit(firstEntry.Id[prefixLength]))
        {
            prefixLength++;
        }

        return prefixLength > 0 ? firstEntry.Id[..prefixLength] : fileId;
    }

    private static string ResolveContentFilePath(string modelRootPath, string descriptorDirectoryPath, string? relativeUriInModelStore, string labelContentFileName, string language)
    {
        if (!string.IsNullOrWhiteSpace(relativeUriInModelStore))
        {
            var normalizedRelativePath = relativeUriInModelStore.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var modelContainerPath = Directory.GetParent(modelRootPath)?.FullName;
            if (!string.IsNullOrWhiteSpace(modelContainerPath))
            {
                var candidate = Path.Combine(modelContainerPath, normalizedRelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var labelResourcesCandidate = Path.Combine(descriptorDirectoryPath, "LabelResources", language, labelContentFileName);
        if (File.Exists(labelResourcesCandidate))
        {
            return labelResourcesCandidate;
        }

        return Path.Combine(descriptorDirectoryPath, labelContentFileName);
    }

    private static string? InferDescriptorFilePath(string labelContentFilePath, string fileId, string language)
    {
        var labelResourcesDirectory = Path.GetDirectoryName(labelContentFilePath);
        if (labelResourcesDirectory is null)
        {
            return null;
        }

        var languageDirectory = new DirectoryInfo(labelResourcesDirectory);
        var labelResourcesRoot = languageDirectory.Parent;
        if (labelResourcesRoot is null || !string.Equals(labelResourcesRoot.Name, "LabelResources", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var axLabelFileDirectory = labelResourcesRoot.Parent?.FullName;
        return axLabelFileDirectory is null ? null : Path.Combine(axLabelFileDirectory, $"{fileId}_{language}.xml");
    }

    [GeneratedRegex("^(?<fileId>.+)[._-](?<language>[A-Za-z]{2}-[A-Za-z]{2})$")]
    private static partial Regex CompositeLanguageLabelFileNameRegex();

    [GeneratedRegex("^(?<fileId>.+)[._-](?<language>[A-Za-z]{2})$")]
    private static partial Regex SimpleLanguageLabelFileNameRegex();
}
