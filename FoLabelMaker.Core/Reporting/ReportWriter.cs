using System.Text.Json;

namespace FoLabelMaker.Core.Reporting;

public sealed class ReportWriter
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
    };

    public async Task WriteAsync<TReport>(TReport report, string filePath, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(report, _jsonSerializerOptions);
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }
}
