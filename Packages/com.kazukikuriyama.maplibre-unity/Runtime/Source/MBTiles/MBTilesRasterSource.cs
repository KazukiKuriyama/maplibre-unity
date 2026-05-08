using System;
using UnityEngine;

namespace MapLibre.Unity.Source.MBTiles
{
    /// <summary>
    /// <see cref="ICustomRasterSource"/> backed by an MBTiles SQLite file.
    /// Use when the metadata's <c>format</c> is png / jpg / webp.
    /// Add via <c>map.AddSource(id, new MBTilesRasterSource(reader))</c>.
    /// </summary>
    public class MBTilesRasterSource : ICustomRasterSource
    {
        private readonly MBTilesReader _reader;

        public int TileSize { get; }
        public int MinZoom => _reader.Metadata?.MinZoom ?? 0;
        public int MaxZoom => _reader.Metadata?.MaxZoom ?? 22;
        public double[] Bounds => _reader.Metadata?.Bounds;
        public string Attribution => _reader.Metadata?.Attribution;

        public MBTilesRasterSource(MBTilesReader reader, int tileSize = 256)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            TileSize = tileSize;
        }

        public void LoadTile(CanonicalTileID tileId,
            Action<Texture2D> onSuccess, Action<string> onError)
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

            if (bytes == null || bytes.Length == 0)
            {
                // Sparse cell -- not an error per the MBTiles spec.
                onError?.Invoke("MBTiles tile not in archive");
                return;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!tex.LoadImage(bytes, markNonReadable: true))
            {
                UnityEngine.Object.Destroy(tex);
                onError?.Invoke(
                    "MBTiles tile failed to decode as image (PNG/JPEG); check metadata.format");
                return;
            }
            onSuccess?.Invoke(tex);
        }

        public void UnloadTile(CanonicalTileID tileId) { }
    }
}
