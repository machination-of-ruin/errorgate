using Robust.Client.Graphics;

namespace Content.Client.Graphics;

/// <summary>
/// A cache for <see cref="Overlay"/>s to store per-viewport render resources, such as render targets.
/// </summary>
/// <remarks>
/// ERRORGATE: upstream (Wizden #40181) hooks <c>IClydeViewport.ClearCachedResources</c> to drop entries when a viewport
/// is destroyed. Our RobustToolbox pin has no such event, so entries are keyed by viewport reference
/// and are only disposed with the cache. Overlays dispose the cache in <c>DisposeBehavior</c>.
/// </remarks>
/// <typeparam name="T">The type of data stored in the cache.</typeparam>
public sealed class OverlayResourceCache<T> : IDisposable where T : class, IDisposable
{
    private readonly Dictionary<IClydeViewport, T> _cache = new();

    /// <summary>
    /// Get the data for a specific viewport, creating a new entry if necessary.
    /// </summary>
    /// <param name="viewport">The viewport for which to retrieve cached data.</param>
    /// <param name="factory">A delegate used to create the cached data, if necessary.</param>
    public T GetForViewport(IClydeViewport viewport, Func<IClydeViewport, T> factory)
    {
        return GetForViewport(viewport, out _, factory);
    }

    /// <summary>
    /// Get the data for a specific viewport, creating a new entry if necessary.
    /// </summary>
    /// <param name="viewport">The viewport for which to retrieve cached data.</param>
    /// <param name="wasCached">True if the data was pulled from cache, false if it was created anew.</param>
    /// <param name="factory">A delegate used to create the cached data, if necessary.</param>
    public T GetForViewport(IClydeViewport viewport, out bool wasCached, Func<IClydeViewport, T> factory)
    {
        if (_cache.TryGetValue(viewport, out var data))
        {
            wasCached = true;
            return data;
        }

        wasCached = false;
        data = factory(viewport);
        _cache.Add(viewport, data);
        return data;
    }

    public void Dispose()
    {
        foreach (var data in _cache.Values)
        {
            data.Dispose();
        }

        _cache.Clear();
    }
}
