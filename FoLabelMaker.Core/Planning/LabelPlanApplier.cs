using System.Text.Json;
using FoLabelMaker.Core.Labels;
using FoLabelMaker.Core.Metadata;

namespace FoLabelMaker.Core.Planning;

public sealed class LabelPlanApplier
{
    private readonly FoMetadataReplacementEngine _replacementEngine;
    private readonly LabelFileReader _labelFileReader;
    private readonly LabelFileWriter _labelFileWriter;

    public LabelPlanApplier(FoMetadataReplacementEngine replacementEngine, LabelFileReader labelFileReader, LabelFileWriter labelFileWriter)
    {
        _replacementEngine = replacementEngine;
        _labelFileReader = labelFileReader;
        _labelFileWriter = labelFileWriter;
    }

    public async Task<ApplyResult> ApplyAsync(LabelChangePlan plan, CancellationToken cancellationToken)
    {
        if (plan.ValidationErrors.Count > 0)
        {
            return new ApplyResult(false, [], plan.ValidationErrors.ToList());
        }

        var changedFiles = new List<string>();
        foreach (var fileGroup in plan.Changes.GroupBy(change => change.SourceFilePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalContent = await File.ReadAllTextAsync(fileGroup.Key, cancellationToken);
            var updatedContent = _replacementEngine.Apply(originalContent, fileGroup.ToList());
            if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
            {
                continue;
            }

            await File.WriteAllTextAsync(fileGroup.Key + ".bak", originalContent, cancellationToken);
            await File.WriteAllTextAsync(fileGroup.Key, updatedContent, cancellationToken);
            changedFiles.Add(fileGroup.Key);
        }

        var allLabelFiles = await _labelFileReader.ReadAsync(plan.ModelRootPath, cancellationToken);
        foreach (var labelGroup in plan.LabelFileChanges.GroupBy(change => change.FilePath))
        {
            var labelFile = allLabelFiles.FirstOrDefault(file => string.Equals(file.FilePath, labelGroup.Key, StringComparison.OrdinalIgnoreCase))
                ?? new LabelFile
                {
                    FilePath = labelGroup.Key,
                    DescriptorFilePath = InferDescriptorFilePath(labelGroup.Key, plan.LabelPrefix, labelGroup.First().Language),
                    Language = labelGroup.First().Language,
                    FileId = plan.LabelPrefix,
                    EntryIdPrefix = plan.LabelPrefix,
                    Format = LabelFileFormat.Text,
                    Entries = [],
                };

            foreach (var change in labelGroup)
            {
                if (labelFile.Entries.Any(entry => string.Equals(entry.Id, change.LabelId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                labelFile.Entries.Add(new LabelEntry { Id = change.LabelId, Text = change.Text });
            }

            if (File.Exists(labelFile.FilePath))
            {
                await File.WriteAllTextAsync(labelFile.FilePath + ".bak", await File.ReadAllTextAsync(labelFile.FilePath, cancellationToken), cancellationToken);
            }

            await _labelFileWriter.WriteAsync(labelFile, cancellationToken);
            changedFiles.Add(labelFile.FilePath);
        }

        var manifestPath = Path.Combine(ResolveWorkingDirectory(plan.MetadataRootPath), "fo-labelmaker-apply-manifest.json");
        var manifestJson = JsonSerializer.Serialize(new { changedFiles }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);
        changedFiles.Add(manifestPath);

        return new ApplyResult(true, changedFiles, []);
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

    private static string ResolveWorkingDirectory(string requestedRootPath) => requestedRootPath;
}

public sealed record ApplyResult(bool Succeeded, IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> Errors);
