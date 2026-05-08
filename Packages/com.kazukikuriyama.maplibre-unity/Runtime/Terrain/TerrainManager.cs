using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.Terrain
{
    /// <summary>
    /// Tracks DEM tiles loaded for the active terrain source and exposes them to
    /// renderers that need GPU-side elevation sampling. Lifecycle is owned by
    /// MapLibreMap; the manager itself does not fetch tiles -- it only registers
    /// textures handed in by the existing tile loading pipeline.
    /// </summary>
    public class TerrainManager
    {
        public string SourceId { get; private set; }
        public bool IsTerrarium { get; private set; } = true;
        public float Exaggeration { get; set; } = 1f;
        public int SourceMaxZoom { get; set; } = 15;

        private readonly Dictionary<CanonicalTileID, Texture2D> _demTextures = new();

        /// <summary>Fired when a new DEM tile becomes available, so dependent renderers can refresh.</summary>
        public event Action<CanonicalTileID> OnTerrainTileReady;

        public void Initialize(string sourceId, string encoding, int sourceMaxZoom)
        {
            SourceId = sourceId;
            IsTerrarium = string.Equals(encoding, "terrarium", StringComparison.OrdinalIgnoreCase);
            SourceMaxZoom = sourceMaxZoom;
        }

        public void RegisterTile(CanonicalTileID tileId, Texture2D tex)
        {
            _demTextures[tileId] = tex;
            OnTerrainTileReady?.Invoke(tileId);
        }

        public void UnregisterTile(CanonicalTileID tileId)
        {
            _demTextures.Remove(tileId);
        }

        public bool HasTile(CanonicalTileID tileId) => _demTextures.ContainsKey(tileId);

        /// <summary>
        /// True if a DEM tile is loaded that covers (is the same tile as, or an
        /// ancestor of) the given render tile. Used to decide whether terrain
        /// vertex displacement is currently possible.
        /// </summary>
        public bool HasAnyDemFor(CanonicalTileID renderTileId)
        {
            var probe = renderTileId;
            while (probe.Z > SourceMaxZoom && probe.Z > 0)
                probe = probe.Parent();
            while (probe.Z >= 0)
            {
                if (_demTextures.ContainsKey(probe)) return true;
                if (probe.Z == 0) break;
                probe = probe.Parent();
            }
            return false;
        }

        public Texture2D GetTile(CanonicalTileID tileId)
        {
            return _demTextures.TryGetValue(tileId, out var t) ? t : null;
        }

        /// <summary>
        /// Resolve the actual DEM tile to use for a target render tile, walking up
        /// the parent chain when over-zoomed past the source maxzoom. Returns the
        /// matched tile id, the loaded texture, and a sub-region (uvOffset/uvScale)
        /// in [0,1] for sampling. Returns false if no ancestor DEM is loaded.
        /// </summary>
        public bool TryResolveDem(CanonicalTileID renderTileId,
            out CanonicalTileID demTileId, out Texture2D demTexture,
            out Vector2 uvOffset, out Vector2 uvScale)
        {
            demTileId = renderTileId;
            // Clamp to source maxzoom.
            while (demTileId.Z > SourceMaxZoom && demTileId.Z > 0)
                demTileId = demTileId.Parent();

            // Walk up parents until we find a loaded tile.
            var probe = demTileId;
            while (probe.Z >= 0)
            {
                if (_demTextures.TryGetValue(probe, out var tex))
                {
                    demTileId = probe;
                    demTexture = tex;
                    ComputeSubRegion(renderTileId, probe, out uvOffset, out uvScale);
                    return true;
                }
                if (probe.Z == 0) break;
                probe = probe.Parent();
            }

            demTexture = null;
            uvOffset = Vector2.zero;
            uvScale = Vector2.one;
            return false;
        }

        private static void ComputeSubRegion(CanonicalTileID child, CanonicalTileID parent,
            out Vector2 uvOffset, out Vector2 uvScale)
        {
            int dz = child.Z - parent.Z;
            if (dz <= 0)
            {
                uvOffset = Vector2.zero;
                uvScale = Vector2.one;
                return;
            }
            int n = 1 << dz;
            int childParentX = child.X >> dz;
            int childParentY = child.Y >> dz;
            int relX = child.X - childParentX * n;
            int relY = child.Y - childParentY * n;
            float scale = 1f / n;
            // UV: NW corner = (0, 1). DEM texture y is flipped (V increases upward).
            uvOffset = new Vector2(relX * scale, 1f - (relY + 1) * scale);
            uvScale = new Vector2(scale, scale);
        }

        public void Clear()
        {
            _demTextures.Clear();
        }

        /// <summary>
        /// Sample elevation in meters at the given mercator [0,1] coordinate by reading
        /// a pixel from whichever DEM tile covers it. Returns NaN when no DEM is loaded
        /// for the location. Matches MapLibre GL JS map.queryTerrainElevation().
        /// </summary>
        public float QueryElevationMeters(MercatorCoordinate merc)
        {
            // Pick the deepest available DEM containing this point. SourceMaxZoom is the
            // best resolution; walk up if not loaded.
            int z = SourceMaxZoom;
            while (z >= 0)
            {
                int n = 1 << z;
                int tx = (int)System.Math.Floor(merc.X * n);
                int ty = (int)System.Math.Floor(merc.Y * n);
                tx = System.Math.Clamp(tx, 0, n - 1);
                ty = System.Math.Clamp(ty, 0, n - 1);

                var id = new CanonicalTileID(z, tx, ty);
                if (_demTextures.TryGetValue(id, out var tex) && tex != null)
                {
                    // Mercator → pixel. Tile DEM textures are Y-up in Unity, Y-down in
                    // mercator, so flip V on read.
                    double localX = merc.X * n - tx;
                    double localY = merc.Y * n - ty;
                    int px = System.Math.Clamp((int)(localX * tex.width), 0, tex.width - 1);
                    int py = System.Math.Clamp((int)((1.0 - localY) * tex.height), 0, tex.height - 1);
                    Color c = tex.GetPixel(px, py);
                    return DecodeElevation(c, IsTerrarium);
                }
                z--;
            }
            return float.NaN;
        }

        private static float DecodeElevation(Color c, bool terrarium)
        {
            // Color components are 0..1 floats; convert back to 0..255 byte values.
            float r = c.r * 255f;
            float g = c.g * 255f;
            float b = c.b * 255f;
            if (terrarium)
                return (r * 256f + g + b / 256f) - 32768f;
            // Mapbox encoding: -10000 + ((R * 256 * 256 + G * 256 + B) * 0.1)
            return -10000f + (r * 256f * 256f + g * 256f + b) * 0.1f;
        }
    }
}
