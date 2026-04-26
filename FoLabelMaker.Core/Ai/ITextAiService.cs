namespace FoLabelMaker.Core.Ai;

public interface ITextAiService
{
    Task<IReadOnlyList<TranslationResult>> TranslateAsync(IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken);
    Task<IReadOnlyList<TextImprovementResult>> ImproveAsync(IReadOnlyList<TextImprovementRequest> requests, CancellationToken cancellationToken);
}
