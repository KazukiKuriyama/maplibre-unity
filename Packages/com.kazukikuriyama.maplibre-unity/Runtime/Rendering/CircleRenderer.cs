using System.Collections.Generic;
using System.Threading.Tasks;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Renders vector tile circle layers (point features as SDF circles).
    /// Per-feature radius and color are baked into vertex data (UV2.x and vertex color).
    /// A _CSSToLocal uniform converts CSS pixels to tile-local units each frame.
    /// </summary>
    public class CircleRenderer : ILayerRenderer
    {
        private readonly Dictionary<CanonicalTileID, List<GameObject>> _activeTileObjects = new();
        private readonly HashSet<CanonicalTileID> _requestedTiles = new();
        private readonly Transform _parent;
        private readonly Material _circleMaterial;
        private readonly MaterialPropertyBlock _propertyBlock = new();
        private float _yOffset;

        private const float MinScreenHeight = 480f;
        private const int MaxMeshesPerFrame = 4;

        /// <summary>Last frustumHeight received from UpdateAllPositions, used by ProcessMeshQueue.</summary>
        private float _lastFrustumHeight;

        private readonly Queue<PendingMesh> _meshQueue = new();

        private static readonly int CSSToLocalPropertyId = Shader.PropertyToID("_CSSToLocal");
        private static readonly int RadiusScalePropertyId = Shader.PropertyToID("_RadiusScale");
        private static readonly int BlurPropertyId = Shader.PropertyToID("_Blur");
        private static readonly int StrokeWidthPropertyId = Shader.PropertyToID("_StrokeWidth");
        private static readonly int StrokeColorPropertyId = Shader.PropertyToID("_StrokeColor");
        private static readonly int StrokeOpacityPropertyId = Shader.PropertyToID("_StrokeOpacity");
        private static readonly int PitchAlignPropertyId = Shader.PropertyToID("_PitchAlign");
        private static readonly int PitchScalePropertyId = Shader.PropertyToID("_PitchScale");
        private static readonly int PixelToClipYPropertyId = Shader.PropertyToID("_PixelToClipY");

        // Cached paint properties for stroke (uniform, not per-feature for now)
        private float _strokeWidthCSS;
        private Color _strokeColor;
        private float _strokeOpacity;
        private float _blur;
        // circle-pitch-alignment / circle-pitch-scale (layer uniforms).
        // 0 = "map", 1 = "viewport". Defaults match MapLibre Style Spec.
        private float _pitchAlign = 1f;
        private float _pitchScale = 0f;

        // For zoom-dependent radius re-evaluation
        private CirclePaintProperties _paintProps;
        private float _dispatchZoom;
        private float _dispatchBaseRadius;

        // Live feature-state lookup. Wired by MapRenderer.SetFeatureStateStore.
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
            public float Blur;
            public float StrokeWidthCSS;
            public Color StrokeColor;
            public float StrokeOpacity;
            public MercatorCoordinate MapCenter;
            public float Zoom;
        }

        /// <param name="layerOrder">Layer index in style (0-based). Higher = rendered on top.</param>
        public CircleRenderer(Transform parent, Shader circleShader, int layerOrder = 0)
        {
            _parent = parent;

            int renderQueue = 3000 + layerOrder;

            if (circleShader != null)
            {
                _circleMaterial = new Material(circleShader);
                _circleMaterial.renderQueue = renderQueue;
            }

            _yOffset = 0.001f + layerOrder * 0.001f;
        }

        public void SetLayerOrder(int layerOrder)
        {
            if (_circleMaterial != null) _circleMaterial.renderQueue = 3000 + layerOrder;
            _yOffset = 0.001f + layerOrder * 0.001f;
        }

        /// <summary>
        /// Render a vector tile for a circle layer.
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
            DispatchCircleMesh(tileId, tileLayer, layerDef, mapCenter, zoom);
        }

        private void DispatchCircleMesh(CanonicalTileID tileId, VectorTileLayer tileLayer,
            LayerDefinition layerDef, MercatorCoordinate mapCenter, float zoom)
        {
            var paintProps = StyleParser.ParseCirclePaint(layerDef.Paint);
            var ctx = MakeContext(zoom, layerDef.Source, layerDef.SourceLayer);

            var features = FilterPointFeatures(tileLayer, layerDef.Filter, zoom,
                layerDef.Source, layerDef.SourceLayer);
            if (features.Count == 0) return;

            int extent = tileLayer.Extent;
            float blur = paintProps.ResolveCircleBlur(ctx);
            float strokeWidth = paintProps.ResolveCircleStrokeWidth(ctx);
            Color strokeColor = paintProps.ResolveCircleStrokeColor(ctx);
            float strokeOpacity = paintProps.ResolveCircleStrokeOpacity(ctx);

            _strokeWidthCSS = strokeWidth;
            _strokeColor = strokeColor;
            _strokeOpacity = strokeOpacity;
            _blur = blur;
            _pitchAlign = paintProps.CirclePitchAlignment == "map" ? 0f : 1f;
            _pitchScale = paintProps.CirclePitchScale == "viewport" ? 1f : 0f;

            // Store for zoom-dependent re-evaluation
            _paintProps = paintProps;
            _dispatchZoom = zoom;
            _dispatchBaseRadius = paintProps.ResolveCircleRadius(ctx);

            // Capture expression references for per-feature evaluation on background thread
            var radiusExpr = paintProps.CircleRadius;
            var colorExpr = paintProps.CircleColor;
            var opacityExpr = paintProps.CircleOpacity;
            var capturedFeatures = features;
            var capturedExtent = extent;
            var capturedZoom = zoom;
            var capturedSourceId = layerDef.Source;
            var capturedSourceLayer = layerDef.SourceLayer;
            var capturedStore = _featureStateStore;

            BackgroundTask.Run(() =>
            {
                var meshData = VectorTileMeshBuilder.BuildCircleMeshData(
                    capturedFeatures, capturedExtent,
                    radiusExpr, colorExpr, opacityExpr, capturedZoom,
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
                            Blur = blur,
                            StrokeWidthCSS = strokeWidth,
                            StrokeColor = strokeColor,
                            StrokeOpacity = strokeOpacity,
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

                var go = CreateMeshObject($"circle_{pending.LayerId}_{pending.TileId}", mesh);
                PositionTileObject(go, pending.TileId, pending.MapCenter, pending.Zoom);

                // Set uniform properties via MaterialPropertyBlock
                float cssToLocal = ComputeCSSToLocal(pending.TileId.Z, pending.Zoom, _lastFrustumHeight);

                var renderer = go.GetComponent<MeshRenderer>();
                _propertyBlock.SetFloat(CSSToLocalPropertyId, cssToLocal);
                _propertyBlock.SetFloat(RadiusScalePropertyId, 1f);
                _propertyBlock.SetFloat(BlurPropertyId, pending.Blur);
                _propertyBlock.SetFloat(StrokeWidthPropertyId, pending.StrokeWidthCSS);
                _propertyBlock.SetColor(StrokeColorPropertyId, pending.StrokeColor);
                _propertyBlock.SetFloat(StrokeOpacityPropertyId, pending.StrokeOpacity);
                _propertyBlock.SetFloat(PitchAlignPropertyId, _pitchAlign);
                _propertyBlock.SetFloat(PitchScalePropertyId, _pitchScale);
                _propertyBlock.SetFloat(PixelToClipYPropertyId,
                    2f / Mathf.Max(Screen.height, MinScreenHeight));
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

        /// <summary>
        /// Compute conversion factor: 1 CSS pixel = how many tile-local units.
        /// Uses the actual camera frustum height for accurate screen-pixel sizing.
        /// </summary>
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
            meshRenderer.sharedMaterial = _circleMaterial;
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

            // Compute zoom-dependent radius scale ratio.
            // Vertex data was baked at _dispatchZoom. If the radius expression is
            // zoom-dependent, re-evaluate at the current zoom and compute a ratio.
            float radiusScale = 1f;
            if (_paintProps != null && _dispatchBaseRadius > 0f)
            {
                var ctx = new EvaluationContext(zoom);
                float currentRadius = _paintProps.ResolveCircleRadius(ctx);
                radiusScale = currentRadius / _dispatchBaseRadius;
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

                // Recompute CSS-to-local conversion for current zoom using actual camera frustum
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
                        _propertyBlock.SetFloat(PitchAlignPropertyId, _pitchAlign);
                        _propertyBlock.SetFloat(PitchScalePropertyId, _pitchScale);
                        _propertyBlock.SetFloat(PixelToClipYPropertyId,
                            2f / Mathf.Max(Screen.height, MinScreenHeight));
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
            if (_circleMaterial != null) Object.Destroy(_circleMaterial);
        }
    }
}
