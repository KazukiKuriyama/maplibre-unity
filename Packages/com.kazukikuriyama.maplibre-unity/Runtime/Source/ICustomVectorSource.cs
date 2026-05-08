using System;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// User-supplied vector tile source. Lets callers serve MVT (Mapbox Vector
    /// Tile, MapLibre's standard PBF format) bytes from any backend -- local
    /// files, custom HTTP, IPC, or generated on the fly. Mirrors the MapLibre
    /// GL JS <c>type: "custom", dataType: "vector"</c> source object.
    /// </summary>
    /// <remarks>
    /// Register via <c>MapLibreMap.AddSource(string id, ICustomVectorSource source)</c>.
    /// The map parses the supplied bytes through the same PBF pipeline used by
    /// HTTP-fetched vector tiles, so all expression / filter / layout features
    /// work unchanged.
    /// </remarks>
    public interface ICustomVectorSource
    {
        /// <summary>Lowest zoom served by this source.</summary>
        int MinZoom { get; }

        /// <summary>Highest zoom served by this source. Above this, parent tiles are over-zoomed.</summary>
        int MaxZoom { get; }

        /// <summary>Optional [west, south, east, north] in degrees. Null = world-wide.</summary>
        double[] Bounds { get; }

        /// <summary>Optional attribution string (rendered by the AttributionControl).</summary>
        string Attribution { get; }

        /// <summary>
        /// Asynchronously load a tile as raw PBF bytes. Implementations call
        /// <paramref name="onSuccess"/> with the byte array (gzip is handled by
        /// the source if the bytes are gzip-prefixed) or <paramref name="onError"/>
        /// with a message when the tile cannot be produced.
        ///
        /// <para>
        /// Callbacks may be invoked from any thread; the source marshals heavy
        /// PBF parsing onto a background <see cref="System.Threading.Tasks.Task"/>.
        /// </para>
        /// </summary>
        void LoadTile(CanonicalTileID tileId,
            Action<byte[]> onSuccess,
            Action<string> onError);

        /// <summary>
        /// Optional hook fired when a tile leaves the cache or the source is
        /// disposed. Default no-op implementations are valid.
        /// </summary>
        void UnloadTile(CanonicalTileID tileId);
    }
}
