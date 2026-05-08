using System.Collections.Generic;
using System.Threading.Tasks;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Renders vector tile fill-extrusion layers (3D building polygons).
    /// Per-feature height, base, and color are baked into vertex data.
    /// Mesh computation runs on background threads; Unity objects are created on the main thread.
    /// </summary>
    public class FillExtrusionRenderer : ILayerRenderer
    {
        private readonly Dictionary<CanonicalTileID, List<GameObject>> _activeTileObjects = new();
        private readonly HashSet<CanonicalTileID> _requestedTiles = new();
        private readonly Transform _parent;
        private readonly Material _material;
        private readonly Material _patternMaterial;
        private readonly SpriteAtlas _spriteAtlas;
        private readonly MaterialPropertyBlock _propertyBlock = new();
        private float _yOffset;

        private const int MaxMeshesPerFrame = 4;
        private const float MinScreenHeight = 480f;

        /// <summary>Last frustumHeight received from UpdateAllPositions.</summary>
        private float _lastFrustumHeight;

        private readonly Queue<PendingMesh> _meshQueue = new();

        private static readonly int OpacityPropertyId = Shader.PropertyToID("_Opacity");
        private static readonly int CSSToLocalPropertyId = Shader.PropertyToID("_CSSToLocal");
        private static readonly int PatternTexPropertyId = Shader.PropertyToID("_PatternTex");
        private static readonly int PatternUVRectPropertyId = Shader.PropertyToID("_PatternUVRect");
        private static readonly int PatternPixelSizePropertyId = Shader.PropertyToID("_PatternPixelSize");
        private static readonly int StyleLightDirPropertyId = Shader.PropertyToID("_StyleLightDir");
        private static readonly int StyleLightColorPropertyId = Shader.PropertyToID("_StyleLightColor");
        private static readonly int StyleLightIntensityPropertyId = Shader.PropertyToID("_StyleLightIntensity");

        // Cached layer-level light. Re-applied per-frame so anchor=viewport rotates with bearing.
        private LightDefinition _light;

        private IFeatureStateStore _featureStateStore;
        public void SetFeatureStateStore(IFeatureStateStore store) => _featureStateStore = store;

        private EvaluationContext MakeContext(float zoom, string sourceId, string sourceLayer)
            => new EvaluationContext(zoom)
            {
                SourceId = sourceId,
                SourceLayer = sourceLayer,
                FeatureStateStore = _featureStateStore,
            };

        private EvaluationContext MakeContext(float zoom, string sourceId, string sourceLayer,
            VectorTileFeature feature)
            => new EvaluationContext(zoom, feature)
            {
                SourceId = sourceId,
                SourceLayer = sourceLayer,
                FeatureStateStore = _featureStateStore,
            };

        private struct PendingMesh
        {
            public CanonicalTileID TileId;
            public string LayerId;
            // Distinguishes per-feature pattern groups within one (tile, layer):
            // "" = no pattern, otherwise the resolved pattern name.
            public string GroupKey;
            public MeshData MeshData;
            public float Opacity;
            public bool IsPattern;
            public Material Material;
            public Texture2D PatternTexture;
            public Vector4 PatternUVRect;
            public Vector4 PatternPixelSize;
            public MercatorCoordinate MapCenter;
            public float Zoom;
        }

        /// <summary>
        /// Bucket features by the resolved fill-extrusion-pattern name. The
        /// "" bucket falls back to the plain extrusion material.
        /// </summary>
        private Dictionary<string, List<VectorTileFeature>> GroupByPattern(
            List<VectorTileFeature> features, Expression patternExpr,
            string sourceId, string sourceLayer, float zoom)
        {
            var groups = new Dictionary<string, List<VectorTileFeature>>();
            for (int i = 0; i < features.Count; i++)
            {
                var f = features[i];
                string key = "";
                if (patternExpr != null)
                {
                    var ctx = MakeContext(zoom, sourceId, sourceLayer, f);
                    key = patternExpr.EvaluateString(ctx, "") ?? "";
                }
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<VectorTileFeature>();
                    groups[key] = list;
                }
                list.Add(f);
            }
            return groups;
        }

        /// <param name="layerOrder">Layer index in style (0-based). Higher = rendered on top.</param>
        public FillExtrusionRenderer(Transform parent, Shader shader, int layerOrder = 0,
            Shader patternShader = null, SpriteAtlas spriteAtlas = null)
        {
            _parent = parent;
            _spriteAtlas = spriteAtlas;

            // Geometry+1 (2001+) so buildings render after the flat map but as opaque
            int renderQueue = 2001 + layerOrder;

            if (shader != null)
            {
                _material = new Material(shader);
                _material.renderQueue = renderQueue;
            }

            if (patternShader != null)
            {
                _patternMaterial = new Material(patternShader);
                _patternMaterial.renderQueue = renderQueue;
            }

            _yOffset = 0.001f + layerOrder * 0.001f;
        }

        public void SetLayerOrder(int layerOrder)
        {
            if (_material != null) _material.renderQueue = 2001 + layerOrder;
            if (_patternMaterial != null) _patternMaterial.renderQueue = 2001 + layerOrder;
            _yOffset = 0.001f + layerOrder * 0.001f;
        }

        /// <summary>
        /// Apply the style's "light" definition to this renderer's materials. Stored so
        /// per-frame UpdateAllPositions can refresh anchor=viewport directions when the
        /// camera bearing changes.
        /// </summary>
        public void SetLight(LightDefinition light)
        {
            _light = light;
            ApplyLightUniforms(0f);
        }

        private void ApplyLightUniforms(float bearingDeg)
        {
            if (_material == null && _patternMaterial == null) return;
            Vector3 dir = ComputeWorldLightDirection(_light, bearingDeg);
            Color col = _light != null ? _light.Color : Color.white;
            float intensity = _light != null ? Mathf.Clamp01(_light.Intensity) : 0.5f;
            if (_material != null)
            {
                _material.SetVector(StyleLightDirPropertyId, new Vector4(dir.x, dir.y, dir.z, 0f));
                _material.SetColor(StyleLightColorPropertyId, col);
                _material.SetFloat(StyleLightIntensityPropertyId, intensity);
            }
            if (_patternMaterial != null)
            {
                _patternMaterial.SetVector(StyleLightDirPropertyId, new Vector4(dir.x, dir.y, dir.z, 0f));
                _patternMaterial.SetColor(StyleLightColorPropertyId, col);
                _patternMaterial.SetFloat(StyleLightIntensityPropertyId, intensity);
            }
        }

        /// <summary>
        /// Convert a MapLibre light position (radial, azimuth, polar in degrees) to a
        /// Y-up world direction pointing TOWARD the source. anchor=viewport rotates the
        /// azimuth by the camera bearing so the highlight follows the screen orientation.
        /// </summary>
        public static Vector3 ComputeWorldLightDirection(LightDefinition light, float bearingDeg)
        {
            // Spec defaults: position = [1.15, 210, 30], anchor = "viewport".
            float azimuth = light != null ? light.Position[1] : 210f;
            float polar = light != null ? light.Position[2] : 30f;
            string anchor = light != null ? light.Anchor : "viewport";
            if (anchor == "viewport") azimuth -= bearingDeg;
            float polarRad = polar * Mathf.Deg2Rad;
            float azRad = azimuth * Mathf.Deg2Rad;
            float sinP = Mathf.Sin(polarRad);
            return new Vector3(sinP * Mathf.Sin(azRad), Mathf.Cos(polarRad), sinP * Mathf.Cos(azRad)).normalized;
        }

        /// <summary>
        /// Render a vector tile for a fill-extrusion layer.
        /// </summary>
        public void ShowTile(CanonicalTileID tileId, VectorTileData tileData,
            LayerDefinition layerDef, MercatorCoordinate mapCenter, float zoom)
        {
            if (_requestedTiles.Contains(tileId))
                return;

            var tileLayer = tileData.GetLayer(layerDef.SourceLayer ?? layerDef.Id);
            if (tileLayer == null || tileLayer.Features.Count == 0)
                return;

            _requestedTiles.Add(tileId);
            DispatchExtrusionMesh(tileId, tileLayer, layerDef, mapCenter, zoom);
        }

        private void DispatchExtrusionMesh(CanonicalTileID tileId, VectorTileLayer tileLayer,
            LayerDefinition layerDef, MercatorCoordinate mapCenter, float zoom)
        {
            var paintProps = StyleParser.ParseFillExtrusionPaint(layerDef.Paint);
            var ctx = MakeContext(zoom, layerDef.Source, layerDef.SourceLayer);

            var features = FilterPolygonFeatures(tileLayer, layerDef.Filter, zoom,
                layerDef.Source, layerDef.SourceLayer);
            if (features.Count == 0) return;

            int extent = tileLayer.Extent;
            float opacity = paintProps.ResolveFillExtrusionOpacity(ctx);

            float metersToLocal = ComputeMetersToLocal(tileId.Z, tileId.Y);

            var heightExpr = paintProps.FillExtrusionHeight;
            var baseExpr = paintProps.FillExtrusionBase;
            var colorExpr = paintProps.FillExtrusionColor;
            var opacityExpr = paintProps.FillExtrusionOpacity;
            var capturedExtent = extent;
            var capturedZoom = zoom;
            var capturedSourceId = layerDef.Source;
            var capturedSourceLayer = layerDef.SourceLayer;
            var capturedStore = _featureStateStore;
            var capturedMetersToLocal = metersToLocal;
            var capturedOpacity = opacity;

            // fill-extrusion-pattern is per-feature: the same expression form
            // as fill-pattern. Group features by their resolved pattern and
            // dispatch one mesh per group.
            var groups = GroupByPattern(features, paintProps.FillExtrusionPattern,
                layerDef.Source, layerDef.SourceLayer, zoom);

            foreach (var kv in groups)
            {
                string groupKey = kv.Key;
                var groupFeatures = kv.Value;

                Material chosenMat = _material;
                Vector4 patternRect = Vector4.zero;
                Vector4 patternPixel = Vector4.zero;
                Texture2D patternTex = null;
                bool hasPattern = false;
                if (!string.IsNullOrEmpty(groupKey)
                    && _patternMaterial != null && _spriteAtlas != null
                    && _spriteAtlas.TryGetEntry(groupKey, out var entry,
                        out var uvRect, out var tex))
                {
                    hasPattern = true;
                    chosenMat = _patternMaterial;
                    patternRect = new Vector4(uvRect.x, uvRect.y, uvRect.width, uvRect.height);
                    patternPixel = new Vector4(
                        entry.Width / entry.PixelRatio,
                        entry.Height / entry.PixelRatio, 0f, 0f);
                    patternTex = tex;
                }

                var capturedGroup = groupFeatures;
                var capturedKey = groupKey;
                var capturedMat = chosenMat;
                var capturedHasPattern = hasPattern;
                var capturedTex = patternTex;
                var capturedRect = patternRect;
                var capturedPixel = patternPixel;

                BackgroundTask.Run(() =>
                {
                    var meshData = VectorTileMeshBuilder.BuildFillExtrusionMeshData(
                        capturedGroup, capturedExtent,
                        heightExpr, baseExpr, colorExpr, opacityExpr,
                        capturedZoom, capturedMetersToLocal,
                        capturedSourceId, capturedSourceLayer, capturedStore);
                    if (meshData == null) return;
                    lock (_meshQueue)
                    {
                        _meshQueue.Enqueue(new PendingMesh
                        {
                            TileId = tileId,
                            LayerId = layerDef.Id,
                            GroupKey = capturedKey,
                            MeshData = meshData,
                            Opacity = capturedOpacity,
                            IsPattern = capturedHasPattern,
                            Material = capturedMat,
                            PatternTexture = capturedTex,
                            PatternUVRect = capturedRect,
                            PatternPixelSize = capturedPixel,
                            MapCenter = mapCenter,
                            Zoom = zoom
                        });
                    }
                });
            }
        }

        /// <summary>
        /// Compute conversion factor: 1 meter = how many tile-local units.
        /// Tile local space is [-0.5, 0.5], meaning 1.0 = full tile width.
        /// Tile width in meters = Earth circumference * cos(lat) / 2^z.
        /// </summary>
        private static float ComputeMetersToLocal(int tileZ, int tileY)
        {
            const double earthCircumference = 40075016.686;
            int n = 1 << tileZ;

            // Latitude at tile center
            double latRad = System.Math.Atan(System.Math.Sinh(System.Math.PI * (1.0 - 2.0 * (tileY + 0.5) / n)));
            double tileSizeMeters = earthCircumference * System.Math.Cos(latRad) / n;

            return (float)(1.0 / tileSizeMeters);
        }

        private List<VectorTileFeature> FilterPolygonFeatures(VectorTileLayer layer,
            Expression filter, float zoom, string sourceId, string sourceLayer)
        {
            var result = new List<VectorTileFeature>();
            foreach (var feature in layer.Features)
            {
                if (feature.Type != GeometryType.Polygon) continue;

                if (filter != null)
                {
                    var ctx = MakeContext(zoom, sourceId, sourceLayer, feature);
                    if (!filter.EvaluateBool(ctx)) continue;
                }

                result.Add(feature);
            }
            return result;
        }

        /// <summary>
        /// Must be called on the main thread each frame.
        /// </summary>
        public void ProcessMeshQueue()
        {
            int processed = 0;
            while (processed < MaxMeshesPerFrame)
            {
                PendingMesh pending;
                lock (_meshQueue)
                {
                    if (_meshQueue.Count == 0) break;
                    pending = _meshQueue.Dequeue();
                }

                if (!_requestedTiles.Contains(pending.TileId))
                {
                    processed++;
                    continue;
                }

                // (tile, layer, group) → at most one GameObject. Per-feature
                // patterns can produce multiple groups for the same tile, so
                // identify duplicates by name rather than just tile id.
                string targetName = string.IsNullOrEmpty(pending.GroupKey)
                    ? $"fillext_{pending.LayerId}_{pending.TileId}"
                    : $"fillext_{pending.LayerId}_{pending.GroupKey}_{pending.TileId}";
                if (_activeTileObjects.TryGetValue(pending.TileId, out var existingGroup))
                {
                    bool duplicate = false;
                    foreach (var existingGo in existingGroup)
                    {
                        if (existingGo != null && existingGo.name == targetName)
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (duplicate) { processed++; continue; }
                }

                var mesh = pending.MeshData.ToMesh();
                if (mesh == null) { processed++; continue; }

                var go = CreateMeshObject(targetName, mesh,
                    pending.Material ?? _material);
                PositionTileObject(go, pending.TileId, pending.MapCenter, pending.Zoom);

                var renderer = go.GetComponent<MeshRenderer>();
                _propertyBlock.SetFloat(OpacityPropertyId, pending.Opacity);
                if (pending.IsPattern && pending.PatternTexture != null)
                {
                    float cssToLocal = ComputeCSSToLocal(pending.TileId.Z, pending.Zoom, _lastFrustumHeight);
                    _propertyBlock.SetFloat(CSSToLocalPropertyId, cssToLocal);
                    _propertyBlock.SetTexture(PatternTexPropertyId, pending.PatternTexture);
                    _propertyBlock.SetVector(PatternUVRectPropertyId, pending.PatternUVRect);
                    _propertyBlock.SetVector(PatternPixelSizePropertyId, pending.PatternPixelSize);
                }
                renderer.SetPropertyBlock(_propertyBlock);

                if (!_activeTileObjects.TryGetValue(pending.TileId, out var objects))
                {
                    objects = new List<GameObject>(1);
                    _activeTileObjects[pending.TileId] = objects;
                }
                objects.Add(go);

                processed++;
            }
        }

        private GameObject CreateMeshObject(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_parent, false);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;

            return go;
        }

        private static float ComputeCSSToLocal(int tileZ, float zoom, float frustumHeight)
        {
            if (frustumHeight <= 0f) return 0f;
            float screenHeight = Mathf.Max(Screen.height, MinScreenHeight);
            float onePixelWorld = frustumHeight / screenHeight;
            int n = 1 << tileZ;
            double tileSize = 1.0 / n;
            float tileWorldSize = (float)(tileSize * CoordinateConversion.GetWorldScale(zoom));
            return onePixelWorld / tileWorldSize;
        }

        private void PositionTileObject(GameObject go, CanonicalTileID tileId,
            MercatorCoordinate mapCenter, float zoom)
        {
            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            int n = 1 << tileId.Z;

            double tileSize = 1.0 / n;
            double tileCenterX = (tileId.X + 0.5) * tileSize;
            double tileCenterY = (tileId.Y + 0.5) * tileSize;

            var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
            Vector3 worldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);
            worldPos.y = _yOffset;

            go.transform.localPosition = worldPos;

            float tileWorldSize = (float)(tileSize * worldScale);
            go.transform.localScale = new Vector3(tileWorldSize, tileWorldSize, tileWorldSize);
        }

        public void HideTile(CanonicalTileID tileId)
        {
            _requestedTiles.Remove(tileId);

            if (_activeTileObjects.TryGetValue(tileId, out var objects))
            {
                foreach (var go in objects)
                {
                    if (go != null)
                    {
                        var meshFilter = go.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                            Object.Destroy(meshFilter.sharedMesh);
                        Object.Destroy(go);
                    }
                }
                _activeTileObjects.Remove(tileId);
            }
        }

        public void UpdateAllPositions(MercatorCoordinate mapCenter, float zoom, float frustumHeight = 0f,
            float bearingDeg = 0f)
        {
            if (frustumHeight > 0f)
                _lastFrustumHeight = frustumHeight;

            // Refresh light direction so anchor=viewport tracks the camera bearing.
            // anchor=map is bearing-independent so this is a no-op there.
            if (_light != null && _light.Anchor == "viewport")
                ApplyLightUniforms(bearingDeg);

            float worldScale = CoordinateConversion.GetWorldScale(zoom);

            foreach (var kvp in _activeTileObjects)
            {
                var tileId = kvp.Key;
                int n = 1 << tileId.Z;
                double tileSize = 1.0 / n;
                double tileCenterX = (tileId.X + 0.5) * tileSize;
                double tileCenterY = (tileId.Y + 0.5) * tileSize;

                var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
                Vector3 worldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);

                float tileWorldSize = (float)(tileSize * worldScale);

                float cssToLocal = ComputeCSSToLocal(tileId.Z, zoom, _lastFrustumHeight);

                foreach (var go in kvp.Value)
                {
                    if (go == null) continue;
                    worldPos.y = go.transform.localPosition.y;
                    go.transform.localPosition = worldPos;
                    go.transform.localScale = new Vector3(tileWorldSize, tileWorldSize, tileWorldSize);

                    // Keep _CSSToLocal in sync for pattern walls (so the pattern stays
                    // CSS-pixel-sized as the camera zooms).
                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer != null && _patternMaterial != null
                        && renderer.sharedMaterial == _patternMaterial)
                    {
                        renderer.GetPropertyBlock(_propertyBlock);
                        _propertyBlock.SetFloat(CSSToLocalPropertyId, cssToLocal);
                        renderer.SetPropertyBlock(_propertyBlock);
                    }
                }
            }
        }

        public bool HasTile(CanonicalTileID tileId) => _activeTileObjects.ContainsKey(tileId);

        public void Clear()
        {
            lock (_meshQueue) { _meshQueue.Clear(); }
            _requestedTiles.Clear();
            var keys = new List<CanonicalTileID>(_activeTileObjects.Keys);
            foreach (var key in keys)
                HideTile(key);
        }

        public void Dispose()
        {
            Clear();
            if (_material != null) Object.Destroy(_material);
            if (_patternMaterial != null) Object.Destroy(_patternMaterial);
        }
    }
}
