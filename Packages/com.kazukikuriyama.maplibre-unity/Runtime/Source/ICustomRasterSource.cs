using System;
using UnityEngine;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// User-supplied raster tile source. Lets callers serve raster tiles from
    /// any backend (procedural, on-disk, custom HTTP, render-to-texture, etc.)
    /// without going through the built-in URL template fetcher. Mirrors the
    /// MapLibre GL JS <c>type: "custom", dataType: "raster"</c> source object.
    /// </summary>
    /// <remarks>
    /// Register via <c>MapLibreMap.AddSource(string id, ICustomRasterSource source)</c>.
    /// The map exposes the source under that id so any raster layer (including
    /// hillshade if dataType were "raster-dem") can reference it.
    /// </remarks>
    public interface ICustomRasterSource
    {
        /// <summary>Tile pixel size (matches MapLibre's <c>tileSize</c>). Typically 256 or 512.</summary>
        int TileSize { get; }

        /// <summary>Lowest zoom served by this source. Tiles below are skipped.</summary>
        int MinZoom { get; }

        /// <summary>Highest zoom served by this source. Above this, parent tiles are over-zoomed.</summary>
        int MaxZoom { get; }

        /// <summary>
        /// Optional [west, south, east, north] in degrees. Tiles outside this
        /// box are not requested. Return null to serve world-wide.
        /// </summary>
        double[] Bounds { get; }

        /// <summary>Optional attribution string (rendered by the AttributionControl).</summary>
        string Attribution { get; }

        /// <summary>
        /// Asynchronously load a tile. Implementations call <paramref name="onSuccess"/>
        /// with a fully decoded <see cref="Texture2D"/> (the source takes ownership of
        /// caching it) or <paramref name="onError"/> with a human-readable message
        /// when the tile cannot be produced.
        ///
        /// <para>
        /// Callbacks must be invoked on the main thread -- Unity texture upload
        /// requires it. If your work is off-thread, marshal back via
        /// <c>UnityMainThreadDispatcher</c> or a coroutine before calling.
        /// </para>
        /// </summary>
        void LoadTile(CanonicalTileID tileId,
            Action<Texture2D> onSuccess,
            Action<string> onError);

        /// <summary>
        /// Optional hook fired when a tile leaves the cache or the source is
        /// disposed. Use to release tile-specific resources held by the
        /// implementation. Default no-op implementations should be valid.
        /// </summary>
        void UnloadTile(CanonicalTileID tileId);
    }
}
