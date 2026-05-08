using System.Collections.Generic;
using System.Threading.Tasks;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Renders vector tile heatmap layers (point features as Gaussian density blobs).
    /// Per-feature weight is baked into vertex data (UV2.x).
    /// The shader maps density through a color ramp texture, with weight affecting
    /// both blob size (sqrt scaling) and color intensity.
    /// </summary>
    public class HeatmapRenderer : ILayerRenderer
    {
        private readonly Dictionary<CanonicalTileID, List<GameObject>> _activeTileObjects = new();
        private readonly HashSet<CanonicalTileID> _requestedTiles = new();
        private readonly Transform _parent;
        private readonly Material _heatmapMaterial;
        private readonly MaterialPropertyBlock _propertyBlock = new();
        private float _yOffset;

        private const float MinScreenHeight = 480f;
        private const int MaxMeshesPerFrame = 4;

        private float _lastFrustumHeight;

        private readonly Queue<PendingMesh> _meshQueue = new();

        private static readonly int CSSToLocalPropertyId = Shader.PropertyToID("_CSSToLocal");
        private static readonly int RadiusCSSPropertyId = Shader.PropertyToID("_RadiusCSS");
        private static readonly int RadiusScalePropertyId = Shader.PropertyToID("_RadiusScale");
        private static readonly int IntensityPropertyId = Shader.PropertyToID("_Intensity");
        private static readonly int OpacityPropertyId = Shader.PropertyToID("_Opacity");
        private static readonly int ColorRampPropertyId = Shader.PropertyToID("_ColorRamp");

        private HeatmapPaintProperties _paintProps;
        private float _dispatchZoom;
        private float _dispatchBaseRadius;
        private Texture2D _colorRampTexture;

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
            public MeshData MeshData;
            public float RadiusCSS;
            public float Intensity;
            public float Opacity;
            public MercatorCoordinate MapCenter;
            public float Zoom;
        }

        /// <param name="layerOrder">Layer index in style (0-based). Higher = rendered on top.</param>
        public HeatmapRenderer(Transform parent, Shader heatmapShader, int layerOrder = 0)
        {
            _parent = parent;

            int renderQueue = 3000 + layerOrder;

            if (heatmapShader != null)
            {
                _heatmapMaterial = new Material(heatmapShader);
                _heatmapMaterial.renderQueue = renderQueue;
            }

            _yOffset = 0.001f + layerOrder * 0.001f;
        }

        public void SetLayerOrder(int layerOrder)
        {
            if (_heatmapMaterial != null) _heatmapMaterial.renderQueue = 3000 + layerOrder;
            _yOffset = 0.001f + layerOrder * 0.001f;
        }

        /// <summary>
        /// Render a vector tile for a heatmap layer.
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
            DispatchHeatmapMesh(tileId, tileLayer, layerDef, mapCenter, zoom);
        }

        private void DispatchHeatmapMesh(CanonicalTileID tileId, VectorTileLayer tileLayer,
            LayerDefinition layerDef, MercatorCoordinate mapCenter, float zoom)
        {
            var paintProps = StyleParser.ParseHeatmapPaint(layerDef.Paint);
            var ctx = MakeContext(zoom, layerDef.Source, layerDef.SourceLayer);

            var features = FilterPointFeatures(tileLayer, layerDef.Filter, zoom,
                layerDef.Source, layerDef.SourceLayer);
            if (features.Count == 0) return;

            int extent = tileLayer.Extent;
            float radiusCSS = paintProps.ResolveHeatmapRadius(ctx);
            float intensity = paintProps.ResolveHeatmapIntensity(ctx);
            float opacity = paintProps.ResolveHeatmapOpacity(ctx);

            _paintProps = paintProps;
            _dispatchZoom = zoom;
            _dispatchBaseRadius = radiusCSS;

            // Generate color ramp texture if not yet created
            if (_colorRampTexture == null)
            {
                _colorRampTexture = HeatmapColorRamp.Generate(paintProps.HeatmapColor, zoom);
            }

            var weightExpr = paintProps.HeatmapWeight;
            var capturedFeatures = features;
            var capturedExtent = extent;
            var capturedZoom = zoom;
            var capturedSourceId = layerDef.Source;
            var capturedSourceLayer = layerDef.SourceLayer;
            var capturedStore = _featureStateStore;

            BackgroundTask.Run(() =>
            {
                var meshData = VectorTileMeshBuilder.BuildHeatmapMeshData(
                    capturedFeatures, capturedExtent, weightExpr, capturedZoom,
                    capturedSourceId, capturedSourceLayer, capturedStore);
                if (meshData != null)
                {
                    lock (_meshQueue)
                    {
                        _meshQueue.Enqueue(new PendingMesh
                        {
                            TileId = tileId,
                            LayerId = layerDef.Id,
                            MeshData = meshData,
                            RadiusCSS = radiusCSS,
                            Intensity = intensity,
                            Opacity = opacity,
                            MapCenter = mapCenter,
                            Zoom = zoom
                        });
                    }
                }
            });
        }

        private List<VectorTileFeature> FilterPointFeatures(VectorTileLayer layer,
            Expression filter, float zoom, string sourceId, string sourceLayer)
        {
            var result = new List<VectorTileFeature>();
            foreach (var feature in layer.Features)
            {
                if (feature.Type != GeometryType.Point) continue;

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

                if (_activeTileObjects.ContainsKey(pending.TileId))
                {
                    processed++;
                    continue;
                }

                var mesh = pending.MeshData.ToMesh();
                if (mesh == null) { processed++; continue; }

                var go = CreateMeshObject($"heatmap_{pending.LayerId}_{pending.TileId}", mesh);
                PositionTileObject(go, pending.TileId, pending.MapCenter, pending.Zoom);

                float cssToLocal = ComputeCSSToLocal(pending.TileId.Z, pending.Zoom, _lastFrustumHeight);

                var renderer = go.GetComponent<MeshRenderer>();
                _propertyBlock.SetFloat(CSSToLocalPropertyId, cssToLocal);
                _propertyBlock.SetFloat(RadiusCSSPropertyId, pending.RadiusCSS);
                _propertyBlock.SetFloat(RadiusScalePropertyId, 1f);
                _propertyBlock.SetFloat(IntensityPropertyId, pending.Intensity);
                _propertyBlock.SetFloat(OpacityPropertyId, pending.Opacity);
                if (_colorRampTexture != null)
                    _propertyBlock.SetTexture(ColorRampPropertyId, _colorRampTexture);
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

        private GameObject CreateMeshObject(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_parent, false);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _heatmapMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            return go;
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
            go.transform.localScale = new Vector3(tileWorldSize, 1f, tileWorldSize);
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

        public void UpdateAllPositions(MercatorCoordinate mapCenter, float zoom, float frustumHeight = 0f)
        {
            if (frustumHeight > 0f)
                _lastFrustumHeight = frustumHeight;

            float worldScale = CoordinateConversion.GetWorldScale(zoom);

            float radiusScale = 1f;
            if (_paintProps != null && _dispatchBaseRadius > 0f)
            {
                var ctx = new EvaluationContext(zoom);
                float currentRadius = _paintProps.ResolveHeatmapRadius(ctx);
                radiusScale = currentRadius / _dispatchBaseRadius;
            }

            float intensity = 1f;
            float opacity = 1f;
            if (_paintProps != null)
            {
                var ctx = new EvaluationContext(zoom);
                intensity = _paintProps.ResolveHeatmapIntensity(ctx);
                opacity = _paintProps.ResolveHeatmapOpacity(ctx);
            }

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
                    go.transform.localScale = new Vector3(tileWorldSize, 1f, tileWorldSize);

                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.GetPropertyBlock(_propertyBlock);
                        _propertyBlock.SetFloat(CSSToLocalPropertyId, cssToLocal);
                        _propertyBlock.SetFloat(RadiusScalePropertyId, radiusScale);
                        _propertyBlock.SetFloat(IntensityPropertyId, intensity);
                        _propertyBlock.SetFloat(OpacityPropertyId, opacity);
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
            if (_heatmapMaterial != null) Object.Destroy(_heatmapMaterial);
            if (_colorRampTexture != null) Object.Destroy(_colorRampTexture);
        }
    }
}
