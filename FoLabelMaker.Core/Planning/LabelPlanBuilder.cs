using FoLabelMaker.Core.Configuration;
using FoLabelMaker.Core.Labels;
using FoLabelMaker.Core.Reporting;
using FoLabelMaker.Core.Scanning;

namespace FoLabelMaker.Core.Planning;

public sealed class LabelPlanBuilder
{
    private readonly LabelReuseMatcher _labelReuseMatcher;
    private readonly LabelIdGenerator _labelIdGenerator;

    public LabelPlanBuilder(LabelReuseMatcher labelReuseMatcher, LabelIdGenerator labelIdGenerator)
    {
        _labelReuseMatcher = labelReuseMatcher;
        _labelIdGenerator = labelIdGenerator;
    }

    public LabelChangePlan Build(
        LabelMakerOptions options,
        string modelRootPath,
        IReadOnlyList<TextCandidate> candidates,
        IReadOnlyList<LabelFile> existingLabelFiles,
        ScanReport scanReport)
    {
        var baseLabelFile = ResolveBaseLabelFile(options, modelRootPath, existingLabelFiles);
        var plan = new LabelChangePlan
        {
            MetadataRootPath = options.MetadataRootPath,
            ModelRootPath = modelRootPath,
            ModelName = Path.GetFileName(modelRootPath),
            BaseLanguage = options.BaseLanguage,
            LabelPrefix = baseLabelFile.FileId,
            ScanReport = scanReport,
        };

        var workingEntries = baseLabelFile.Entries.ToList();
        var plannedNewLabelsByText = new Dictionary<string, LabelEntry>(StringComparer.Ordinal);
        foreach (var candidate in candidates.Where(candidate => candidate.IsUserFacing && candidate.Kind is TextCandidateKind.MetadataProperty or TextCandidateKind.XppStringLiteral))
        {
            plannedNewLabelsByText.TryGetValue(candidate.OriginalText, out var plannedEntry);
            var reused = plannedEntry;
            var reuseKind = plannedEntry is not null ? LabelReuseKind.PlannedLabel : LabelReuseKind.None;
            if (reused is null)
            {
                reused = _labelReuseMatcher.FindExact(candidate.OriginalText, workingEntries);
                if (reused is not null)
                {
                    reuseKind = LabelReuseKind.ExistingLabel;
                }
            }

            if (reused is null && options.ReuseSimilarLabels)
            {
                reused = _labelReuseMatcher.FindSimilar(candidate.OriginalText, workingEntries);
                if (reused is not null)
                {
                    reuseKind = LabelReuseKind.ExistingLabel;
                }
            }

            var labelId = reused?.Id ?? _labelIdGenerator.GenerateNextId(baseLabelFile.EntryIdPrefix, workingEntries);
            if (reused is null)
            {
                var newEntry = new LabelEntry { Id = labelId, Text = candidate.OriginalText };
                workingEntries.Add(newEntry);
                plannedNewLabelsByText[candidate.OriginalText] = newEntry;
                plan.LabelFileChanges.Add(new LabelFileChange
                {
                    FilePath = baseLabelFile.FilePath,
                    Language = options.BaseLanguage,
                    LabelId = labelId,
                    Text = candidate.OriginalText,
                    IsNewFile = !File.Exists(baseLabelFile.FilePath),
                });
            }

            var labelReference = $"@{baseLabelFile.FileId}:{labelId}";
            plan.Changes.Add(new LabelChange
            {
                ChangeType = candidate.Kind == TextCandidateKind.XppStringLiteral ? LabelChangeType.ReplaceXppLiteral : LabelChangeType.ReplaceMetadataValue,
                SourceFilePath = candidate.SourceFilePath,
                ElementType = candidate.ElementType,
                ElementName = candidate.ElementName,
                PropertyOrMethod = candidate.PropertyOrMethod,
                OriginalText = candidate.OriginalText,
                LabelId = labelId,
                ReplacementText = candidate.Kind == TextCandidateKind.XppStringLiteral ? string.Concat('"', labelReference, '"') : labelReference,
                GeneratedLabelReference = labelReference,
                XmlPath = candidate.XmlPath,
                XmlElementName = candidate.XmlElementName,
                CDataMarker = candidate.CDataMarker,
                ReuseKind = reuseKind,
                Reasons = candidate.Reasons,
            });
        }

        return plan;
    }

    private static LabelFile ResolveBaseLabelFile(LabelMakerOptions options, string modelRootPath, IReadOnlyList<LabelFile> existingLabelFiles)
    {
        var normalizedPrefix = options.NormalizedLabelPrefix;
        var baseLanguageFiles = existingLabelFiles
            .Where(file => string.Equals(file.Language, options.BaseLanguage, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var existing = baseLanguageFiles.FirstOrDefault(file =>
                string.Equals(file.FileId, normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(file.EntryIdPrefix, normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            ?? (baseLanguageFiles.Count == 1 ? baseLanguageFiles[0] : null);
        if (existing is not null)
        {
            return existing;
        }

        var labelDirectory = Directory.EnumerateDirectories(modelRootPath, "AxLabelFile", SearchOption.AllDirectories).FirstOrDefault()
            ?? Path.Combine(modelRootPath, "AxLabelFile");
        var fileId = options.ModelName ?? Path.GetFileName(modelRootPath);
        var descriptorFilePath = Path.Combine(labelDirectory, $"{fileId}_{options.BaseLanguage}.xml");
        var filePath = Path.Combine(labelDirectory, "LabelResources", options.BaseLanguage, $"{fileId}.{options.BaseLanguage}.label.txt");
        return new LabelFile
        {
            FilePath = filePath,
            DescriptorFilePath = descriptorFilePath,
            Language = options.BaseLanguage,
            FileId = fileId,
            EntryIdPrefix = fileId,
            Format = LabelFileFormat.Text,
            Entries = [],
        };
    }
}
