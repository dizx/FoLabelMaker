using System.Text.Json;
using FoLabelMaker.Core.Ai;
using FoLabelMaker.Core.Configuration;
using FoLabelMaker.Core.Improvement;
using FoLabelMaker.Core.Labels;
using FoLabelMaker.Core.Planning;
using FoLabelMaker.Core.Reporting;
using FoLabelMaker.Core.Scanning;

namespace FoLabelMaker.Core;

public sealed class FoLabelMakerService
{
    private readonly MetadataScanner _metadataScanner;
    private readonly LabelFileReader _labelFileReader;
    private readonly LabelPlanBuilder _labelPlanBuilder;
    private readonly LabelPlanApplier _labelPlanApplier;
    private readonly ReportWriter _reportWriter;
    private readonly HtmlReportWriter _htmlReportWriter;
    private readonly TextImprovementSuggester _textImprovementSuggester;
    private readonly ITextAiService _textAiService;
    private readonly LabelFileWriter _labelFileWriter;

    public FoLabelMakerService(
        MetadataScanner metadataScanner,
        LabelFileReader labelFileReader,
        LabelPlanBuilder labelPlanBuilder,
        LabelPlanApplier labelPlanApplier,
        ReportWriter reportWriter,
        HtmlReportWriter htmlReportWriter,
        TextImprovementSuggester textImprovementSuggester,
        ITextAiService textAiService,
        LabelFileWriter labelFileWriter)
    {
        _metadataScanner = metadataScanner;
        _labelFileReader = labelFileReader;
        _labelPlanBuilder = labelPlanBuilder;
        _labelPlanApplier = labelPlanApplier;
        _reportWriter = reportWriter;
        _htmlReportWriter = htmlReportWriter;
        _textImprovementSuggester = textImprovementSuggester;
        _textAiService = textAiService;
        _labelFileWriter = labelFileWriter;
    }

    public async Task<ScanReport> ScanAsync(LabelMakerOptions options, CancellationToken cancellationToken)
    {
        var scanResult = await _metadataScanner.ScanAsync(options.MetadataRootPath, options.ModelName, cancellationToken);
        foreach (var suggestion in _textImprovementSuggester.Suggest(scanResult.Report.DetectedCandidates.ToList()))
        {
            scanResult.Report.ImprovementSuggestions.Add(suggestion);
        }

        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            var outputPath = ResolveOutputPath(options.OutputPath, options.MetadataRootPath);
            await _reportWriter.WriteAsync(scanResult.Report, outputPath, cancellationToken);
            var htmlOutputPath = await _htmlReportWriter.WriteCompanionHtmlAsync(scanResult.Report, outputPath, cancellationToken);
            Console.WriteLine($"Wrote JSON report: {outputPath}");
            if (!string.IsNullOrWhiteSpace(htmlOutputPath))
            {
                Console.WriteLine($"Wrote HTML report: {htmlOutputPath}");
            }
        }

        return scanResult.Report;
    }

    public async Task<(LabelChangePlan Plan, PlanReport Report)> PlanAsync(LabelMakerOptions options, CancellationToken cancellationToken)
    {
        var scanResult = await _metadataScanner.ScanAsync(options.MetadataRootPath, options.ModelName, cancellationToken);
        foreach (var suggestion in _textImprovementSuggester.Suggest(scanResult.Report.DetectedCandidates.ToList()))
        {
            scanResult.Report.ImprovementSuggestions.Add(suggestion);
        }

        var existingLabels = await _labelFileReader.ReadAsync(scanResult.ModelRootPath, cancellationToken);
        var plan = _labelPlanBuilder.Build(options, scanResult.ModelRootPath, scanResult.Report.DetectedCandidates.ToList(), existingLabels, scanResult.Report);
        var report = new PlanReport
        {
            ScanReport = scanResult.Report,
            ExistingLabelsReused = plan.Changes
                .Where(change => change.ReuseKind == LabelReuseKind.ExistingLabel)
                .Select(change => change.GeneratedLabelReference)
                .Distinct()
                .ToList(),
            DuplicateTextsConsolidated = plan.Changes
                .Where(change => change.ReuseKind == LabelReuseKind.PlannedLabel)
                .Select(change => change.GeneratedLabelReference)
                .Distinct()
                .ToList(),
            LabelsCreated = plan.LabelFileChanges.Select(change => $"{change.Language}:{change.LabelId}").ToList(),
            ValidationErrors = plan.ValidationErrors,
        };

        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            var outputPath = ResolveOutputPath(options.OutputPath, options.MetadataRootPath);
            await _reportWriter.WriteAsync(plan, outputPath, cancellationToken);
            var htmlOutputPath = await _htmlReportWriter.WriteCompanionHtmlAsync(plan, outputPath, cancellationToken);
            Console.WriteLine($"Wrote JSON report: {outputPath}");
            if (!string.IsNullOrWhiteSpace(htmlOutputPath))
            {
                Console.WriteLine($"Wrote HTML report: {htmlOutputPath}");
            }
        }

        return (plan, report);
    }

    public async Task<ApplyResult> ApplyAsync(string planPath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(planPath, cancellationToken);
        var plan = JsonSerializer.Deserialize<LabelChangePlan>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Plan file is invalid.");
        return await _labelPlanApplier.ApplyAsync(plan, cancellationToken);
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(LabelMakerOptions options, CancellationToken cancellationToken)
    {
        Console.WriteLine("Preparing translation plan...");
        var (plan, _) = await PlanAsync(options, cancellationToken);
        Console.WriteLine($"Resolved model: {plan.ModelName}");
        Console.WriteLine($"Base language: {options.BaseLanguage}");
        Console.WriteLine($"Target languages: {string.Join(", ", options.TargetLanguages)}");
        var translationResults = await GenerateTranslationsAsync(plan, options, cancellationToken);
        if (translationResults.Count == 0)
        {
            Console.WriteLine("No translations were needed.");
            return translationResults;
        }

        Console.WriteLine($"Persisting {translationResults.Count} translation results...");
        await PersistTranslationsAsync(plan, options, translationResults, cancellationToken);
        Console.WriteLine("Translation files updated.");
        return translationResults;
    }

    public async Task<IReadOnlyList<TextImprovementResult>> ImproveAsync(LabelMakerOptions options, CancellationToken cancellationToken)
    {
        var scanResult = await _metadataScanner.ScanAsync(options.MetadataRootPath, options.ModelName, cancellationToken);
        var suggestions = _textImprovementSuggester.Suggest(scanResult.Report.DetectedCandidates.ToList()).ToList();
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            var outputPath = ResolveOutputPath(options.OutputPath, options.MetadataRootPath);
            await _reportWriter.WriteAsync(suggestions, outputPath, cancellationToken);
            var htmlOutputPath = await _htmlReportWriter.WriteCompanionHtmlAsync((IReadOnlyList<TextImprovementResult>)suggestions, outputPath, cancellationToken);
            Console.WriteLine($"Wrote JSON report: {outputPath}");
            if (!string.IsNullOrWhiteSpace(htmlOutputPath))
            {
                Console.WriteLine($"Wrote HTML report: {htmlOutputPath}");
            }
        }

        return suggestions;
    }

    private static string ResolveOutputPath(string outputPath, string requestedRootPath)
    {
        if (Path.IsPathRooted(outputPath))
        {
            return outputPath;
        }

        return Path.Combine(ResolveWorkingDirectory(requestedRootPath), outputPath);
    }

    private static string ResolveWorkingDirectory(string requestedRootPath) => requestedRootPath;

    private async Task<IReadOnlyList<TranslationResult>> GenerateTranslationsAsync(LabelChangePlan plan, LabelMakerOptions options, CancellationToken cancellationToken)
    {
        if (options.TargetLanguages.Count == 0)
        {
            return [];
        }

        var labelFiles = await _labelFileReader.ReadAsync(plan.ModelRootPath, cancellationToken);
        var baseLanguageFiles = labelFiles
            .Where(file => string.Equals(file.Language, options.BaseLanguage, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var baseLabelFile = baseLanguageFiles.FirstOrDefault(file =>
                string.Equals(file.FileId, plan.LabelPrefix, StringComparison.OrdinalIgnoreCase))
            ?? (baseLanguageFiles.Count == 1 ? baseLanguageFiles[0] : null);

        var baseEntries = baseLabelFile?.Entries.ToList()
            ?? plan.LabelFileChanges.Select(change => new LabelEntry { Id = change.LabelId, Text = change.Text }).ToList();

        var translationRequests = new List<TranslationRequest>();
        foreach (var targetLanguage in options.TargetLanguages)
        {
            var targetLanguageFiles = labelFiles
                .Where(file => string.Equals(file.Language, targetLanguage, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var targetLabelFile = targetLanguageFiles.FirstOrDefault(file =>
                    string.Equals(file.FileId, plan.LabelPrefix, StringComparison.OrdinalIgnoreCase))
                ?? (targetLanguageFiles.Count == 1 ? targetLanguageFiles[0] : null);

            foreach (var baseEntry in baseEntries)
            {
                var targetEntry = targetLabelFile?.Entries.FirstOrDefault(entry => string.Equals(entry.Id, baseEntry.Id, StringComparison.OrdinalIgnoreCase));
                if (targetEntry is not null && !options.OverwriteTranslations)
                {
                    continue;
                }

                translationRequests.Add(new TranslationRequest
                {
                    LabelId = baseEntry.Id,
                    SourceLanguage = options.BaseLanguage,
                    TargetLanguage = targetLanguage,
                    Text = baseEntry.Text,
                    Context = $"{plan.ModelName}:{baseEntry.Id}",
                });
            }
        }

        if (translationRequests.Count == 0)
        {
            return [];
        }

        var cachedTargetCount = translationRequests
            .GroupBy(request => request.TargetLanguage)
            .Select(group => $"{group.Key}: {group.Count()}")
            .ToArray();
        Console.WriteLine($"Preparing {translationRequests.Count} translation requests ({string.Join(", ", cachedTargetCount)}).");

        return await _textAiService.TranslateAsync(translationRequests, cancellationToken);
    }

    private async Task PersistTranslationsAsync(
        LabelChangePlan plan,
        LabelMakerOptions options,
        IReadOnlyList<TranslationResult> translationResults,
        CancellationToken cancellationToken)
    {
        var existingFiles = await _labelFileReader.ReadAsync(plan.ModelRootPath, cancellationToken);
        foreach (var languageGroup in translationResults.Where(result => result.IsValid).GroupBy(result => result.TargetLanguage))
        {
            var labelDirectory = Directory.EnumerateDirectories(plan.ModelRootPath, "AxLabelFile", SearchOption.AllDirectories).FirstOrDefault() ?? Path.Combine(plan.ModelRootPath, "AxLabelFile");
            var labelFile = existingFiles.FirstOrDefault(file =>
                    string.Equals(file.Language, languageGroup.Key, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(file.FileId, plan.LabelPrefix, StringComparison.OrdinalIgnoreCase))
                ?? new LabelFile
                {
                    FilePath = Path.Combine(labelDirectory, "LabelResources", languageGroup.Key, $"{plan.LabelPrefix}.{languageGroup.Key}.label.txt"),
                    DescriptorFilePath = Path.Combine(labelDirectory, $"{plan.LabelPrefix}_{languageGroup.Key}.xml"),
                    Language = languageGroup.Key,
                    FileId = plan.LabelPrefix,
                    EntryIdPrefix = plan.LabelPrefix,
                    Format = LabelFileFormat.Text,
                    Entries = [],
                };

            foreach (var translation in languageGroup)
            {
                var existingEntry = labelFile.Entries.FirstOrDefault(entry => string.Equals(entry.Id, translation.LabelId, StringComparison.OrdinalIgnoreCase));
                if (existingEntry is not null)
                {
                    if (options.OverwriteTranslations)
                    {
                        existingEntry.Text = translation.Text;
                    }

                    continue;
                }

                labelFile.Entries.Add(new LabelEntry { Id = translation.LabelId, Text = translation.Text });
            }

            await _labelFileWriter.WriteAsync(labelFile, cancellationToken);
        }
    }
}
