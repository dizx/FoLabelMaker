using System.Xml.Linq;

namespace FoLabelMaker.Core.Metadata;

public sealed class FoMetadataDocument
{
    public required string FilePath { get; init; }
    public required XDocument Document { get; init; }
    public required string ElementType { get; init; }
    public required string ElementName { get; init; }
}
