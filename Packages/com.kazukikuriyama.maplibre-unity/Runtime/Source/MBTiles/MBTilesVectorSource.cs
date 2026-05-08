using System;

namespace MapLibre.Unity.Source.MBTiles
{
    /// <summary>
    /// <see cref="ICustomVectorSource"/> backed by an MBTiles SQLite file.
    /// Use when the metadata's <c>format</c> is <c>"pbf"</c>. Tile rows are
    /// usually gzip-wrapped -- the existing <c>VectorTileSource</c> pipeline
    /// detects the gzip magic and unwraps before parsing, so we just hand
    /// raw bytes through.
    /// Add via <c>map.AddSource(id, new MBTilesVectorSource(reader))</c>.
    /// </summary>
    public class MBTilesVectorSource : ICustomVectorSource
    {
        private readonly MBTilesReader _reader;

        public int MinZoom => _reader.Metadata?.MinZoom ?? 0;
        public int MaxZoom => _reader.Metadata?.MaxZoom ?? 22;
        public double[] Bounds => _reader.Metadata?.Bounds;
        public string Attribution => _reader.Metadata?.Attribution;

        public MBTilesVectorSource(MBTilesReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public void LoadTile(CanonicalTileID tileId,
            Action<byte[]> onSuccess, Action<string> onError)
        {
            byte[] bytes;
            try
            {
                bytes = _reader.GetTile(tileId.Z, tileId.X, tileId.Y);
            }
            catch (Exception e)
            {
                onError?.Invoke($"MBTiles read failed: {e.Message}");
                return;
            }
            // VectorTileSource treats null/empty as "no data here" (HTTP 404
            // equivalent) rather than an error, so we forward an empty array
            // for missing rows instead of bubbling them as errors.
            onSuccess?.Invoke(bytes ?? Array.Empty<byte>());
        }

        public void UnloadTile(CanonicalTileID tileId) { }
    }
}
