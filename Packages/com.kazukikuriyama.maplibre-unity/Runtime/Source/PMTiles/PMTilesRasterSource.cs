using System;
using UnityEngine;

namespace MapLibre.Unity.Source.PMTiles
{
    /// <summary>
    /// Wraps a <see cref="PMTilesArchive"/> as an <see cref="ICustomRasterSource"/>
    /// so a PMTiles file (PNG / JPEG / WebP tiles) can be added to a map via
    /// <c>map.AddSource(id, new PMTilesRasterSource(archive))</c>.
    /// </summary>
    /// <remarks>
    /// The source delegates every <see cref="LoadTile"/> call to the archive
    /// via the supplied coroutine host. Texture decoding uses Unity's built-in
    /// <see cref="Texture2D.LoadImage"/> which handles PNG / JPEG natively;
    /// WebP requires the Unity WebP package.
    /// </remarks>
    public class PMTilesRasterSource : ICustomRasterSource
    {
        private readonly PMTilesArchive _archive;

        public int TileSize { get; }
        public int MinZoom => _archive.Header?.MinZoom ?? 0;
        public int MaxZoom => _archive.Header?.MaxZoom ?? 22;
        public double[] Bounds => _archive.Header == null
            ? null
            : new[] { _archive.Header.MinLon, _archive.Header.MinLat,
                      _archive.Header.MaxLon, _archive.Header.MaxLat };
        public string Attribution { get; }

        public PMTilesRasterSource(PMTilesArchive archive,
            MonoBehaviour coroutineHost = null,
            int tileSize = 256,
            string attribution = null)
        {
            _archive = archive ?? throw new ArgumentNullException(nameof(archive));
            // coroutineHost is no longer required (the archive uses Awaitable)
            // but we keep the parameter for backwards-compat with callers that
            // still pass `this`.
            _ = coroutineHost;
            TileSize = tileSize;
            Attribution = attribution;
        }

        public void LoadTile(CanonicalTileID tileId,
            Action<Texture2D> onSuccess, Action<string> onError)
        {
            _ = LoadTileAsync(tileId, onSuccess, onError);
        }

        private async Awaitable LoadTileAsync(CanonicalTileID tileId,
            Action<Texture2D> onSuccess, Action<string> onError)
        {
            byte[] bytes;
            try
            {
                bytes = await _archive.LoadTileAsync(tileId.Z, (uint)tileId.X, (uint)tileId.Y);
            }
            catch (Exception e)
            {
                onError?.Invoke(e.Message);
                return;
            }
            if (bytes == null)
            {
                onError?.Invoke("PMTiles tile not in archive");
                return;
            }
            if (bytes.Length == 0)
            {
                onError?.Invoke("PMTiles tile is empty");
                return;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!tex.LoadImage(bytes, markNonReadable: true))
            {
                UnityEngine.Object.Destroy(tex);
                onError?.Invoke("PMTiles tile failed to decode as image (PNG/JPEG)");
                return;
            }
            onSuccess?.Invoke(tex);
        }

        public void UnloadTile(CanonicalTileID tileId)
        {
            // No per-tile state; the archive caches leaf directories, not
            // tile bytes, so there's nothing to release here.
        }
    }
}
