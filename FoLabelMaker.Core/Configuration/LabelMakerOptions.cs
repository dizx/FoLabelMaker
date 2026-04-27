using System.Text.Json.Serialization;

namespace FoLabelMaker.Core.Configuration;

public sealed class LabelMakerOptions
{
    public string MetadataRootPath { get; init; } = string.Empty;
    public string? ModelName { get; init; }
    public string LabelPrefix { get; init; } = "@LBL";
    public string BaseLanguage { get; init; } = "en-US";
    public IReadOnlyList<string> TargetLanguages { get; init; } = [];
    public bool ApplyChanges { get; init; }
    public bool OverwriteTranslations { get; init; }
    public bool ReuseSimilarLabels { get; init; }
    public bool AllowCreateLabelFile { get; init; } = true;
    public bool AllowCrossModelChanges { get; init; }
    public string? OutputPath { get; init; }
    public string? PlanPath { get; init; }
    public string? OpenAiModel { get; init; }

    [JsonIgnore]
    public string NormalizedLabelPrefix => LabelPrefix.StartsWith('@') ? LabelPrefix[1..] : LabelPrefix;
}
