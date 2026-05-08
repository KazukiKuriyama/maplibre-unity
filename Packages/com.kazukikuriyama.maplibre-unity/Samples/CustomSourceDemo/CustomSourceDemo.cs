using System;
using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates ICustomRasterSource. A procedural source generates each
    /// raster tile on-the-fly from a deterministic value-noise function -- no
    /// network involved. Useful as a template for serving tiles from custom
    /// backends (local files, IPC, render-to-texture, etc.).
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class CustomSourceDemo : MonoBehaviour
    {
        private const string SourceId = "noise-source";
        private const string LayerId = "noise-layer";

        private MapLibreMap _map;
        private NoiseRasterSource _source;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[CustomSourceDemo] MapLibreMap not found on this GameObject");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            yield return null;

            _source = new NoiseRasterSource();
            _map.AddSource(SourceId, _source);

            // Render the noise as a translucent overlay above the basemap.
            _map.AddLayer(new LayerDefinition
            {
                Id = LayerId,
                Type = LayerType.Raster,
                Source = SourceId,
                Paint = new Dictionary<string, object>
                {
                    { "raster-opacity", 0.6 }
                }
            });
        }

        private void OnDestroy()
        {
            if (_map == null) return;
            if (_map.GetLayer(LayerId) != null) _map.RemoveLayer(LayerId);
            if (_map.GetSource(SourceId) != null) _map.RemoveSource(SourceId);
        }

        /// <summary>
        /// ICustomRasterSource implementation that returns a value-noise tile
        /// keyed by (z, x, y). Each tile is deterministic so panning back to a
        /// previously-visited area shows the same colours.
        /// </summary>
        private class NoiseRasterSource : ICustomRasterSource
        {
            public int TileSize => 256;
            public int MinZoom => 0;
            public int MaxZoom => 14;
            public double[] Bounds => null;
            public string Attribution => "Procedural noise (CustomSourceDemo)";

            public void LoadTile(CanonicalTileID tileId,
                Action<Texture2D> onSuccess, Action<string> onError)
            {
                try
                {
                    var tex = GenerateTile(tileId);
                    onSuccess?.Invoke(tex);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"GenerateTile failed: {e.Message}");
                }
            }

            public void UnloadTile(CanonicalTileID tileId) { }

            private Texture2D GenerateTile(CanonicalTileID tileId)
            {
                // Linear filter so the noise blends smoothly across zoom levels;
                // wrap clamped to avoid edge bleed at tile borders.
                var tex = new Texture2D(TileSize, TileSize, TextureFormat.RGBA32, mipChain: false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };

                var pixels = new Color32[TileSize * TileSize];
                int seed = HashTile(tileId);
                var rng = new System.Random(seed);

                // Pick a base hue per tile so neighbouring tiles look distinct;
                // jitter brightness inside the tile to give it visible texture.
                float baseHue = (float)rng.NextDouble();
                Color baseColor = Color.HSVToRGB(baseHue, 0.55f, 0.85f);

                for (int i = 0; i < pixels.Length; i++)
                {
                    float jitter = 0.6f + 0.4f * (float)rng.NextDouble();
                    pixels[i] = new Color32(
                        (byte)Mathf.Clamp(baseColor.r * jitter * 255f, 0, 255),
                        (byte)Mathf.Clamp(baseColor.g * jitter * 255f, 0, 255),
                        (byte)Mathf.Clamp(baseColor.b * jitter * 255f, 0, 255),
                        255);
                }

                tex.SetPixels32(pixels);
                tex.Apply(updateMipmaps: false);
                return tex;
            }

            // Stable hash so the same tile produces the same colours every time.
            private static int HashTile(CanonicalTileID t)
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + t.Z;
                    h = h * 31 + t.X;
                    h = h * 31 + t.Y;
                    return h;
                }
            }
        }
    }
}
