using System.Text;
using System.Xml.Linq;

namespace FoLabelMaker.Core.Labels;

public sealed class LabelFileWriter
{
    public async Task WriteAsync(LabelFile labelFile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(labelFile.FilePath)!);
        if (labelFile.Format == LabelFileFormat.Xml)
        {
            var document = new XDocument(
                new XElement("LabelFile",
                    new XAttribute("Language", labelFile.Language),
                    new XAttribute("Prefix", labelFile.FileId),
                    labelFile.Entries.Select(entry => new XElement("Label", new XAttribute("Id", entry.Id), entry.Text))));

            await using var stream = File.Create(labelFile.FilePath);
            await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
            return;
        }

        var builder = new StringBuilder();
        foreach (var entry in labelFile.Entries)
        {
            builder.Append(entry.Id).Append('=').AppendLine(entry.Text);
        }

        await File.WriteAllTextAsync(labelFile.FilePath, builder.ToString(), cancellationToken);

        if (!string.IsNullOrWhiteSpace(labelFile.DescriptorFilePath))
        {
            await WriteDescriptorAsync(labelFile, cancellationToken);
        }
    }

    private static async Task WriteDescriptorAsync(LabelFile labelFile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(labelFile.DescriptorFilePath!)!);
        var labelResourcesDirectoryPath = Path.GetDirectoryName(labelFile.FilePath)!;
        var axLabelFileDirectoryPath = Directory.GetParent(labelResourcesDirectoryPath)?.Parent?.FullName
            ?? Path.GetDirectoryName(labelFile.DescriptorFilePath!)!;
        var modelRootPath = Directory.GetParent(axLabelFileDirectoryPath)?.FullName;
        var modelContainerPath = modelRootPath is null ? null : Directory.GetParent(modelRootPath)?.FullName;
        var relativeUriInModelStore = modelContainerPath is null
            ? Path.GetFileName(labelFile.FilePath)
            : Path.GetRelativePath(modelContainerPath, labelFile.FilePath).Replace(Path.DirectorySeparatorChar, '\\');

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("AxLabelFile",
                new XAttribute(XNamespace.Xmlns + "i", "http://www.w3.org/2001/XMLSchema-instance"),
                new XElement("Name", $"{labelFile.FileId}_{labelFile.Language}"),
                new XElement("LabelContentFileName", Path.GetFileName(labelFile.FilePath)),
                new XElement("LabelFileId", labelFile.FileId),
                new XElement("RelativeUriInModelStore", relativeUriInModelStore)));

        await using var stream = File.Create(labelFile.DescriptorFilePath!);
        await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
    }
}
