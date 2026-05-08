using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Renders raster tile layers. Supports per-tile fade-in matching MapLibre GL JS'
    /// raster-fade-duration paint property: when a tile first becomes visible its
    /// alpha animates from 0 to the layer opacity over <see cref="FadeDurationSeconds"/>.
    /// </summary>
    public class RasterTileRenderer : ILayerRenderer
    {
        private readonly Pool.TileObjectPool _tilePool;
        private readonly Dictionary<CanonicalTileID, ActiveTile> _activeTileObjects = new();
        private readonly MaterialPropertyBlock _propertyBlock = new();
        private readonly float _yOffset;
        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexSTPropertyId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int OpacityPropertyId = Shader.PropertyToID("_Opacity");
        private static readonly Vector4 IdentityST = new(1f, 1f, 0f, 0f);

        /// <summary>
        /// Fade-in duration in seconds. 0 disables the fade.
        /// Defaults to 0.3s (matches MapLibre GL JS raster-fade-duration default of 300 ms).
        /// </summary>
        public float FadeDurationSeconds { get; set; } = 0.3f;

        private struct ActiveTile
        {
            public GameObject Go;
            public float FadeStartTime;
            public float TargetOpacity;
            /// <summary>True for placeholder tiles drawn from a parent texture.</summary>
            public bool IsParentFallback;
        }

        /// <param name="layerOrder">
        /// Layer index in style (0-based). Drives the y offset that prevents
        /// z-fighting when multiple raster layers are stacked (e.g. a basemap
        /// + a procedural overlay added via AddSource(ICustomRasterSource)).
        /// </param>
        public RasterTileRenderer(Pool.TileObjectPool tilePool, int layerOrder = 0)
        {
            _tilePool = tilePool;
            _yOffset = layerOrder * 0.001f;
        }

        public void ShowTile(CanonicalTileID tileId, Texture2D texture,
            MercatorCoordinate mapCenter, float zoom, float opacity = 1f)
        {
            // If a parent fallback was already drawn for this slot, replace it
            // with the real child texture (no fade so the swap is seamless).
            if (_activeTileObjects.TryGetValue(tileId, out var existing) && existing.IsParentFallback)
            {
                ApplyTexture(existing.Go, texture, opacity, IdentityST);
                _activeTileObjects[tileId] = new ActiveTile
                {
                    Go = existing.Go,
                    FadeStartTime = Time.unscaledTime - FadeDurationSeconds, // already faded in
                    TargetOpacity = opacity,
                    IsParentFallback = false
                };
                return;
            }

            if (_activeTileObjects.ContainsKey(tileId))
                return;

            var go = _tilePool.Get();
            _activeTileObjects[tileId] = new ActiveTile
            {
                Go = go,
                FadeStartTime = Time.unscaledTime,
                TargetOpacity = opacity,
                IsParentFallback = false
            };

            PositionTile(go, tileId, mapCenter, zoom);

            float startAlpha = FadeDurationSeconds > 0f ? 0f : opacity;
            ApplyTexture(go, texture, startAlpha, IdentityST);
        }

        /// <summary>
        /// Display the requested child tile slot using a parent tile's texture, sampling
        /// only the sub-region that corresponds to the child. Replaced atomically when
        /// <see cref="ShowTile"/> is called for the same child id.
        /// </summary>
        public void ShowParentFallback(CanonicalTileID childTileId, CanonicalTileID parentTileId,
            Texture2D parentTexture, MercatorCoordinate mapCenter, float zoom, float opacity = 1f)
        {
            if (_activeTileObjects.ContainsKey(childTileId)) return;
            if (parentTexture == null || childTileId.Z <= parentTileId.Z) return;

            var go = _tilePool.Get();
            PositionTile(go, childTileId, mapCenter, zoom);

            // Sub-region within the parent texture occupied by this child.
            int dz = childTileId.Z - parentTileId.Z;
            int scale = 1 << dz;
            int offsetX = childTileId.X - (parentTileId.X << dz);
            int offsetY = childTileId.Y - (parentTileId.Y << dz);
            float invScale = 1f / scale;
            // Quad UV runs (0,0)=south to (0,1)=north; tile Y grows south, so flip V.
            var st = new Vector4(invScale, invScale, offsetX * invScale,
                1f - (offsetY + 1) * invScale);

            // Parent fallback shows immediately at full opacity (no fade -- it's a placeholder).
            ApplyTexture(go, parentTexture, opacity, st);

            _activeTileObjects[childTileId] = new ActiveTile
            {
                Go = go,
                FadeStartTime = Time.unscaledTime - FadeDurationSeconds,
                TargetOpacity = opacity,
                IsParentFallback = true
            };
        }

        public void HideTile(CanonicalTileID tileId)
        {
            if (_activeTileObjects.TryGetValue(tileId, out var tile))
            {
                _tilePool.Release(tile.Go);
                _activeTileObjects.Remove(tileId);
            }
        }

        public void UpdateAllPositions(MercatorCoordinate mapCenter, float zoom)
        {
            float now = Time.unscaledTime;
            float fadeDuration = Mathf.Max(0f, FadeDurationSeconds);

            foreach (var kvp in _activeTileObjects)
            {
                var tile = kvp.Value;
                if (tile.Go == null) continue;
                PositionTile(tile.Go, kvp.Key, mapCenter, zoom);

                if (fadeDuration <= 0f) continue;

                // ShowTile primes the property block with opacity=0 and relies on
                // this loop to ramp it up to TargetOpacity over fadeDuration. We
                // must keep writing opacity until it reaches TargetOpacity -- if
                // we skipped the update once t already crossed 1 (e.g. when the
                // first frame after ShowTile arrives more than fadeDuration
                // later, common during startup stutters), the tile would stay
                // invisible at opacity=0 indefinitely.
                float t = Mathf.Clamp01((now - tile.FadeStartTime) / fadeDuration);
                float targetOpacity = tile.TargetOpacity * t;
                var renderer = tile.Go.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_propertyBlock);
                if (Mathf.Abs(_propertyBlock.GetFloat(OpacityPropertyId) - targetOpacity) < 1e-4f)
                    continue;
                _propertyBlock.SetFloat(OpacityPropertyId, targetOpacity);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        public bool HasTile(CanonicalTileID tileId) => _activeTileObjects.ContainsKey(tileId);

        private void ApplyTexture(GameObject go, Texture2D texture, float opacity, Vector4 st)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetTexture(MainTexPropertyId, texture);
            _propertyBlock.SetFloat(OpacityPropertyId, opacity);
            _propertyBlock.SetVector(MainTexSTPropertyId, st);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        private void PositionTile(GameObject go, CanonicalTileID tileId,
            MercatorCoordinate mapCenter, float zoom)
        {
            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            int n = 1 << tileId.Z;

            // Tile center in Mercator [0,1] space
            double tileSize = 1.0 / n;
            double tileCenterX = (tileId.X + 0.5) * tileSize;
            double tileCenterY = (tileId.Y + 0.5) * tileSize;

            var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
            Vector3 worldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);
            worldPos.y = _yOffset;

            go.transform.localPosition = worldPos;

            // Scale: tile covers (tileSize * worldScale) in each axis
            float tileWorldSize = (float)(tileSize * worldScale);
            go.transform.localScale = new Vector3(tileWorldSize, 1f, tileWorldSize);
        }

        public void Clear()
        {
            foreach (var kvp in _activeTileObjects)
                _tilePool.Release(kvp.Value.Go);
            _activeTileObjects.Clear();
        }
    }
}
