using System.Collections.Generic;
using System.Threading.Tasks;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Renders vector tile fill and line layers.
    /// Per-feature paint properties (color, opacity, line-width, line-offset) are evaluated
    /// per-feature on a background thread and baked into vertex attributes so data-driven
    /// styling such as ["match", ["get", "class"], ...] works.
    /// Heavy mesh computation runs on background threads; Unity Mesh creation and
    /// GameObject setup run on the main thread via a queue.
    /// </summary>
    public class VectorTileRenderer : ILayerRenderer
    {
        private readonly Dictionary<CanonicalTileID, List<GameObject>> _activeTileObjects = new();
        // Snapshot of objects awaiting replacement during an in-place rebuild
        // (feature-state invalidation). Old GOs stay visible until matching new
        // meshes arrive from the re-dispatch -- prevents the flicker that a
        // hard Clear() would cause on every SetFeatureState call.
        private readonly Dictionary<CanonicalTileID, List<GameObject>> _staleTileObjects = new();
        private int _staleSweepDeadlineFrame = -1;
        private readonly HashSet<CanonicalTileID> _requestedTiles = new();
        private readonly Transform _parent;
        private readonly Material _fillMaterial;
        private readonly Material _lineMaterial;
        private readonly Material _fillPatternMaterial;
        private readonly Material _linePatternMaterial;
        private readonly Shader _lineGradientShader;
        private readonly System.Func<string, bool> _lineMetricsResolver;
        private readonly SpriteAtlas _spriteAtlas;
        // One MapLibre/LineGradient material per layer, materialised the first
        // time the layer dispatches a gradient mesh. The 256x1 ramp texture is
        // baked from the layer's line-gradient expression and rebuilt only when
        // SetPaintProperty mutates it (cached as a field on the layer's entry).
        private readonly Dictionary<string, GradientCacheEntry> _gradientCache = new();
        private struct GradientCacheEntry
        {
            public Material Material;
            public Texture2D Ramp;
            public int ExpressionVersion;
        }
        private readonly MaterialPropertyBlock _propertyBlock = new();
        private float _yOffset;

        private const float MinScreenHeight = 480f;

        /// <summary>Last frustumHeight received from UpdateAllPositions, used by ProcessMeshQueue.</summary>
        private float _lastFrustumHeight;
        private const int MaxMeshesPerFrame = 4;

        private readonly Queue<PendingMesh> _meshQueue = new();

        private static readonly int LayerOpacityPropertyId = Shader.PropertyToID("_LayerOpacity");
        private static readonly int CSSToLocalPropertyId = Shader.PropertyToID("_CSSToLocal");
        private static readonly int WidthScalePropertyId = Shader.PropertyToID("_WidthScale");
        private static readonly int ClipExtendPropertyId = Shader.PropertyToID("_ClipExtend");
        private static readonly int DashPatternPropertyId = Shader.PropertyToID("_DashPattern");
        private static readonly int DashTotalPropertyId = Shader.PropertyToID("_DashTotal");
        private static readonly int PatternTexPropertyId = Shader.PropertyToID("_PatternTex");
        private static readonly int PatternUVRectPropertyId = Shader.PropertyToID("_PatternUVRect");
        private static readonly int PatternPixelSizePropertyId = Shader.PropertyToID("_PatternPixelSize");
        private static readonly int GradientTexPropertyId = Shader.PropertyToID("_GradientTex");

        /// <summary>
        /// Buffer size in tile coordinate units. Most vector tile servers use 64 for extent 4096.
        /// Geometry in the buffer zone extends beyond the tile boundary to allow seamless
        /// line rendering across tile edges, matching MapLibre GL JS behavior.
        /// </summary>
        private const int DefaultTileBuffer = 64;

        // Cached layer-level properties for zoom-dependent line-width re-evaluation
        // (main-thread only). Fill paint is fully baked into vertex data so no caching
        // is needed there.
        private LinePaintProperties _linePaintProps;
        private float _dispatchBaseLineWidthCSS;

        // Layer-level translate offsets (CSS pixels). Spec: fill-translate / line-translate
        // shifts the rendered geometry by [x, y] CSS px. Anchor "map" rotates with the
        // world; "viewport" stays screen-aligned.
        private Vector2 _fillTranslateCSS;
        private string _fillTranslateAnchor = "map";
        private Vector2 _lineTranslateCSS;
        private string _lineTranslateAnchor = "map";

        // Feature-state lookup hook. Stamped into every EvaluationContext built
        // during dispatch so ["feature-state", ...] expressions resolve against
        // the live store. Renderers that pre-date the feature-state work were
        // happy without this; we keep the field nullable for that reason.
        private Expressions.IFeatureStateStore _featureStateStore;

        /// <summary>
        /// Inject the feature-state store. MapRenderer wires this on construction;
        /// passing null disables the lookup (expressions return null and any
        /// surrounding coalesce/case branches take over).
        /// </summary>
        public void SetFeatureStateStore(Expressions.IFeatureStateStore store)
            => _featureStateStore = store;

        private EvaluationContext MakeContext(float zoom, string sourceId, string sourceLayer)
        {
            var c = new EvaluationContext(zoom)
            {
                SourceId = sourceId,
                SourceLayer = sourceLayer,
                FeatureStateStore = _featureStateStore,
            };
            return c;
        }

        private EvaluationContext MakeContext(float zoom, string sourceId, string sourceLayer,
            VectorTileFeature feature)
        {
            var c = new EvaluationContext(zoom, feature)
            {
                SourceId = sourceId,
                SourceLayer = sourceLayer,
                FeatureStateStore = _featureStateStore,
            };
            return c;
        }

        /// <summary>
        /// Bucket features by the pattern name produced by <paramref name="patternExpr"/>
        /// (a fill-pattern / line-pattern paint property). Each unique pattern
        /// becomes its own dispatch so the renderer can emit one GameObject per
        /// pattern with its own MaterialPropertyBlock. Features with no pattern
        /// (expression returns null/empty, or the supplied expression is null)
        /// land in the "" bucket and render with the plain fill / line material.
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

        private struct PendingMesh
        {
            public CanonicalTileID TileId;
            public string LayerId;
            // Distinguishes per-feature pattern groups within one (tile, layer):
            // "" = no pattern, otherwise the resolved pattern name. Used by the
            // duplicate-mesh guard so multiple groups for the same tile don't
            // get collapsed.
            public string GroupKey;
            public bool IsLine;
            public bool IsPattern;
            public MeshData MeshData;
            public Material Material;
            public float LayerOpacity;
            public Vector4 DashPattern;
            public float DashTotal;
            public int TileExtent;
            public MercatorCoordinate MapCenter;
            public float Zoom;
            // Pattern-mode fields
            public Texture2D PatternTexture;
            public Vector4 PatternUVRect;
            public Vector4 PatternPixelSize;
        }

        /// <param name="layerOrder">Layer index in style (0-based). Higher = rendered on top.</param>
        public VectorTileRenderer(Transform parent, Shader fillShader, Shader lineShader,
            int layerOrder = 0, Shader fillPatternShader = null, SpriteAtlas spriteAtlas = null,
            Shader linePatternShader = null, Shader lineGradientShader = null,
            System.Func<string, bool> lineMetricsResolver = null)
        {
            _parent = parent;
            _spriteAtlas = spriteAtlas;
            _lineGradientShader = lineGradientShader;
            _lineMetricsResolver = lineMetricsResolver;

            // Each layer gets a unique render queue for correct draw ordering.
            // Transparent base = 3000. Later layers render on top.
            int renderQueue = 3000 + layerOrder;

            if (fillPatternShader != null)
            {
                _fillPatternMaterial = new Material(fillPatternShader);
                _fillPatternMaterial.renderQueue = renderQueue;
            }

            if (linePatternShader != null)
            {
                _linePatternMaterial = new Material(linePatternShader);
                _linePatternMaterial.renderQueue = renderQueue;
            }

            if (fillShader != null)
            {
                _fillMaterial = new Material(fillShader);
                _fillMaterial.renderQueue = renderQueue;
            }
            if (lineShader != null)
            {
                _lineMaterial = new Material(lineShader);
                _lineMaterial.renderQueue = renderQueue;
            }

            // Small Y offset per layer to avoid z-fighting between fill layers
            _yOffset = 0.001f + layerOrder * 0.001f;
        }

        /// <summary>
        /// Update the layer's render order (z-index). Called by MoveLayer().
        /// Adjusts renderQueue + y-offset so the layer draws above/below siblings.
        /// </summary>
        public void SetLayerOrder(int layerOrder)
        {
            int rq = 3000 + layerOrder;
            if (_fillMaterial != null) _fillMaterial.renderQueue = rq;
            if (_lineMaterial != null) _lineMaterial.renderQueue = rq;
            if (_fillPatternMaterial != null) _fillPatternMaterial.renderQueue = rq;
            if (_linePatternMaterial != null) _linePatternMaterial.renderQueue = rq;
            // Per-layer gradient materials live in _gradientCache; keep their
            // queue in step so MoveLayer works for line-gradient layers too.
            foreach (var entry in _gradientCache.Values)
                if (entry.Material != null) entry.Material.renderQueue = rq;
            _yOffset = 0.001f + layerOrder * 0.001f;
        }

        /// <summary>
        /// Render a vector tile for a specific layer.
        /// Mesh data computation is dispatched to a background thread.
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

            switch (layerDef.Type)
            {
                case LayerType.Fill:
                    DispatchFillMesh(tileId, tileLayer, layerDef, mapCenter, zoom);
                    break;
                case LayerType.Line:
                    DispatchLineMesh(tileId, tileLayer, layerDef, mapCenter, zoom);
                    break;
            }
        }

        private void DispatchFillMesh(CanonicalTileID tileId, VectorTileLayer tileLayer,
            LayerDefinition layerDef, MercatorCoordinate mapCenter, float zoom)
        {
            var paintProps = StyleParser.ParseFillPaint(layerDef.Paint);

            var features = FilterFeatures(tileLayer, GeometryType.Polygon, layerDef.Filter, zoom,
                layerDef.Source, layerDef.SourceLayer);
            if (features.Count == 0) return;

            int extent = tileLayer.Extent;

            _fillTranslateCSS = paintProps.FillTranslate != null && paintProps.FillTranslate.Length >= 2
                ? new Vector2(paintProps.FillTranslate[0], paintProps.FillTranslate[1])
                : Vector2.zero;
            _fillTranslateAnchor = paintProps.FillTranslateAnchor ?? "map";

            // fill-pattern is per-feature: ["match", ["get", "class"], "park", ...]
            // resolves to a different sprite for each feature. Group features by
            // their resolved pattern name and emit one mesh per group -- same
            // pattern groups in atlas use _fillPatternMaterial, the (possibly
            // implicit) "" group falls back to the plain _fillMaterial.
            var groups = GroupByPattern(features, paintProps.FillPattern,
                layerDef.Source, layerDef.SourceLayer, zoom);

            var colorExpr = paintProps.FillColor;
            var opacityExpr = paintProps.FillOpacity;
            var capturedExtent = extent;
            var capturedZoom = zoom;
            // Captured for the background mesh build so feature-state lookups
            // resolve against the live store (the renderer field can change).
            var capturedSourceId = layerDef.Source;
            var capturedSourceLayer = layerDef.SourceLayer;
            var capturedStore = _featureStateStore;

            foreach (var kv in groups)
            {
                string groupKey = kv.Key;
                var groupFeatures = kv.Value;

                // The out variables are only definitely assigned when the &&
                // chain reaches TryGetEntry -- pre-declare so the if-branch can
                // reference them without the compiler tripping on short-circuit.
                SpriteEntry patternEntry = default;
                Rect patternUv = default;
                Texture2D patternTex = null;
                bool usePattern = !string.IsNullOrEmpty(groupKey)
                    && _fillPatternMaterial != null && _spriteAtlas != null
                    && _spriteAtlas.TryGetEntry(groupKey, out patternEntry,
                        out patternUv, out patternTex);

                if (usePattern)
                {
                    var rectVec = new Vector4(patternUv.x, patternUv.y,
                        patternUv.width, patternUv.height);
                    var pixelSize = new Vector4(
                        patternEntry.Width / patternEntry.PixelRatio,
                        patternEntry.Height / patternEntry.PixelRatio, 0f, 0f);
                    var capturedTex = patternTex;
                    var capturedGroup = groupFeatures;
                    var capturedKey = groupKey;

                    BackgroundTask.Run(() =>
                    {
                        var meshData = VectorTileMeshBuilder.BuildFillMeshData(
                            capturedGroup, capturedExtent, colorExpr, opacityExpr, capturedZoom,
                            capturedSourceId, capturedSourceLayer, capturedStore);
                        if (meshData == null) return;
                        lock (_meshQueue)
                        {
                            _meshQueue.Enqueue(new PendingMesh
                            {
                                TileId = tileId,
                                LayerId = layerDef.Id,
                                GroupKey = capturedKey,
                                IsLine = false,
                                IsPattern = true,
                                MeshData = meshData,
                                Material = _fillPatternMaterial,
                                LayerOpacity = 1f,
                                TileExtent = extent,
                                MapCenter = mapCenter,
                                Zoom = zoom,
                                PatternTexture = capturedTex,
                                PatternUVRect = rectVec,
                                PatternPixelSize = pixelSize
                            });
                        }
                    });
                }
                else
                {
                    var capturedGroup = groupFeatures;

                    BackgroundTask.Run(() =>
                    {
                        var meshData = VectorTileMeshBuilder.BuildFillMeshData(
                            capturedGroup, capturedExtent, colorExpr, opacityExpr, capturedZoom,
                            capturedSourceId, capturedSourceLayer, capturedStore);
                        if (meshData == null) return;
                        lock (_meshQueue)
                        {
                            _meshQueue.Enqueue(new PendingMesh
                            {
                                TileId = tileId,
                                LayerId = layerDef.Id,
                                GroupKey = "",
                                IsLine = false,
                                IsPattern = false,
                                MeshData = meshData,
                                Material = _fillMaterial,
                                LayerOpacity = 1f,
                                TileExtent = extent,
                                MapCenter = mapCenter,
                                Zoom = zoom
                            });
                        }
                    });
                }
            }
        }

        private void DispatchLineMesh(CanonicalTileID tileId, VectorTileLayer tileLayer,
            LayerDefinition layerDef, MercatorCoordinate mapCenter, float zoom)
        {
            var paintProps = StyleParser.ParseLinePaint(layerDef.Paint);

            // MapLibre GL JS renders line layers for both LineString and Polygon boundaries.
            // The mesh builder handles both geometry types in one pass.
            var features = FilterLineEligibleFeatures(tileLayer, layerDef.Filter, zoom,
                layerDef.Source, layerDef.SourceLayer);
            if (features.Count == 0) return;

            int extent = tileLayer.Extent;

            // Pack dash pattern into Vector4 (up to 4 entries, matching MapLibre spec)
            Vector4 dashPattern = Vector4.zero;
            float dashTotal = 0f;
            if (paintProps.LineDasharray != null && paintProps.LineDasharray.Length > 0)
            {
                var d = paintProps.LineDasharray;
                int count = Mathf.Min(d.Length, 4);
                if (count >= 1) dashPattern.x = d[0];
                if (count >= 2) dashPattern.y = d[1];
                if (count >= 3) dashPattern.z = d[2];
                if (count >= 4) dashPattern.w = d[3];
                for (int i = 0; i < count; i++) dashTotal += d[i];
            }

            _linePaintProps = paintProps;
            // Base width used for zoom-dependent _WidthScale ratio. Evaluated without
            // a feature so it represents the camera-only component of the expression.
            _dispatchBaseLineWidthCSS = paintProps.ResolveLineWidth(
                MakeContext(zoom, layerDef.Source, layerDef.SourceLayer));

            _lineTranslateCSS = paintProps.LineTranslate != null && paintProps.LineTranslate.Length >= 2
                ? new Vector2(paintProps.LineTranslate[0], paintProps.LineTranslate[1])
                : Vector2.zero;
            _lineTranslateAnchor = paintProps.LineTranslateAnchor ?? "map";

            // line-gradient takes precedence over line-pattern and line-color.
            // Spec: only effective when the source has lineMetrics: true. When
            // both are wired we bake a 256x1 ramp from the expression and route
            // every feature through MapLibre/LineGradient (no per-feature
            // bucketing -- the gradient is layer-uniform).
            bool gradientEligible = paintProps.LineGradient != null
                && _lineGradientShader != null
                && (_lineMetricsResolver == null || _lineMetricsResolver(layerDef.Source));

            if (gradientEligible)
            {
                var gradientMat = EnsureGradientMaterial(layerDef.Id, paintProps.LineGradient,
                    capturedZoom: zoom);
                if (gradientMat != null)
                {
                    DispatchLineGradient(tileId, features, layerDef, mapCenter, zoom, extent,
                        paintProps, gradientMat);
                    return;
                }
            }
            else if (paintProps.LineGradient != null)
            {
                Debug.LogWarning(
                    $"[MapLibre] line-gradient on '{layerDef.Id}' ignored: source '{layerDef.Source}'"
                    + " does not have lineMetrics: true.");
            }

            // line-pattern is per-feature (same shape as fill-pattern). Group
            // features by their resolved pattern; "" group falls back to the
            // plain line material (and inherits the layer's dash pattern).
            var groups = GroupByPattern(features, paintProps.LinePattern,
                layerDef.Source, layerDef.SourceLayer, zoom);

            var colorExpr = paintProps.LineColor;
            var opacityExpr = paintProps.LineOpacity;
            var widthExpr = paintProps.LineWidth;
            var offsetExpr = paintProps.LineOffset;
            var capturedExtent = extent;
            var capturedZoom = zoom;
            var capturedSourceId = layerDef.Source;
            var capturedSourceLayer = layerDef.SourceLayer;
            var capturedStore = _featureStateStore;

            foreach (var kv in groups)
            {
                string groupKey = kv.Key;
                var groupFeatures = kv.Value;

                SpriteEntry patternEntry = default;
                Rect patternUv = default;
                Texture2D patternTex = null;
                bool usePattern = !string.IsNullOrEmpty(groupKey)
                    && _linePatternMaterial != null && _spriteAtlas != null
                    && _spriteAtlas.TryGetEntry(groupKey, out patternEntry,
                        out patternUv, out patternTex);

                if (usePattern)
                {
                    var rectVec = new Vector4(patternUv.x, patternUv.y,
                        patternUv.width, patternUv.height);
                    var pixelSize = new Vector4(
                        patternEntry.Width / patternEntry.PixelRatio,
                        patternEntry.Height / patternEntry.PixelRatio, 0f, 0f);
                    var capturedTex = patternTex;
                    var capturedGroup = groupFeatures;
                    var capturedKey = groupKey;

                    BackgroundTask.Run(() =>
                    {
                        var meshData = VectorTileMeshBuilder.BuildLineMeshData(
                            capturedGroup, capturedExtent,
                            colorExpr, opacityExpr, widthExpr, offsetExpr, capturedZoom,
                            capturedSourceId, capturedSourceLayer, capturedStore);
                        if (meshData == null) return;
                        lock (_meshQueue)
                        {
                            _meshQueue.Enqueue(new PendingMesh
                            {
                                TileId = tileId,
                                LayerId = layerDef.Id,
                                GroupKey = capturedKey,
                                IsLine = true,
                                IsPattern = true,
                                MeshData = meshData,
                                Material = _linePatternMaterial,
                                LayerOpacity = 1f,
                                // Dash pattern does not apply to line-pattern.
                                DashPattern = Vector4.zero,
                                DashTotal = 0f,
                                TileExtent = extent,
                                MapCenter = mapCenter,
                                Zoom = zoom,
                                PatternTexture = capturedTex,
                                PatternUVRect = rectVec,
                                PatternPixelSize = pixelSize
                            });
                        }
                    });
                }
                else
                {
                    var capturedGroup = groupFeatures;

                    BackgroundTask.Run(() =>
                    {
                        var meshData = VectorTileMeshBuilder.BuildLineMeshData(
                            capturedGroup, capturedExtent,
                            colorExpr, opacityExpr, widthExpr, offsetExpr, capturedZoom,
                            capturedSourceId, capturedSourceLayer, capturedStore);
                        if (meshData == null) return;
                        lock (_meshQueue)
                        {
                            _meshQueue.Enqueue(new PendingMesh
                            {
                                TileId = tileId,
                                LayerId = layerDef.Id,
                                GroupKey = "",
                                IsLine = true,
                                IsPattern = false,
                                MeshData = meshData,
                                Material = _lineMaterial,
                                LayerOpacity = 1f,
                                DashPattern = dashPattern,
                                DashTotal = dashTotal,
                                TileExtent = extent,
                                MapCenter = mapCenter,
                                Zoom = zoom
                            });
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Bake the layer's line-gradient expression into a 256x1 RGBA texture so
        /// the line shader can sample it with a 0..1 progress UV. Cached per
        /// layer id; only rebuilt when the expression instance changes (which
        /// happens on every SetPaintProperty(line-gradient) call).
        /// </summary>
        private Material EnsureGradientMaterial(string layerId,
            Expressions.Expression gradientExpr, float capturedZoom)
        {
            int version = gradientExpr.GetHashCode();
            if (_gradientCache.TryGetValue(layerId, out var entry)
                && entry.ExpressionVersion == version
                && entry.Material != null && entry.Ramp != null)
            {
                return entry.Material;
            }

            const int RampWidth = 256;
            var ramp = new Texture2D(RampWidth, 1, TextureFormat.RGBA32, false, true)
            {
                name = $"LineGradient_{layerId}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[RampWidth];
            var ctx = new EvaluationContext(capturedZoom);
            for (int i = 0; i < RampWidth; i++)
            {
                ctx.LineProgress = i / (float)(RampWidth - 1);
                pixels[i] = (Color32)gradientExpr.EvaluateColor(ctx, Color.black);
            }
            ramp.SetPixels32(pixels);
            ramp.Apply(false, true);

            // Reuse the cached material slot when only the texture changed --
            // saves a Material allocation on style edits.
            Material mat;
            if (entry.Material != null)
            {
                mat = entry.Material;
            }
            else
            {
                mat = new Material(_lineGradientShader)
                {
                    renderQueue = _lineMaterial != null ? _lineMaterial.renderQueue : 3000
                };
            }
            mat.SetTexture(GradientTexPropertyId, ramp);

            if (entry.Ramp != null && entry.Ramp != ramp)
                Object.Destroy(entry.Ramp);

            _gradientCache[layerId] = new GradientCacheEntry
            {
                Material = mat,
                Ramp = ramp,
                ExpressionVersion = version,
            };
            return mat;
        }

        private void DispatchLineGradient(CanonicalTileID tileId,
            List<VectorTileFeature> features, LayerDefinition layerDef,
            MercatorCoordinate mapCenter, float zoom, int extent,
            LinePaintProperties paintProps, Material gradientMaterial)
        {
            var widthExpr = paintProps.LineWidth;
            var offsetExpr = paintProps.LineOffset;
            var opacityExpr = paintProps.LineOpacity;
            var capturedExtent = extent;
            var capturedZoom = zoom;
            var capturedSourceId = layerDef.Source;
            var capturedSourceLayer = layerDef.SourceLayer;
            var capturedStore = _featureStateStore;

            float layerOpacity = opacityExpr != null
                ? opacityExpr.EvaluateFloat(MakeContext(zoom, layerDef.Source, layerDef.SourceLayer), 1f)
                : 1f;

            BackgroundTask.Run(() =>
            {
                // colorExpr is irrelevant for gradient -- the shader samples the
                // ramp instead. We pass null so per-vertex colors stay at the
                // default (unused). Opacity is folded into _LayerOpacity.
                var meshData = VectorTileMeshBuilder.BuildLineMeshData(
                    features, capturedExtent,
                    null, null, widthExpr, offsetExpr, capturedZoom,
                    capturedSourceId, capturedSourceLayer, capturedStore,
                    emitLineProgress: true);
                if (meshData == null) return;
                lock (_meshQueue)
                {
                    _meshQueue.Enqueue(new PendingMesh
                    {
                        TileId = tileId,
                        LayerId = layerDef.Id,
                        GroupKey = "__gradient",
                        IsLine = true,
                        IsPattern = false,
                        MeshData = meshData,
                        Material = gradientMaterial,
                        LayerOpacity = layerOpacity,
                        DashPattern = Vector4.zero,
                        DashTotal = 0f,
                        TileExtent = extent,
                        MapCenter = mapCenter,
                        Zoom = zoom,
                    });
                }
            });
        }

        /// <summary>
        /// Filter features by geometry type and layer filter expression. Non-static
        /// so the EvaluationContext picks up <see cref="_featureStateStore"/> and
        /// the layer's source ids -- needed for filters that read feature-state
        /// (e.g. <c>["==", ["feature-state", "selected"], true]</c>).
        /// </summary>
        private List<VectorTileFeature> FilterFeatures(VectorTileLayer layer,
            GeometryType type, Expression filter, float zoom,
            string sourceId, string sourceLayer)
        {
            var result = new List<VectorTileFeature>();
            foreach (var feature in layer.Features)
            {
                if (feature.Type != type) continue;

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
        /// Collect features eligible for a line layer: LineString and Polygon (boundary stroke).
        /// </summary>
        private List<VectorTileFeature> FilterLineEligibleFeatures(VectorTileLayer layer,
            Expression filter, float zoom, string sourceId, string sourceLayer)
        {
            var result = new List<VectorTileFeature>();
            foreach (var feature in layer.Features)
            {
                if (feature.Type != GeometryType.LineString &&
                    feature.Type != GeometryType.Polygon) continue;

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
        /// Dequeues completed mesh data and creates Unity GameObjects.
        /// Processes up to MaxMeshesPerFrame per call to maintain frame rate.
        /// </summary>
        public void ProcessMeshQueue()
        {
            // Stragglers from a previous in-place rebuild whose new meshes
            // never arrived (e.g. the feature was filtered out post-rebuild).
            if (_staleSweepDeadlineFrame >= 0 && Time.frameCount >= _staleSweepDeadlineFrame)
                SweepStaleTileObjects();

            int processed = 0;
            while (processed < MaxMeshesPerFrame)
            {
                PendingMesh pending;
                lock (_meshQueue)
                {
                    if (_meshQueue.Count == 0) break;
                    pending = _meshQueue.Dequeue();
                }

                // Skip expired tiles (already removed by HideTile)
                if (!_requestedTiles.Contains(pending.TileId))
                {
                    processed++;
                    continue;
                }

                // Each (tile, layer, group) emits one GameObject. Skip if we
                // already realised this exact triple -- re-dispatching a tile
                // can race the original Task.Run and produce a second mesh
                // whose contents duplicate the first.
                string targetName = string.IsNullOrEmpty(pending.GroupKey)
                    ? $"{pending.LayerId}_{pending.TileId}"
                    : $"{pending.LayerId}_{pending.GroupKey}_{pending.TileId}";
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

                var go = CreateMeshObject(targetName, mesh, pending.Material);
                PositionTileObject(go, pending.TileId, pending.MapCenter, pending.Zoom);

                var renderer = go.GetComponent<MeshRenderer>();
                _propertyBlock.SetFloat(LayerOpacityPropertyId, pending.LayerOpacity);

                if (pending.IsLine)
                {
                    float cssToLocal = ComputeCSSToLocal(pending.TileId.Z, pending.Zoom, _lastFrustumHeight);
                    _propertyBlock.SetFloat(CSSToLocalPropertyId, cssToLocal);
                    _propertyBlock.SetFloat(WidthScalePropertyId, 1f);

                    // Extend clip boundary into the MVT buffer zone so lines
                    // are not cut at tile edges.
                    float clipExtend = (float)DefaultTileBuffer / pending.TileExtent;
                    _propertyBlock.SetFloat(ClipExtendPropertyId, clipExtend);

                    if (pending.IsPattern)
                    {
                        _propertyBlock.SetTexture(PatternTexPropertyId, pending.PatternTexture);
                        _propertyBlock.SetVector(PatternUVRectPropertyId, pending.PatternUVRect);
                        _propertyBlock.SetVector(PatternPixelSizePropertyId, pending.PatternPixelSize);
                    }
                    else
                    {
                        _propertyBlock.SetVector(DashPatternPropertyId, pending.DashPattern);
                        _propertyBlock.SetFloat(DashTotalPropertyId, pending.DashTotal);
                    }
                }
                else if (pending.IsPattern)
                {
                    // Fill pattern
                    float cssToLocal = ComputeCSSToLocal(pending.TileId.Z, pending.Zoom, _lastFrustumHeight);
                    _propertyBlock.SetFloat(CSSToLocalPropertyId, cssToLocal);
                    _propertyBlock.SetTexture(PatternTexPropertyId, pending.PatternTexture);
                    _propertyBlock.SetVector(PatternUVRectPropertyId, pending.PatternUVRect);
                    _propertyBlock.SetVector(PatternPixelSizePropertyId, pending.PatternPixelSize);
                }

                renderer.SetPropertyBlock(_propertyBlock);

                if (!_activeTileObjects.TryGetValue(pending.TileId, out var objects))
                {
                    objects = new List<GameObject>(2);
                    _activeTileObjects[pending.TileId] = objects;
                }
                objects.Add(go);

                // In-place rebuild: now that the new GO is live, retire the
                // matching stale GO (same tile, same target name) so the
                // visible scene transitions in one frame instead of going
                // through a "torn down → empty → new" gap.
                ReplaceStaleObject(pending.TileId, targetName);

                processed++;
            }
        }

        private void ReplaceStaleObject(CanonicalTileID tileId, string targetName)
        {
            if (!_staleTileObjects.TryGetValue(tileId, out var stale)) return;
            for (int i = stale.Count - 1; i >= 0; i--)
            {
                var staleGo = stale[i];
                if (staleGo == null) { stale.RemoveAt(i); continue; }
                if (staleGo.name != targetName) continue;
                var sf = staleGo.GetComponent<MeshFilter>();
                if (sf != null && sf.sharedMesh != null) Object.Destroy(sf.sharedMesh);
                Object.Destroy(staleGo);
                stale.RemoveAt(i);
                break;
            }
            if (stale.Count == 0) _staleTileObjects.Remove(tileId);
        }

        private void SweepStaleTileObjects()
        {
            foreach (var kv in _staleTileObjects)
            {
                foreach (var go in kv.Value)
                {
                    if (go == null) continue;
                    var sf = go.GetComponent<MeshFilter>();
                    if (sf != null && sf.sharedMesh != null) Object.Destroy(sf.sharedMesh);
                    Object.Destroy(go);
                }
            }
            _staleTileObjects.Clear();
            _staleSweepDeadlineFrame = -1;
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

        private GameObject CreateMeshObject(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_parent, false);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
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

        public void UpdateAllPositions(MercatorCoordinate mapCenter, float zoom,
            float frustumHeight = 0f, float bearingDeg = 0f)
        {
            if (frustumHeight > 0f)
                _lastFrustumHeight = frustumHeight;

            float worldScale = CoordinateConversion.GetWorldScale(zoom);

            float widthScale = 1f;
            if (_linePaintProps != null && _dispatchBaseLineWidthCSS > 0f)
            {
                var ctx = new EvaluationContext(zoom);
                float currentBaseWidth = _linePaintProps.ResolveLineWidth(ctx);
                widthScale = currentBaseWidth / _dispatchBaseLineWidthCSS;
            }

            // Convert layer-level translate (CSS px) to world XZ for each material kind.
            // "viewport" anchor: rotate by -bearing so the offset stays screen-aligned;
            // "map" anchor: leave as-is so it rotates with the world.
            float pxToWorld = _lastFrustumHeight > 0f
                ? _lastFrustumHeight / Mathf.Max(Screen.height, MinScreenHeight)
                : 0f;
            Vector2 fillOffWorld = TranslateToWorld(_fillTranslateCSS, _fillTranslateAnchor,
                pxToWorld, bearingDeg);
            Vector2 lineOffWorld = TranslateToWorld(_lineTranslateCSS, _lineTranslateAnchor,
                pxToWorld, bearingDeg);

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

                    var renderer = go.GetComponent<MeshRenderer>();
                    var mat = renderer != null ? renderer.sharedMaterial : null;
                    bool isGradientMat = mat != null && _lineGradientShader != null
                        && mat.shader == _lineGradientShader;
                    bool isLine = (_lineMaterial != null && mat == _lineMaterial) ||
                                  (_linePatternMaterial != null && mat == _linePatternMaterial)
                                  || isGradientMat;
                    bool isFill = (_fillMaterial != null && mat == _fillMaterial) ||
                                  (_fillPatternMaterial != null && mat == _fillPatternMaterial);
                    Vector3 finalPos = worldPos;
                    if (isLine && lineOffWorld != Vector2.zero)
                    {
                        finalPos.x += lineOffWorld.x;
                        finalPos.z += lineOffWorld.y;
                    }
                    else if (isFill && fillOffWorld != Vector2.zero)
                    {
                        finalPos.x += fillOffWorld.x;
                        finalPos.z += fillOffWorld.y;
                    }
                    go.transform.localPosition = finalPos;
                    go.transform.localScale = new Vector3(tileWorldSize, 1f, tileWorldSize);

                    if (renderer == null) continue;
                    if (isLine)
                    {
                        renderer.GetPropertyBlock(_propertyBlock);
                        _propertyBlock.SetFloat(CSSToLocalPropertyId, cssToLocal);
                        _propertyBlock.SetFloat(WidthScalePropertyId, widthScale);
                        renderer.SetPropertyBlock(_propertyBlock);
                    }
                    else if (_fillPatternMaterial != null && mat == _fillPatternMaterial)
                    {
                        renderer.GetPropertyBlock(_propertyBlock);
                        _propertyBlock.SetFloat(CSSToLocalPropertyId, cssToLocal);
                        renderer.SetPropertyBlock(_propertyBlock);
                    }
                }
            }
        }

        /// <summary>
        /// Convert a CSS-pixel translate vector ([x, y]) into a world XZ offset.
        /// "viewport" anchor: counter-rotate by bearing so the offset stays
        /// screen-aligned; "map" anchor: keep raw axes (rotates with the world).
        /// MapLibre treats positive Y as downward on screen, so screen-down maps to
        /// -Z in our XZ plane.
        /// </summary>
        private static Vector2 TranslateToWorld(Vector2 cssOffset, string anchor,
            float pxToWorld, float bearingDeg)
        {
            if (cssOffset == Vector2.zero || pxToWorld <= 0f) return Vector2.zero;
            // CSS axes (x right, y down) → world XZ (x east, z north): (x, -y).
            Vector2 world = new(cssOffset.x * pxToWorld, -cssOffset.y * pxToWorld);
            if (anchor == "viewport")
            {
                // Counter-rotate by bearing so the offset stays screen-aligned.
                float rad = -bearingDeg * Mathf.Deg2Rad;
                float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                world = new Vector2(world.x * c - world.y * s, world.x * s + world.y * c);
            }
            return world;
        }

        public bool HasTile(CanonicalTileID tileId) => _activeTileObjects.ContainsKey(tileId);

        public void Clear()
        {
            lock (_meshQueue) { _meshQueue.Clear(); }
            _requestedTiles.Clear();
            var keys = new List<CanonicalTileID>(_activeTileObjects.Keys);
            foreach (var key in keys)
                HideTile(key);
            SweepStaleTileObjects();
        }

        /// <summary>
        /// Soft variant of <see cref="Clear"/> for feature-state-driven rebuilds.
        /// Retains every active GameObject under a stale snapshot so they stay
        /// visible while the re-dispatched meshes are computed; ProcessMeshQueue
        /// retires them one-by-one as the matching new GOs arrive. Without this
        /// path, every SetFeatureState call would tear down the entire layer
        /// and the polygons that were not the hover target would briefly
        /// disappear too.
        /// </summary>
        public void InvalidateInPlace()
        {
            lock (_meshQueue) { _meshQueue.Clear(); }
            _requestedTiles.Clear();
            foreach (var kv in _activeTileObjects)
            {
                if (!_staleTileObjects.TryGetValue(kv.Key, out var list))
                    _staleTileObjects[kv.Key] = list = new List<GameObject>(kv.Value.Count);
                list.AddRange(kv.Value);
            }
            _activeTileObjects.Clear();
            // Sweep any stragglers ~0.5s later in case the rebuild emits no
            // mesh for a tile (e.g. all features filtered out post-state).
            _staleSweepDeadlineFrame = Time.frameCount + 30;
        }

        public void Dispose()
        {
            Clear();
            if (_fillMaterial != null) Object.Destroy(_fillMaterial);
            if (_lineMaterial != null) Object.Destroy(_lineMaterial);
            if (_fillPatternMaterial != null) Object.Destroy(_fillPatternMaterial);
            if (_linePatternMaterial != null) Object.Destroy(_linePatternMaterial);
            foreach (var entry in _gradientCache.Values)
            {
                if (entry.Material != null) Object.Destroy(entry.Material);
                if (entry.Ramp != null) Object.Destroy(entry.Ramp);
            }
            _gradientCache.Clear();
        }
    }
}
