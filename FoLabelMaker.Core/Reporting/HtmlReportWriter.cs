using System.Net;
using System.Text;
using System.Text.Json;
using FoLabelMaker.Core.Ai;
using FoLabelMaker.Core.Merging;
using FoLabelMaker.Core.Planning;
using FoLabelMaker.Core.Scanning;

namespace FoLabelMaker.Core.Reporting;

public sealed class HtmlReportWriter
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task<string?> WriteCompanionHtmlAsync<TReport>(TReport report, string jsonOutputPath, CancellationToken cancellationToken)
    {
        if (!jsonOutputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var htmlPath = BuildHtmlPath(jsonOutputPath);
        var createdAt = DateTimeOffset.Now;
        var html = BuildHtml(report, Path.GetFileName(htmlPath), createdAt);
        var directoryPath = Path.GetDirectoryName(htmlPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await File.WriteAllTextAsync(htmlPath, html, cancellationToken);
        return htmlPath;
    }

    private static string BuildHtmlPath(string jsonOutputPath)
    {
        var directoryPath = Path.GetDirectoryName(jsonOutputPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(jsonOutputPath);
        return Path.Combine(directoryPath, $"{fileNameWithoutExtension}-report.html");
    }

    private string BuildHtml<TReport>(TReport report, string fileName, DateTimeOffset createdAt)
    {
        return report switch
        {
            ScanReport scanReport => BuildScanHtml(scanReport, fileName, createdAt),
            LabelChangePlan labelChangePlan => BuildPlanHtml(labelChangePlan, fileName, createdAt),
            LabelMergeReport mergeReport => BuildMergeHtml(mergeReport, fileName, createdAt),
            IReadOnlyList<TextImprovementResult> improvements => BuildImprovementsHtml(improvements, fileName, createdAt),
            _ => BuildRawHtml(report, fileName, createdAt),
        };
    }

    private string BuildScanHtml(ScanReport report, string fileName, DateTimeOffset createdAt)
    {
        var body = new StringBuilder();
        body.Append(SummaryCards([
            ("Scanned Files", report.ScannedFiles.Count.ToString()),
            ("Detected", report.DetectedCandidates.Count.ToString()),
            ("Ignored", report.IgnoredCandidates.Count.ToString()),
            ("Missing Text", report.MissingTextProposals.Count.ToString()),
            ("Improvements", report.ImprovementSuggestions.Count.ToString()),
            ("Validation Errors", report.ValidationErrors.Count.ToString())
        ]));
        body.Append(Section("Detected Candidates", CandidateTable(report.DetectedCandidates)));
        body.Append(Section("Ignored Candidates (Not Planned)", CandidateTable(report.IgnoredCandidates)));
        body.Append(Section("Missing Text Proposals", CandidateTable(report.MissingTextProposals)));
        body.Append(Section("Improvement Suggestions", ImprovementTable(report.ImprovementSuggestions)));
        body.Append(Section("Validation Errors", StringList(report.ValidationErrors)));
        return WrapHtml("FoLabelMaker Scan Report", fileName, createdAt, body.ToString());
    }

    private string BuildPlanHtml(LabelChangePlan plan, string fileName, DateTimeOffset createdAt)
    {
        var report = plan.ScanReport ?? new ScanReport();
        var body = new StringBuilder();
        body.Append($"<div class='meta'><strong>Model:</strong> {Encode(plan.ModelName)}<br><strong>Model Root:</strong> <code>{Encode(plan.ModelRootPath)}</code><br><strong>Label File Id:</strong> <code>{Encode(plan.LabelPrefix)}</code></div>");
        body.Append(SummaryCards([
            ("Scanned Files", report.ScannedFiles.Count.ToString()),
            ("Planned Changes", plan.Changes.Count.ToString()),
            ("Label Adds", plan.LabelFileChanges.Count.ToString()),
            ("Missing Text", report.MissingTextProposals.Count.ToString()),
            ("Improvements", report.ImprovementSuggestions.Count.ToString()),
            ("Validation Errors", report.ValidationErrors.Count.ToString())
        ]));
        body.Append(Section("Planned Replacements", PlanChangeTable(plan.Changes)));
        body.Append(Section("Label File Adds", LabelFileChangeTable(plan.LabelFileChanges)));
        body.Append(Section("Missing Text Proposals", CandidateTable(report.MissingTextProposals)));
        body.Append(Section("Ignored Candidates", CandidateTable(report.IgnoredCandidates)));
        body.Append(Section("Validation Errors", StringList(report.ValidationErrors)));
        return WrapHtml($"FoLabelMaker Plan Report for {plan.ModelName}", fileName, createdAt, body.ToString());
    }

    private string BuildImprovementsHtml(IReadOnlyList<TextImprovementResult> improvements, string fileName, DateTimeOffset createdAt)
    {
        var body = new StringBuilder();
        body.Append(SummaryCards([("Improvements", improvements.Count.ToString())]));
        body.Append(Section("Improvement Suggestions", ImprovementTable(improvements)));
        return WrapHtml("FoLabelMaker Improvement Report", fileName, createdAt, body.ToString());
    }

    private string BuildMergeHtml(LabelMergeReport report, string fileName, DateTimeOffset createdAt)
    {
        var body = new StringBuilder();
        body.Append($"<div class='meta'><strong>Model:</strong> {Encode(report.ModelName)}<br><strong>Model Root:</strong> <code>{Encode(report.ModelRootPath)}</code><br><strong>Target Label File Id:</strong> <code>{Encode(report.TargetLabelFileId)}</code><br><strong>Mode:</strong> {Encode(report.Applied ? "Applied" : "Dry run")}</div>");
        body.Append(SummaryCards([
            ("Mappings", report.Mappings.Count.ToString()),
            ("Changed Files", report.ChangedFiles.Count.ToString()),
            ("Validation Errors", report.ValidationErrors.Count.ToString()),
            ("Applied", report.Applied ? "Yes" : "No")
        ]));
        body.Append(Section("Label Mappings", MergeMappingTable(report.Mappings)));
        body.Append(Section("Changed Files", StringList(report.ChangedFiles)));
        body.Append(Section("Validation Errors", StringList(report.ValidationErrors)));
        return WrapHtml($"FoLabelMaker Merge Report for {report.ModelName}", fileName, createdAt, body.ToString());
    }

    private string BuildRawHtml<TReport>(TReport report, string fileName, DateTimeOffset createdAt)
    {
        var json = JsonSerializer.Serialize(report, _jsonOptions);
        return WrapHtml("FoLabelMaker Report", fileName, createdAt, $"<pre>{Encode(json)}</pre>");
    }

    private static string SummaryCards(IReadOnlyList<(string Label, string Value)> stats)
    {
        var builder = new StringBuilder();
        builder.Append("<div class='cards'>");
        foreach (var stat in stats)
        {
            builder.Append($"<div class='card'><div class='label'>{Encode(stat.Label)}</div><div class='value'>{Encode(stat.Value)}</div></div>");
        }

        builder.Append("</div>");
        return builder.ToString();
    }

    private static string Section(string title, string content) => $"<section><h2>{Encode(title)}</h2>{content}</section>";

    private static string CandidateTable(IEnumerable<TextCandidate> candidates)
    {
        var candidateList = candidates.ToList();
        if (candidateList.Count == 0)
        {
            return Empty("No items.");
        }

        var rows = candidateList.Select(candidate =>
            $"<tr><td><code>{Encode(candidate.SourceFilePath)}</code></td><td>{Encode(candidate.LineNumber?.ToString() ?? string.Empty)}</td><td>{Encode(candidate.ElementType)}</td><td>{Encode(candidate.ElementName)}</td><td>{Encode(candidate.PropertyOrMethod)}</td><td>{Encode(candidate.OriginalText)}</td><td>{Encode(string.Join(" | ", candidate.Reasons))}</td></tr>");
        return Table(["File", "Line", "Element Type", "Element Name", "Property", "Text", "Reasons"], rows);
    }

    private static string PlanChangeTable(IEnumerable<LabelChange> changes)
    {
        var changeList = changes.ToList();
        if (changeList.Count == 0)
        {
            return Empty("No planned changes.");
        }

        var rows = changeList.Select(change =>
            $"<tr><td><code>{Encode(change.SourceFilePath)}</code></td><td>{Encode(change.OriginalText)}</td><td><code>{Encode(change.GeneratedLabelReference)}</code></td><td><code>{Encode(change.ReplacementText)}</code></td><td>{Encode(change.ReuseKind.ToString())}</td></tr>");
        return Table(["File", "Original Text", "Label", "Replacement", "Reuse"], rows);
    }

    private static string LabelFileChangeTable(IEnumerable<LabelFileChange> changes)
    {
        var changeList = changes.ToList();
        if (changeList.Count == 0)
        {
            return Empty("No label file changes.");
        }

        var rows = changeList.Select(change =>
            $"<tr><td><code>{Encode(change.FilePath)}</code></td><td><code>{Encode(change.LabelId)}</code></td><td>{Encode(change.Text)}</td><td>{Encode(change.IsNewFile ? "Yes" : "No")}</td></tr>");
        return Table(["File", "Label Id", "Text", "New File"], rows);
    }

    private static string ImprovementTable(IEnumerable<TextImprovementResult> improvements)
    {
        var improvementList = improvements.ToList();
        if (improvementList.Count == 0)
        {
            return Empty("No improvements.");
        }

        var rows = improvementList.Select(improvement =>
            $"<tr><td><code>{Encode(improvement.SourceFilePath)}</code></td><td>{Encode(improvement.OriginalText)}</td><td>{Encode(improvement.SuggestedText)}</td><td>{Encode(improvement.Confidence.ToString("P0"))}</td><td>{Encode(improvement.Reason)}</td></tr>");
        return Table(["File", "Original", "Suggested", "Confidence", "Reason"], rows);
    }

    private static string MergeMappingTable(IEnumerable<LabelMergeMapping> mappings)
    {
        var mappingList = mappings.ToList();
        if (mappingList.Count == 0)
        {
            return Empty("No label mappings.");
        }

        var rows = mappingList.Select(mapping =>
            $"<tr><td><code>{Encode(mapping.SourceReference)}</code></td><td><code>{Encode(mapping.TargetReference)}</code></td><td>{Encode(mapping.Text)}</td><td>{Encode(mapping.Reason)}</td></tr>");
        return Table(["Source", "Target", "Text", "Reason"], rows);
    }

    private static string StringList(IEnumerable<string> values)
    {
        var valueList = values.ToList();
        if (valueList.Count == 0)
        {
            return Empty("No items.");
        }

        return $"<ul>{string.Join(string.Empty, valueList.Select(value => $"<li><code>{Encode(value)}</code></li>"))}</ul>";
    }

    private static string Table(IReadOnlyList<string> headers, IEnumerable<string> rows)
    {
        var headerHtml = string.Join(string.Empty, headers.Select(header => $"<th>{Encode(header)}</th>"));
        return $"<div class='table-wrap'><table><thead><tr>{headerHtml}</tr></thead><tbody>{string.Join(string.Empty, rows)}</tbody></table></div>";
    }

    private static string Empty(string text) => $"<div class='empty'>{Encode(text)}</div>";

    private static string WrapHtml(string title, string fileName, DateTimeOffset createdAt, string body) => $@"<!doctype html>
<html lang='en'>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <title>{Encode(title)}</title>
  <style>
    body {{ margin: 0; font-family: Inter, Segoe UI, Arial, sans-serif; background: #0f172a; color: #e2e8f0; }}
    .page {{ max-width: 1400px; margin: 0 auto; padding: 24px; }}
    h1, h2 {{ margin: 0 0 14px; }}
    .hero, section {{ background: #111c34; border: 1px solid #26334d; border-radius: 14px; padding: 18px; margin-bottom: 18px; }}
    .meta {{ color: #c7d2fe; margin-bottom: 16px; line-height: 1.6; }}
    .cards {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; }}
    .card {{ background: #182847; border: 1px solid #2f456d; border-radius: 12px; padding: 14px; }}
    .label {{ color: #a5b4fc; font-size: 13px; }}
    .value {{ font-size: 28px; font-weight: 700; margin-top: 6px; }}
    .table-wrap {{ overflow: auto; border: 1px solid #2f456d; border-radius: 12px; }}
    table {{ width: 100%; border-collapse: collapse; min-width: 760px; }}
    th, td {{ text-align: left; vertical-align: top; padding: 10px 12px; border-bottom: 1px solid #2f456d; font-size: 13px; }}
    th {{ position: sticky; top: 0; background: #1c2d52; }}
    code, pre {{ font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }}
    .empty {{ padding: 24px; color: #94a3b8; background: #0b1221; border-radius: 12px; }}
    ul {{ margin: 0; padding-left: 18px; }}
    pre {{ white-space: pre-wrap; word-break: break-word; }}
  </style>
</head>
<body>
  <div class='page'>
    <div class='hero'>
      <h1>{Encode(title)}</h1>
      <div class='meta'><strong>Report File:</strong> <code>{Encode(fileName)}</code><br><strong>Created:</strong> {Encode(createdAt.ToString("yyyy-MM-dd HH:mm:ss zzz"))}</div>
    </div>
    {body}
  </div>
</body>
</html>";

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
