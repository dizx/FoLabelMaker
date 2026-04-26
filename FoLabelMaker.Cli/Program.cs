using FoLabelMaker.Core;
using FoLabelMaker.Core.Ai;
using FoLabelMaker.Core.Configuration;
using FoLabelMaker.Core.Improvement;
using FoLabelMaker.Core.Labels;
using FoLabelMaker.Core.Metadata;
using FoLabelMaker.Core.Planning;
using FoLabelMaker.Core.Reporting;
using FoLabelMaker.Core.Scanning;
using FoLabelMaker.Core.Xpp;
using System.Text.Json;

return await CliProgram.RunAsync(args);

internal static class CliProgram
{
    private static readonly HashSet<string> FlagOptions =
    [
        "use-ai",
        "overwrite-translations",
        "overwrite",
        "reuse-similar-labels",
        "reuse-similar",
    ];

    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en-US",
        ["en-us"] = "en-US",
        ["en-gb"] = "en-GB",
        ["no"] = "nb-NO",
        ["nb"] = "nb-NO",
        ["nb-no"] = "nb-NO",
        ["nn"] = "nn-NO",
        ["nn-no"] = "nn-NO",
        ["sv"] = "sv-SE",
        ["sv-se"] = "sv-SE",
        ["da"] = "da-DK",
        ["da-dk"] = "da-DK",
        ["th"] = "th-TH",
        ["th-th"] = "th-TH",
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var appSettings = await LoadAppSettingsAsync();

        LabelMakerOptions options;
        try
        {
            options = ParseOptions(args.Skip(1).ToArray(), appSettings);
            options = ApplyResolvedModelName(options, ResolveModelName(options));
            options = ApplyDefaultOutputPath(command, options);
            options = ApplyPlanShortcut(command, options);
            ValidateCommandOptions(command, options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        var service = CreateService(options, appSettings);
        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        try
        {
            switch (command)
            {
                case "scan":
                    var scanReport = await service.ScanAsync(options, cancellationTokenSource.Token);
                    Console.WriteLine($"Scanned {scanReport.ScannedFiles.Count} files. Detected {scanReport.DetectedCandidates.Count} candidates. Ignored {scanReport.IgnoredCandidates.Count} candidates.");
                    return scanReport.ValidationErrors.Count == 0 ? 0 : 2;
                case "plan":
                    var (_, planReport) = await service.PlanAsync(options, cancellationTokenSource.Token);
                    Console.WriteLine($"Planned {planReport.LabelsCreated.Count} labels, reused {planReport.ExistingLabelsReused.Count} existing labels, and consolidated {planReport.DuplicateTextsConsolidated.Count} duplicate texts.");
                    return planReport.ValidationErrors.Count == 0 ? 0 : 2;
                case "apply":
                    if (string.IsNullOrWhiteSpace(options.PlanPath))
                    {
                        Console.Error.WriteLine("Missing required option: -plan <path>");
                        return 1;
                    }

                    var applyResult = await service.ApplyAsync(options.PlanPath, cancellationTokenSource.Token);
                    foreach (var changedFile in applyResult.ChangedFiles)
                    {
                        Console.WriteLine($"Changed: {changedFile}");
                    }

                    return applyResult.Succeeded ? 0 : 2;
                case "translate":
                    var translations = await service.TranslateAsync(options, cancellationTokenSource.Token);
                    Console.WriteLine($"Created or validated {translations.Count} translations.");
                    return translations.All(result => result.IsValid) ? 0 : 2;
                case "improve":
                    var improvements = await service.ImproveAsync(options, cancellationTokenSource.Token);
                    Console.WriteLine($"Generated {improvements.Count} improvement suggestions.");
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation canceled.");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static LabelMakerOptions ParseOptions(string[] args, AppSettings appSettings)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? positionalModelName = null;
        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            if (!current.StartsWith("-", StringComparison.Ordinal))
            {
                positionalModelName ??= current;
                continue;
            }

            var key = current.TrimStart('-');
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var nextIsValue = index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal);
            string value;
            if (nextIsValue)
            {
                value = args[++index];
            }
            else if (FlagOptions.Contains(key))
            {
                value = "true";
            }
            else
            {
                throw new InvalidOperationException($"Missing required value for option: -{key}");
            }

            if (!values.TryGetValue(key, out var bucket))
            {
                bucket = [];
                values[key] = bucket;
            }

            bucket.Add(value);
        }

        return new LabelMakerOptions
        {
            MetadataRootPath = GetOptional(values, "metadata-root") ?? appSettings.LabelMaker.MetadataRootPath ?? Environment.CurrentDirectory,
            ModelName = GetOptional(values, "model") ?? positionalModelName ?? appSettings.LabelMaker.ModelName,
            LabelPrefix = GetOptional(values, "label-prefix") ?? appSettings.LabelMaker.LabelPrefix ?? "@LBL",
            BaseLanguage = NormalizeLanguage(GetOptional(values, "base-lang") ?? GetOptional(values, "base-language") ?? appSettings.LabelMaker.BaseLanguage ?? "en-US"),
            TargetLanguages = NormalizeLanguages(GetMany(values, "target-lang").Count > 0
                ? GetMany(values, "target-lang")
                : GetMany(values, "target-language").Count > 0
                    ? GetMany(values, "target-language")
                    : appSettings.LabelMaker.TargetLanguages ?? []),
            UseAi = GetNullableBool(values, "use-ai") ?? appSettings.LabelMaker.UseAi ?? false,
            OutputPath = GetOptional(values, "output"),
            PlanPath = GetOptional(values, "plan"),
            OpenAiModel = GetOptional(values, "openai-model") ?? appSettings.OpenAi.Model,
            OverwriteTranslations = GetNullableBool(values, "overwrite") ?? GetNullableBool(values, "overwrite-translations") ?? appSettings.LabelMaker.OverwriteTranslations ?? false,
            ReuseSimilarLabels = GetNullableBool(values, "reuse-similar") ?? GetNullableBool(values, "reuse-similar-labels") ?? appSettings.LabelMaker.ReuseSimilarLabels ?? false,
        };
    }

    private static FoLabelMakerService CreateService(LabelMakerOptions options, AppSettings appSettings)
    {
        var classifier = new TextCandidateClassifier();
        var xppScanner = new XppStringLiteralScanner();
        var metadataScanner = new MetadataScanner(new FoMetadataTextScanner(classifier, xppScanner));
        var labelFileReader = new LabelFileReader();
        var labelFileWriter = new LabelFileWriter();
        var replacementEngine = new FoMetadataReplacementEngine(new XppStringReplacementEngine());
        var cacheFilePath = appSettings.OpenAi.CacheFilePath;
        var resolvedCacheFilePath = string.IsNullOrWhiteSpace(cacheFilePath)
            ? Path.Combine(Environment.CurrentDirectory, ".fo-labelmaker-ai-cache.json")
            : ResolveConfigPath(cacheFilePath);
        var openAiOptions = new OpenAiOptions
        {
            ApiKey = ResolveConfiguredApiKey(appSettings.OpenAi),
            Model = options.OpenAiModel ?? appSettings.OpenAi.Model ?? "gpt-5-mini",
            ApiKeyEnvironmentVariable = appSettings.OpenAi.ApiKeyEnvironmentVariable ?? "OPENAI_API_KEY",
            BaseUrl = appSettings.OpenAi.BaseUrl ?? "https://api.openai.com/v1/chat/completions",
            CacheFilePath = resolvedCacheFilePath,
        };

        return new FoLabelMakerService(
            metadataScanner,
            labelFileReader,
            new LabelPlanBuilder(new LabelReuseMatcher(), new LabelIdGenerator()),
            new LabelPlanApplier(replacementEngine, labelFileReader, labelFileWriter),
            new ReportWriter(),
            new HtmlReportWriter(),
            new TextImprovementSuggester(),
            new OpenAiTextAiService(new HttpClient(), openAiOptions),
            labelFileWriter);
    }

    private static string? GetOptional(Dictionary<string, List<string>> values, string key) => values.TryGetValue(key, out var entries) ? entries.LastOrDefault() : null;

    private static IReadOnlyList<string> GetMany(Dictionary<string, List<string>> values, string key) => values.TryGetValue(key, out var entries) ? entries : [];

    private static bool GetBool(Dictionary<string, List<string>> values, string key) => values.TryGetValue(key, out var entries) && entries.LastOrDefault() is "true";

    private static bool? GetNullableBool(Dictionary<string, List<string>> values, string key) => values.TryGetValue(key, out var entries) ? entries.LastOrDefault() is "true" : null;

    private static IReadOnlyList<string> NormalizeLanguages(IReadOnlyList<string> languages) => languages.Select(NormalizeLanguage).ToList();

    private static string NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return language;
        }

        var trimmedLanguage = language.Trim();
        if (LanguageAliases.TryGetValue(trimmedLanguage, out var alias))
        {
            return alias;
        }

        var parts = trimmedLanguage.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => parts[0].ToLowerInvariant(),
            _ => $"{parts[0].ToLowerInvariant()}-{parts[1].ToUpperInvariant()}",
        };
    }

    private static LabelMakerOptions ApplyDefaultOutputPath(string command, LabelMakerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return options;
        }

        var resolvedModelName = ResolveModelName(options);
        var normalizedModelName = string.IsNullOrWhiteSpace(resolvedModelName)
            ? "report"
            : resolvedModelName.Trim().ToLowerInvariant();

        var defaultOutputPath = command switch
        {
            "scan" => $"{normalizedModelName}-scan.json",
            "plan" => $"{normalizedModelName}-plan.json",
            "improve" => $"{normalizedModelName}-improvements.json",
            _ => null,
        };

        if (defaultOutputPath is null)
        {
            return options;
        }

        return new LabelMakerOptions
        {
            MetadataRootPath = options.MetadataRootPath,
            ModelName = options.ModelName,
            LabelPrefix = options.LabelPrefix,
            BaseLanguage = options.BaseLanguage,
            TargetLanguages = options.TargetLanguages,
            UseAi = options.UseAi,
            ApplyChanges = options.ApplyChanges,
            OverwriteTranslations = options.OverwriteTranslations,
            ReuseSimilarLabels = options.ReuseSimilarLabels,
            AllowCreateLabelFile = options.AllowCreateLabelFile,
            AllowCrossModelChanges = options.AllowCrossModelChanges,
            OutputPath = defaultOutputPath,
            PlanPath = options.PlanPath,
            OpenAiModel = options.OpenAiModel,
        };
    }

    private static void ValidateCommandOptions(string command, LabelMakerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PlanPath) && !string.Equals(command, "apply", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Option -plan is only valid with the apply command. Use 'plan' as the command when you want to create a plan.");
        }

        var usesTranslationOptions = options.TargetLanguages.Count > 0 || options.UseAi || options.OverwriteTranslations;
        if (usesTranslationOptions && !string.Equals(command, "translate", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Options -target-lang, -target-language, -use-ai, and translation overwrite settings are only valid with the translate command.");
        }
    }

    private static LabelMakerOptions ApplyPlanShortcut(string command, LabelMakerOptions options)
    {
        if (!string.Equals(command, "apply", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(options.PlanPath))
        {
            return options;
        }

        var workingRoot = options.MetadataRootPath;
        var requestedPlanPath = options.PlanPath.Trim();
        var resolvedPlanPath = requestedPlanPath;

        if (!Path.HasExtension(requestedPlanPath) && !requestedPlanPath.Contains(Path.DirectorySeparatorChar) && !requestedPlanPath.Contains(Path.AltDirectorySeparatorChar))
        {
            resolvedPlanPath = Path.Combine(workingRoot, $"{requestedPlanPath.ToLowerInvariant()}-plan.json");
        }
        else if (!Path.IsPathRooted(requestedPlanPath))
        {
            resolvedPlanPath = Path.Combine(workingRoot, requestedPlanPath);
        }

        return new LabelMakerOptions
        {
            MetadataRootPath = options.MetadataRootPath,
            ModelName = options.ModelName,
            LabelPrefix = options.LabelPrefix,
            BaseLanguage = options.BaseLanguage,
            TargetLanguages = options.TargetLanguages,
            UseAi = options.UseAi,
            ApplyChanges = options.ApplyChanges,
            OverwriteTranslations = options.OverwriteTranslations,
            ReuseSimilarLabels = options.ReuseSimilarLabels,
            AllowCreateLabelFile = options.AllowCreateLabelFile,
            AllowCrossModelChanges = options.AllowCrossModelChanges,
            OutputPath = options.OutputPath,
            PlanPath = resolvedPlanPath,
            OpenAiModel = options.OpenAiModel,
        };
    }

    private static string? ResolveModelName(LabelMakerOptions options)
    {
        if (!Directory.Exists(options.MetadataRootPath))
        {
            return options.ModelName;
        }

        var searchRoot = Directory.Exists(Path.Combine(options.MetadataRootPath, "Metadata"))
            ? Path.Combine(options.MetadataRootPath, "Metadata")
            : options.MetadataRootPath;

        var candidateModelRoots = Directory.EnumerateDirectories(searchRoot)
            .Select(directory => Path.Combine(directory, Path.GetFileName(directory)))
            .Where(Directory.Exists)
            .ToList();

        if (string.IsNullOrWhiteSpace(options.ModelName))
        {
            return candidateModelRoots.Count == 1 ? Path.GetFileName(candidateModelRoots[0]) : null;
        }

        var exactMatch = candidateModelRoots.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), options.ModelName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exactMatch))
        {
            return Path.GetFileName(exactMatch);
        }

        var containsMatches = candidateModelRoots
            .Where(path => Path.GetFileName(path).Contains(options.ModelName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return containsMatches.Count == 1 ? Path.GetFileName(containsMatches[0]) : options.ModelName;
    }

    private static LabelMakerOptions ApplyResolvedModelName(LabelMakerOptions options, string? resolvedModelName)
    {
        if (string.IsNullOrWhiteSpace(resolvedModelName) || string.Equals(resolvedModelName, options.ModelName, StringComparison.Ordinal))
        {
            return options;
        }

        return new LabelMakerOptions
        {
            MetadataRootPath = options.MetadataRootPath,
            ModelName = resolvedModelName,
            LabelPrefix = options.LabelPrefix,
            BaseLanguage = options.BaseLanguage,
            TargetLanguages = options.TargetLanguages,
            UseAi = options.UseAi,
            ApplyChanges = options.ApplyChanges,
            OverwriteTranslations = options.OverwriteTranslations,
            ReuseSimilarLabels = options.ReuseSimilarLabels,
            AllowCreateLabelFile = options.AllowCreateLabelFile,
            AllowCrossModelChanges = options.AllowCrossModelChanges,
            OutputPath = options.OutputPath,
            PlanPath = options.PlanPath,
            OpenAiModel = options.OpenAiModel,
        };
    }

    private static async Task<AppSettings> LoadAppSettingsAsync()
    {
        await Task.CompletedTask;
        return LoadAppSettings();
    }

    private static AppSettings LoadAppSettings()
    {
        var filePath = ResolveAppSettingsPath();
        if (filePath is null || !File.Exists(filePath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? new AppSettings();
    }

    private static string? ResolveAppSettingsPath()
    {
        var currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, "appsettings.json");
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        return File.Exists(baseDirectoryPath) ? baseDirectoryPath : null;
    }

    private static string ResolveConfigPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var appSettingsPath = ResolveAppSettingsPath();
        var baseDirectory = appSettingsPath is null ? Environment.CurrentDirectory : Path.GetDirectoryName(appSettingsPath)!;
        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static string? ResolveConfiguredApiKey(OpenAiSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return settings.ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(settings.ApiKeyEnvironmentVariable) && LooksLikeApiKey(settings.ApiKeyEnvironmentVariable))
        {
            return settings.ApiKeyEnvironmentVariable;
        }

        return null;
    }

    private static bool LooksLikeApiKey(string value) => value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);

    private static void PrintUsage()
    {
        Console.WriteLine("FoLabelMaker scan -metadata-root <path> -model <modelName> -label-prefix <prefix> -base-lang en-US -output report.json");
        Console.WriteLine("FoLabelMaker plan -metadata-root <path> -model <modelName> -label-prefix <prefix> -base-lang en-US -output label-plan.json");
        Console.WriteLine("FoLabelMaker apply -metadata-root <path> -plan label-plan.json");
        Console.WriteLine("FoLabelMaker translate -metadata-root <path> -model <modelName> -label-prefix <prefix> -base-lang en-US -target-lang nb-NO -use-ai");
        Console.WriteLine("FoLabelMaker improve -metadata-root <path> -model <modelName> -output improvements.json");
    }
}
