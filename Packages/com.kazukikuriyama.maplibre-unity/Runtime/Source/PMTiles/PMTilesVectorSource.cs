using System;
using UnityEngine;

namespace MapLibre.Unity.Source.PMTiles
{
    /// <summary>
    /// Wraps a <see cref="PMTilesArchive"/> as an <see cref="ICustomVectorSource"/>
    /// so PMTiles archives whose payload is MVT (vector) flow through the
    /// existing PBF parsing pipeline. Add via
    /// <c>map.AddSource(id, new PMTilesVectorSource(archive))</c>.
    /// </summary>
    public class PMTilesVectorSource : ICustomVectorSource
    {
        private readonly PMTilesArchive _archive;

        public int MinZoom => _archive.Header?.MinZoom ?? 0;
        public int MaxZoom => _archive.Header?.MaxZoom ?? 22;
        public double[] Bounds => _archive.Header == null
            ? null
            : new[] { _archive.Header.MinLon, _archive.Header.MinLat,
                      _archive.Header.MaxLon, _archive.Header.MaxLat };
        public string Attribution { get; }

        public PMTilesVectorSource(PMTilesArchive archive,
            MonoBehaviour coroutineHost = null,
            string attribution = null)
        {
            _archive = archive ?? throw new ArgumentNullException(nameof(archive));
            // coroutineHost retained for back-compat -- the archive runs on
            // Awaitable now, so it's no longer required.
            _ = coroutineHost;
            Attribution = attribution;
        }

        public void LoadTile(CanonicalTileID tileId,
            Action<byte[]> onSuccess, Action<string> onError)
        {
            _ = LoadTileAsync(tileId, onSuccess, onError);
        }

        private async Awaitable LoadTileAsync(CanonicalTileID tileId,
            Action<byte[]> onSuccess, Action<string> onError)
        {
            byte[] bytes;
            try
            {
                // The archive returns raw bytes still wearing the tile-compression
                // wrapper from the spec (gzip for MVT in practice). VectorTileSource
                // already gzip-unwraps incoming bytes when the magic matches, so
                // we just hand them straight through.
                bytes = await _archive.LoadTileAsync(tileId.Z, (uint)tileId.X, (uint)tileId.Y);
            }
            catch (Exception e)
            {
                onError?.Invoke(e.Message);
                return;
            }
            // null = sparse-archive miss; treat like an empty tile.
            onSuccess?.Invoke(bytes ?? Array.Empty<byte>());
        }

        public void UnloadTile(CanonicalTileID tileId)
        {
            // No per-tile state to release; the archive only caches leaf
            // directories.
        }
    }
}
