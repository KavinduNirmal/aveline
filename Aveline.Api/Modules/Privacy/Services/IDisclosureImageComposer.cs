namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Renders the first-contact disclosure image for a boutique. Synchronous and CPU-bound on purpose:
/// it is called from a background worker, never from a request path, and making it async would only
/// hide that it blocks a thread.
/// </summary>
public interface IDisclosureImageComposer
{
    /// <summary>
    /// The layout version. Folded into the cache key and the Cloudinary public id, so changing the
    /// drawing produces a new URL instead of leaving a stale image behind a cached one.
    /// </summary>
    string LayoutVersion { get; }

    /// <summary>
    /// Draws the lockup for <paramref name="boutiqueDisplayName"/> as a JPEG.
    /// </summary>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    byte[] Compose(string boutiqueDisplayName);
}
