using Ailu.Api.Domain;

namespace Ailu.Api.Infrastructure;

// Everything a renderer needs to make one film. Photo slides point at the full-size photo in the photo
// store (PhotoKeys.File(..., PhotoProcessor.Full)); slides whose photo is gone have already been dropped.
// Captions are written by the renderer from the kinds, in Locale, like the app does for the preview.
public sealed record StoryRenderRequest(Guid StoryId, Guid CoupleId, int Year, string Locale, string NameA, string NameB,
    IReadOnlyList<StorySlide> Slides, IReadOnlyDictionary<Guid, string> PhotoKeys);

// Makes the MP4 of a year's story. The film is rendered on a server (roadmap: about 60 ₸ a film), and no
// rendering host is chosen yet, so the real renderer plugs in here later. Photos must stay in Kazakhstan
// (privacy doc), which limits where it can run.
public interface IStoryRenderer
{
    // The finished film. Throws RendererUnavailableException when nothing can render right now, which keeps
    // the order in the queue without counting a failed attempt.
    Task<byte[]> RenderAsync(StoryRenderRequest request, CancellationToken ct = default);
}

public sealed class RendererUnavailableException(string message) : Exception(message);

// Stands in until a renderer is set up: orders are taken and wait in the queue, and the log says why.
public sealed class UnconfiguredStoryRenderer : IStoryRenderer
{
    public Task<byte[]> RenderAsync(StoryRenderRequest request, CancellationToken ct = default) =>
        throw new RendererUnavailableException("No video renderer is configured on this server.");
}
