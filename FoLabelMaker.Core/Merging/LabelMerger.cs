using System.Text.RegularExpressions;
using FoLabelMaker.Core.Labels;

namespace FoLabelMaker.Core.Merging;

public sealed partial class LabelMerger
{
    private readonly LabelFileReader _labelFileReader;
    private readonly LabelFileWriter _labelFileWriter;
    private readonly LabelIdGenerator _labelIdGenerator;

    public LabelMerger(LabelFileReader labelFileReader, LabelFileWriter labelFileWriter, LabelIdGenerator labelIdGenerator)
    {
        _labelFileReader = labelFileReader;
        _labelFileWriter = labelFileWriter;
        _labelIdGenerator = labelIdGenerator;
    }

    public async Task<LabelMergeReport> MergeAsync(
        string metadataRootPath,
        string modelRootPath,
        string modelName,
        string targetLabelFileId,
        string baseLanguage,
        IReadOnlyList<string> sourceLabelPrefixes,
        IReadOnlyList<string> sourceLabelFileIds,
        bool applyChanges,
        CancellationToken cancellationToken)
    {
        var targetFileId = targetLabelFileId.TrimStart('@');
        var labelFiles = (await _labelFileReader.ReadAsync(modelRootPath, cancellationToken)).ToList();
        var report = new LabelMergeReport
        {
            MetadataRootPath = metadataRootPath,
            ModelRootPath = modelRootPath,
            ModelName = modelName,
            TargetLabelFileId = targetFileId,
            Applied = applyChanges,
        };

        if (labelFiles.Count == 0)
        {
            report.ValidationErrors.Add("No label files were found for the model.");
            return report;
        }

        var baseFiles = labelFiles
            .Where(file => string.Equals(file.Language, baseLanguage, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var mappingSourceFiles = baseFiles.Count > 0 ? baseFiles : labelFiles;
        var targetBaseFile = mappingSourceFiles.FirstOrDefault(file => string.Equals(file.FileId, targetFileId, StringComparison.OrdinalIgnoreCase))
            ?? labelFiles.FirstOrDefault(file => string.Equals(file.FileId, targetFileId, StringComparison.OrdinalIgnoreCase));

        if (targetBaseFile is null)
        {
            report.ValidationErrors.Add($"Target label file id '{targetFileId}' was not found.");
            return report;
        }

        var mappings = BuildMappings(mappingSourceFiles, targetBaseFile, targetFileId, sourceLabelPrefixes, sourceLabelFileIds);
        report.Mappings = mappings.Values
            .OrderBy(mapping => mapping.SourceReference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (report.Mappings.Count == 0)
        {
            return report;
        }

        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ApplyLabelEntryChanges(labelFiles, mappings, targetFileId, changedFiles);
        await ApplyMetadataReferenceChangesAsync(modelRootPath, mappings, applyChanges, changedFiles, cancellationToken);

        if (applyChanges)
        {
            foreach (var labelFile in labelFiles.Where(file => changedFiles.Contains(file.FilePath)))
            {
                if (File.Exists(labelFile.FilePath))
                {
                    await File.WriteAllTextAsync(labelFile.FilePath + ".bak", await File.ReadAllTextAsync(labelFile.FilePath, cancellationToken), cancellationToken);
                }

                await _labelFileWriter.WriteAsync(labelFile, cancellationToken);
            }
        }

        report.ChangedFiles = changedFiles.Order(StringComparer.OrdinalIgnoreCase).ToList();
        return report;
    }

    private Dictionary<string, LabelMergeMapping> BuildMappings(
        IReadOnlyList<LabelFile> mappingSourceFiles,
        LabelFile targetBaseFile,
        string targetFileId,
        IReadOnlyList<string> sourceLabelPrefixes,
        IReadOnlyList<string> sourceLabelFileIds)
    {
        var mappings = new Dictionary<string, LabelMergeMapping>(StringComparer.OrdinalIgnoreCase);
        var targetEntries = targetBaseFile.Entries.ToList();
        var targetByText = new Dictionary<string, LabelEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in targetEntries.Where(entry => IsTargetEntry(entry.Id, targetFileId)))
        {
            var textKey = NormalizeText(entry.Text);
            if (!targetByText.TryAdd(textKey, entry))
            {
                AddMapping(mappings, targetFileId, entry.Id, targetFileId, targetByText[textKey].Id, entry.Text, "Duplicate target text");
            }
        }

        foreach (var file in mappingSourceFiles)
        {
            foreach (var entry in file.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    continue;
                }

                var isTargetFile = string.Equals(file.FileId, targetFileId, StringComparison.OrdinalIgnoreCase);
                if (isTargetFile && IsTargetEntry(entry.Id, targetFileId))
                {
                    continue;
                }

                if (!IsRequestedSource(file.FileId, entry.Id, sourceLabelPrefixes, sourceLabelFileIds))
                {
                    continue;
                }

                var textKey = NormalizeText(entry.Text);
                if (!targetByText.TryGetValue(textKey, out var targetEntry))
                {
                    targetEntry = new LabelEntry
                    {
                        Id = _labelIdGenerator.GenerateNextId(targetFileId, targetEntries),
                        Text = entry.Text,
                    };
                    targetEntries.Add(targetEntry);
                    targetByText[textKey] = targetEntry;
                }

                AddMapping(mappings, file.FileId, entry.Id, targetFileId, targetEntry.Id, entry.Text,
                    isTargetFile ? "Intra-file merge" : "Cross-file merge");
            }
        }

        return mappings;
    }

    private static void ApplyLabelEntryChanges(
        IReadOnlyList<LabelFile> labelFiles,
        IReadOnlyDictionary<string, LabelMergeMapping> mappings,
        string targetFileId,
        ISet<string> changedFiles)
    {
        foreach (var languageGroup in labelFiles.GroupBy(file => file.Language, StringComparer.OrdinalIgnoreCase))
        {
            var targetFile = languageGroup.FirstOrDefault(file => string.Equals(file.FileId, targetFileId, StringComparison.OrdinalIgnoreCase));
            if (targetFile is null)
            {
                continue;
            }

            var targetIds = new HashSet<string>(targetFile.Entries.Select(entry => entry.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var file in languageGroup)
            {
                var entriesToRemove = new HashSet<LabelEntry>();
                foreach (var entry in file.Entries.ToList())
                {
                    var sourceReference = BuildReference(file.FileId, entry.Id);
                    if (!mappings.TryGetValue(sourceReference, out var mapping))
                    {
                        continue;
                    }

                    var targetId = mapping.TargetReference.Split(':')[1];
                    if (!targetIds.Contains(targetId))
                    {
                        targetFile.Entries.Add(new LabelEntry { Id = targetId, Text = entry.Text });
                        targetIds.Add(targetId);
                        changedFiles.Add(targetFile.FilePath);
                    }

                    entriesToRemove.Add(entry);
                }

                foreach (var entry in entriesToRemove)
                {
                    file.Entries.Remove(entry);
                    changedFiles.Add(file.FilePath);
                }
            }
        }
    }

    private static async Task ApplyMetadataReferenceChangesAsync(
        string modelRootPath,
        IReadOnlyDictionary<string, LabelMergeMapping> mappings,
        bool applyChanges,
        ISet<string> changedFiles,
        CancellationToken cancellationToken)
    {
        var replacements = mappings.Values
            .Where(mapping => !string.Equals(mapping.SourceReference, mapping.TargetReference, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(mapping => mapping.SourceReference.Length)
            .ToList();

        foreach (var filePath in Directory.EnumerateFiles(modelRootPath, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xpp", StringComparison.OrdinalIgnoreCase)))
        {
            if (filePath.Contains($"{Path.DirectorySeparatorChar}AxLabelFile{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var updatedContent = content;
            foreach (var replacement in replacements)
            {
                updatedContent = updatedContent.Replace(replacement.SourceReference, replacement.TargetReference, StringComparison.Ordinal);
            }

            if (string.Equals(content, updatedContent, StringComparison.Ordinal))
            {
                continue;
            }

            changedFiles.Add(filePath);
            if (!applyChanges)
            {
                continue;
            }

            await File.WriteAllTextAsync(filePath + ".bak", content, cancellationToken);
            await File.WriteAllTextAsync(filePath, updatedContent, cancellationToken);
        }
    }

    private static void AddMapping(
        IDictionary<string, LabelMergeMapping> mappings,
        string sourceFileId,
        string sourceLabelId,
        string targetFileId,
        string targetLabelId,
        string text,
        string reason)
    {
        var sourceReference = BuildReference(sourceFileId, sourceLabelId);
        mappings[sourceReference] = new LabelMergeMapping
        {
            SourceReference = sourceReference,
            TargetReference = BuildReference(targetFileId, targetLabelId),
            Text = text,
            Reason = reason,
        };
    }

    private static bool IsTargetEntry(string labelId, string targetFileId) => labelId.StartsWith(targetFileId, StringComparison.OrdinalIgnoreCase);

    private static bool IsRequestedSource(
        string sourceFileId,
        string sourceLabelId,
        IReadOnlyList<string> sourceLabelPrefixes,
        IReadOnlyList<string> sourceLabelFileIds)
    {
        if (sourceLabelPrefixes.Count == 0 && sourceLabelFileIds.Count == 0)
        {
            return true;
        }

        return sourceLabelFileIds.Any(fileId => string.Equals(fileId.TrimStart('@'), sourceFileId, StringComparison.OrdinalIgnoreCase))
            || sourceLabelPrefixes.Any(prefix => sourceLabelId.TrimStart('@').StartsWith(prefix.TrimStart('@'), StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildReference(string fileId, string labelId) => $"@{fileId}:{labelId.TrimStart('@')}";

    private static string NormalizeText(string text) => WhiteSpaceRegex().Replace(text.Trim(), " ");

    [GeneratedRegex("\\s+")]
    private static partial Regex WhiteSpaceRegex();
}
