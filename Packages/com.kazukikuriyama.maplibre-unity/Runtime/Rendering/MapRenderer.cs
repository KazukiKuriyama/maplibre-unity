using System.Collections.Generic;
using MapLibre.Unity.Style;
using MapLibre.Unity.Terrain;
using MapLibre.Unity.VectorTile;
using TMPro;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    public class MapRenderer
    {
        private BackgroundRenderer _backgroundRenderer;
        private BackgroundPaintProperties _backgroundPaint;
        private readonly Dictionary<string, RasterTileRenderer> _rasterRenderers = new();
        private readonly Dictionary<string, ImageRenderer> _imageRenderers = new();
        private readonly Dictionary<string, TerrainTileRenderer> _terrainRenderers = new();
        private readonly Dictionary<string, VectorTileRenderer> _vectorRenderers = new();
        private readonly Dictionary<string, SymbolRenderer> _symbolRenderers = new();
        private readonly Dictionary<string, CircleRenderer> _circleRenderers = new();
        private readonly Dictionary<string, FillExtrusionRenderer> _fillExtrusionRenderers = new();
        private readonly Dictionary<string, HeatmapRenderer> _heatmapRenderers = new();
        private readonly Dictionary<string, HillshadeRenderer> _hillshadeRenderers = new();
        // Unified view of every tile-driven renderer, keyed by layer id. Mirrors
        // the type-specific dictionaries above so HideTile / SetLayerOrder /
        // Dispose can dispatch through a single lookup instead of switching on
        // layer type. Each entry implements <see cref="ILayerRenderer"/>.
        private readonly Dictionary<string, ILayerRenderer> _allTileRenderers = new();
        private readonly List<string> _vectorLayerIds = new();
        private readonly List<string> _symbolLayerIds = new();
        private readonly List<string> _circleLayerIds = new();
        private readonly List<string> _fillExtrusionLayerIds = new();
        private readonly List<string> _heatmapLayerIds = new();
        private readonly List<string> _hillshadeLayerIds = new();
        private Transform _mapRoot;
        private MapLibreStyle _style;
        private Pool.TileObjectPool _tilePool;
        private Shader _fillShader;
        private Shader _fillPatternShader;
        private Shader _lineShader;
        private Shader _linePatternShader;
        private Shader _lineGradientShader;
        private Shader _backgroundShader;
        private Shader _backgroundPatternShader;
        // Resolves the source for a layer to its declared lineMetrics flag,
        // gating line-gradient (per Style Spec). MapLibreMap supplies it.
        private System.Func<string, bool> _lineMetricsResolver;
        private Shader _circleShader;
        private Shader _fillExtrusionShader;
        private Shader _fillExtrusionPatternShader;
        private Shader _skyShader;
        private SkyRenderer _skyRenderer;
        private Shader _heatmapShader;
        private Shader _hillshadeShader;
        private Shader _terrainShader;
        private Shader _rasterShader;
        private TerrainManager _terrainManager;
        private TMP_FontAsset _symbolFont;
        private TMP_FontAsset[] _symbolFontFallbacks;
        private SpriteAtlas _spriteAtlas;
        private Material _iconMaterial;
        private int _nextLayerOrder;
        private System.Func<string, SourceType?> _sourceTypeResolver;

        // Live feature-state lookup. Threaded through to the vector renderer so
        // ["feature-state", ...] expressions in fill/line layers resolve against
        // the map's current state. Stored at the renderer level so layers added
        // after Initialize() also receive it.
        private Expressions.IFeatureStateStore _featureStateStore;

        /// <summary>
        /// Inject the feature-state store. Call once after Initialize() (typically
        /// MapLibreMap passes itself). Existing vector renderers receive the store
        /// immediately; AddRendererForLayer wires it for layers added later.
        /// </summary>
        public void SetFeatureStateStore(Expressions.IFeatureStateStore store)
        {
            _featureStateStore = store;
            foreach (var r in _vectorRenderers.Values) r.SetFeatureStateStore(store);
            foreach (var r in _circleRenderers.Values) r.SetFeatureStateStore(store);
            foreach (var r in _heatmapRenderers.Values) r.SetFeatureStateStore(store);
            foreach (var r in _fillExtrusionRenderers.Values) r.SetFeatureStateStore(store);
            foreach (var r in _symbolRenderers.Values) r.SetFeatureStateStore(store);
        }

        // SDF text path. When _glyphSource is non-null and _sdfTextShader is
        // wired, every Symbol renderer prefers self-rendered SDF text over
        // TextMeshPro for point placement.
        private Source.GlyphSource _glyphSource;
        private Shader _sdfTextShader;

        /// <summary>
        /// Inject the glyph source + SDF text shader. Pass null/null to disable
        /// SDF text on every Symbol renderer (TMP fallback). MapLibreMap calls
        /// this whenever the active style's <c>glyphs</c> URL changes.
        /// </summary>
        public void SetGlyphSource(Source.GlyphSource glyphSource, Shader sdfTextShader)
        {
            _glyphSource = glyphSource;
            _sdfTextShader = sdfTextShader;
            foreach (var r in _symbolRenderers.Values)
                r.SetGlyphSource(glyphSource, sdfTextShader);
        }

        /// <summary>
        /// Drop all active tile GameObjects for one layer so the next dispatch
        /// rebuilds with current state. Used by feature-state invalidation to
        /// avoid the heavy <see cref="MapLibreMap.RefreshTiles"/> path that
        /// rebuilds every layer's tiles. Returns true when a renderer was
        /// matched (raster/hillshade/terrain/image return false -- those layer
        /// types don't observe feature-state today).
        /// </summary>
        public bool InvalidateLayer(string layerId)
        {
            // Vector (Fill/Line) uses an in-place rebuild path: the prior
            // GameObjects stay visible until their replacements arrive, so
            // SetFeatureState updates don't flicker the layer's other
            // features. Circle/Heatmap/FillExtrusion/Symbol still take the
            // hard Clear() path; can be migrated when those layer types
            // start exercising feature-state in real demos.
            if (_vectorRenderers.TryGetValue(layerId, out var vr)) { vr.InvalidateInPlace(); return true; }
            if (_circleRenderers.TryGetValue(layerId, out var cr)) { cr.Clear(); return true; }
            if (_heatmapRenderers.TryGetValue(layerId, out var hr)) { hr.Clear(); return true; }
            if (_fillExtrusionRenderers.TryGetValue(layerId, out var fer)) { fer.Clear(); return true; }
            if (_symbolRenderers.TryGetValue(layerId, out var sr)) { sr.Clear(); return true; }
            return false;
        }

        public void Initialize(Transform mapRoot, MapLibreStyle style, Pool.TileObjectPool tilePool,
            Shader backgroundShader, Shader fillShader = null, Shader lineShader = null,
            Shader circleShader = null, Shader fillExtrusionShader = null,
            Shader heatmapShader = null, Shader hillshadeShader = null,
            Shader terrainShader = null, TerrainManager terrainManager = null,
            TMP_FontAsset symbolFont = null, SpriteAtlas spriteAtlas = null, Material iconMaterial = null,
            Shader rasterShader = null,
            System.Func<string, SourceType?> sourceTypeResolver = null,
            Shader fillPatternShader = null,
            Shader linePatternShader = null,
            TMP_FontAsset[] symbolFontFallbacks = null,
            Shader fillExtrusionPatternShader = null,
            Shader skyShader = null,
            Shader lineGradientShader = null,
            System.Func<string, bool> lineMetricsResolver = null,
            Shader backgroundPatternShader = null)
        {
            _mapRoot = mapRoot;
            _style = style;
            _tilePool = tilePool;
            _fillShader = fillShader;
            _fillPatternShader = fillPatternShader;
            _lineShader = lineShader;
            _linePatternShader = linePatternShader;
            _lineGradientShader = lineGradientShader;
            _lineMetricsResolver = lineMetricsResolver;
            _backgroundShader = backgroundShader;
            _backgroundPatternShader = backgroundPatternShader;
            _circleShader = circleShader;
            _fillExtrusionShader = fillExtrusionShader;
            _fillExtrusionPatternShader = fillExtrusionPatternShader;
            _skyShader = skyShader;

            // Sky renderer (optional). Always create; disabled until SetSky(...) is called.
            if (_skyShader != null)
            {
                _skyRenderer = new SkyRenderer(mapRoot, _skyShader);
                _skyRenderer.SetSky(style.Sky);
            }
            _heatmapShader = heatmapShader;
            _hillshadeShader = hillshadeShader;
            _terrainShader = terrainShader;
            _rasterShader = rasterShader;
            _terrainManager = terrainManager;
            _symbolFont = symbolFont;
            _symbolFontFallbacks = symbolFontFallbacks;
            _spriteAtlas = spriteAtlas;
            _iconMaterial = iconMaterial;
            _sourceTypeResolver = sourceTypeResolver;

            int vectorLayerOrder = 0;
            foreach (var layer in style.Layers)
            {
                switch (layer.Type)
                {
                    case LayerType.Background:
                    {
                        EnsureBackgroundRenderer(layer);
                        break;
                    }
                    case LayerType.Raster:
                    {
                        var sourceType = _sourceTypeResolver?.Invoke(layer.Source);
                        if (sourceType == SourceType.Image)
                        {
                            // Image sources render a single quad draped over 4 geographic
                            // corners -- no tile management.
                            var imageRenderer = new ImageRenderer(mapRoot, _rasterShader, vectorLayerOrder);
                            imageRenderer.SetPaintProperties(StyleParser.ParseRasterPaint(layer.Paint));
                            _imageRenderers[layer.Id] = imageRenderer;
                        }
                        else if (terrainManager != null && terrainShader != null)
                        {
                            // When the style declares "terrain", route raster layers through
                            // a 3D-displacement renderer so the map drapes over the DEM.
                            var terrainRenderer = new TerrainTileRenderer(mapRoot, terrainShader, terrainManager);
                            _terrainRenderers[layer.Id] = terrainRenderer;
                            _allTileRenderers[layer.Id] = terrainRenderer;
                        }
                        else
                        {
                            var rasterRenderer = new RasterTileRenderer(tilePool, vectorLayerOrder);
                            // Honor raster-fade-duration (in milliseconds per spec) → seconds.
                            var rasterPaint = StyleParser.ParseRasterPaint(layer.Paint);
                            rasterRenderer.FadeDurationSeconds = rasterPaint.FadeDuration / 1000f;
                            _rasterRenderers[layer.Id] = rasterRenderer;
                            _allTileRenderers[layer.Id] = rasterRenderer;
                        }
                        // Raster layers participate in the global layer ordering so
                        // subsequent layers (e.g. hillshade) render above them.
                        vectorLayerOrder++;
                        break;
                    }
                    case LayerType.Fill:
                    case LayerType.Line:
                    {
                        if (!_vectorRenderers.ContainsKey(layer.Id))
                        {
                            var vectorRenderer = new VectorTileRenderer(mapRoot, fillShader, lineShader,
                                vectorLayerOrder, fillPatternShader, spriteAtlas, linePatternShader,
                                lineGradientShader, _lineMetricsResolver);
                            vectorRenderer.SetFeatureStateStore(_featureStateStore);
                            _vectorRenderers[layer.Id] = vectorRenderer;
                            _allTileRenderers[layer.Id] = vectorRenderer;
                            _vectorLayerIds.Add(layer.Id);
                            vectorLayerOrder++;
                        }
                        break;
                    }
                    case LayerType.Circle:
                    {
                        if (!_circleRenderers.ContainsKey(layer.Id))
                        {
                            var circleRenderer = new CircleRenderer(mapRoot, circleShader, vectorLayerOrder);
                            circleRenderer.SetFeatureStateStore(_featureStateStore);
                            _circleRenderers[layer.Id] = circleRenderer;
                            _allTileRenderers[layer.Id] = circleRenderer;
                            _circleLayerIds.Add(layer.Id);
                            vectorLayerOrder++;
                        }
                        break;
                    }
                    case LayerType.FillExtrusion:
                    {
                        if (!_fillExtrusionRenderers.ContainsKey(layer.Id))
                        {
                            var extrusionRenderer = new FillExtrusionRenderer(mapRoot, fillExtrusionShader,
                                vectorLayerOrder, fillExtrusionPatternShader, spriteAtlas);
                            extrusionRenderer.SetLight(_style.Light);
                            extrusionRenderer.SetFeatureStateStore(_featureStateStore);
                            _fillExtrusionRenderers[layer.Id] = extrusionRenderer;
                            _allTileRenderers[layer.Id] = extrusionRenderer;
                            _fillExtrusionLayerIds.Add(layer.Id);
                            vectorLayerOrder++;
                        }
                        break;
                    }
                    case LayerType.Heatmap:
                    {
                        if (!_heatmapRenderers.ContainsKey(layer.Id))
                        {
                            var heatmapRenderer = new HeatmapRenderer(mapRoot, heatmapShader, vectorLayerOrder);
                            heatmapRenderer.SetFeatureStateStore(_featureStateStore);
                            _heatmapRenderers[layer.Id] = heatmapRenderer;
                            _allTileRenderers[layer.Id] = heatmapRenderer;
                            _heatmapLayerIds.Add(layer.Id);
                            vectorLayerOrder++;
                        }
                        break;
                    }
                    case LayerType.Hillshade:
                    {
                        if (!_hillshadeRenderers.ContainsKey(layer.Id))
                        {
                            var hillshadeRenderer = new HillshadeRenderer(mapRoot, hillshadeShader, vectorLayerOrder);
                            var hillshadePaint = StyleParser.ParseHillshadePaint(layer.Paint);
                            hillshadeRenderer.SetPaintProperties(hillshadePaint);
                            _hillshadeRenderers[layer.Id] = hillshadeRenderer;
                            _allTileRenderers[layer.Id] = hillshadeRenderer;
                            _hillshadeLayerIds.Add(layer.Id);
                            vectorLayerOrder++;
                        }
                        break;
                    }
                    case LayerType.Symbol:
                    {
                        if (!_symbolRenderers.ContainsKey(layer.Id))
                        {
                            var symbolRenderer = new SymbolRenderer(mapRoot, vectorLayerOrder,
                                fontAsset: symbolFont, spriteAtlas: spriteAtlas, iconMaterial: iconMaterial,
                                fontFallbacks: symbolFontFallbacks);
                            symbolRenderer.SetFeatureStateStore(_featureStateStore);
                            symbolRenderer.SetGlyphSource(_glyphSource, _sdfTextShader);
                            _symbolRenderers[layer.Id] = symbolRenderer;
                            _allTileRenderers[layer.Id] = symbolRenderer;
                            _symbolLayerIds.Add(layer.Id);
                            vectorLayerOrder++;
                        }
                        break;
                    }
                    case LayerType.Custom:
                    {
                        // ICustomLayer renders itself; the map only reserves the
                        // layerOrder slot so layers added later stack above it.
                        vectorLayerOrder++;
                        break;
                    }
                    default:
                        Debug.LogWarning($"[MapLibre] Layer type '{layer.Type}' not yet supported, skipping: {layer.Id}");
                        break;
                }
            }
            _nextLayerOrder = vectorLayerOrder;
        }

        /// <summary>
        /// Create or refresh the background renderer for <paramref name="layer"/>.
        /// Shared by Initialize()'s constructor-time Background case and
        /// AddRendererForLayer's dynamic-AddLayer case so both paths apply
        /// identical handling. Reuses <see cref="_backgroundRenderer"/> if one
        /// already exists (the Style Spec allows at most one background layer)
        /// instead of allocating a second.
        /// </summary>
        private void EnsureBackgroundRenderer(LayerDefinition layer)
        {
            _backgroundPaint = StyleParser.ParseBackgroundPaint(layer.Paint);
            if (_backgroundRenderer == null)
            {
                _backgroundRenderer = new BackgroundRenderer();
                _backgroundRenderer.Initialize(_mapRoot, _backgroundShader,
                    _backgroundPaint.ResolveBackgroundColor(0f),
                    _backgroundPaint.ResolveOpacity(0f),
                    _backgroundPatternShader, _spriteAtlas);
            }
            else
            {
                _backgroundRenderer.UpdateColor(_backgroundPaint.ResolveBackgroundColor(0f),
                    _backgroundPaint.ResolveOpacity(0f));
            }
            var initialPattern = _backgroundPaint.ResolveBackgroundPattern(0f);
            if (!string.IsNullOrEmpty(initialPattern))
                _backgroundRenderer.UpdatePattern(initialPattern,
                    _backgroundPaint.ResolveOpacity(0f));
        }

        /// <summary>
        /// Create a renderer for a dynamically added layer.
        /// Returns true if the renderer was created successfully.
        /// </summary>
        public bool AddRendererForLayer(LayerDefinition layer)
        {
            switch (layer.Type)
            {
                case LayerType.Background:
                    // true here means "handled" (created or reused), not "newly
                    // created" -- the Style Spec allows at most one background
                    // layer, so a repeat call updates the existing renderer
                    // instead of signalling failure.
                    EnsureBackgroundRenderer(layer);
                    return true;

                case LayerType.Raster:
                {
                    var sourceType = _sourceTypeResolver?.Invoke(layer.Source);
                    if (sourceType == SourceType.Image)
                    {
                        if (_imageRenderers.ContainsKey(layer.Id)) return false;
                        var imageRenderer = new ImageRenderer(_mapRoot, _rasterShader, _nextLayerOrder);
                        imageRenderer.SetPaintProperties(StyleParser.ParseRasterPaint(layer.Paint));
                        _imageRenderers[layer.Id] = imageRenderer;
                        _nextLayerOrder++;
                        return true;
                    }
                    if (_rasterRenderers.ContainsKey(layer.Id)) return false;
                    var newRaster = new RasterTileRenderer(_tilePool, _nextLayerOrder);
                    var newRasterPaint = StyleParser.ParseRasterPaint(layer.Paint);
                    newRaster.FadeDurationSeconds = newRasterPaint.FadeDuration / 1000f;
                    _rasterRenderers[layer.Id] = newRaster;
                    _allTileRenderers[layer.Id] = newRaster;
                    _nextLayerOrder++;
                    return true;
                }

                case LayerType.Fill:
                case LayerType.Line:
                    if (_vectorRenderers.ContainsKey(layer.Id)) return false;
                    var newVectorRenderer = new VectorTileRenderer(_mapRoot, _fillShader, _lineShader,
                        _nextLayerOrder, _fillPatternShader, _spriteAtlas, _linePatternShader,
                        _lineGradientShader, _lineMetricsResolver);
                    newVectorRenderer.SetFeatureStateStore(_featureStateStore);
                    _vectorRenderers[layer.Id] = newVectorRenderer;
                    _allTileRenderers[layer.Id] = newVectorRenderer;
                    _vectorLayerIds.Add(layer.Id);
                    _nextLayerOrder++;
                    return true;

                case LayerType.Circle:
                    if (_circleRenderers.ContainsKey(layer.Id)) return false;
                    var newCircleRenderer = new CircleRenderer(_mapRoot, _circleShader, _nextLayerOrder);
                    newCircleRenderer.SetFeatureStateStore(_featureStateStore);
                    _circleRenderers[layer.Id] = newCircleRenderer;
                    _allTileRenderers[layer.Id] = newCircleRenderer;
                    _circleLayerIds.Add(layer.Id);
                    _nextLayerOrder++;
                    return true;

                case LayerType.FillExtrusion:
                    if (_fillExtrusionRenderers.ContainsKey(layer.Id)) return false;
                    var fer = new FillExtrusionRenderer(_mapRoot, _fillExtrusionShader,
                        _nextLayerOrder, _fillExtrusionPatternShader, _spriteAtlas);
                    fer.SetLight(_style?.Light);
                    fer.SetFeatureStateStore(_featureStateStore);
                    _fillExtrusionRenderers[layer.Id] = fer;
                    _allTileRenderers[layer.Id] = fer;
                    _fillExtrusionLayerIds.Add(layer.Id);
                    _nextLayerOrder++;
                    return true;

                case LayerType.Heatmap:
                    if (_heatmapRenderers.ContainsKey(layer.Id)) return false;
                    var newHeatmapRenderer = new HeatmapRenderer(_mapRoot, _heatmapShader, _nextLayerOrder);
                    newHeatmapRenderer.SetFeatureStateStore(_featureStateStore);
                    _heatmapRenderers[layer.Id] = newHeatmapRenderer;
                    _allTileRenderers[layer.Id] = newHeatmapRenderer;
                    _heatmapLayerIds.Add(layer.Id);
                    _nextLayerOrder++;
                    return true;

                case LayerType.Hillshade:
                    if (_hillshadeRenderers.ContainsKey(layer.Id)) return false;
                    var hsRenderer = new HillshadeRenderer(_mapRoot, _hillshadeShader, _nextLayerOrder);
                    hsRenderer.SetPaintProperties(StyleParser.ParseHillshadePaint(layer.Paint));
                    _hillshadeRenderers[layer.Id] = hsRenderer;
                    _allTileRenderers[layer.Id] = hsRenderer;
                    _hillshadeLayerIds.Add(layer.Id);
                    _nextLayerOrder++;
                    return true;

                case LayerType.Symbol:
                    if (_symbolRenderers.ContainsKey(layer.Id)) return false;
                    var newSymbolRenderer = new SymbolRenderer(_mapRoot, _nextLayerOrder,
                        fontAsset: _symbolFont, spriteAtlas: _spriteAtlas, iconMaterial: _iconMaterial,
                        fontFallbacks: _symbolFontFallbacks);
                    newSymbolRenderer.SetFeatureStateStore(_featureStateStore);
                    newSymbolRenderer.SetGlyphSource(_glyphSource, _sdfTextShader);
                    _symbolRenderers[layer.Id] = newSymbolRenderer;
                    _allTileRenderers[layer.Id] = newSymbolRenderer;
                    _symbolLayerIds.Add(layer.Id);
                    _nextLayerOrder++;
                    return true;

                case LayerType.Custom:
                    // Custom layers are driven by the user's ICustomLayer.Render
                    // callback -- there is no built-in renderer to construct. We
                    // still consume a layerOrder slot so subsequent layers stack
                    // above this one in the render queue.
                    _nextLayerOrder++;
                    return true;

                default:
                    Debug.LogWarning($"[MapRenderer] AddRendererForLayer: unsupported type '{layer.Type}' for '{layer.Id}'");
                    return false;
            }
        }

        /// <summary>
        /// Remove the renderer for a dynamically removed layer.
        /// </summary>
        public void RemoveRendererForLayer(LayerDefinition layer)
        {
            switch (layer.Type)
            {
                case LayerType.Background:
                    _backgroundRenderer?.Dispose();
                    _backgroundRenderer = null;
                    _backgroundPaint = null;
                    break;

                case LayerType.Raster:
                    if (_rasterRenderers.TryGetValue(layer.Id, out var rr))
                    {
                        rr.Clear();
                        _rasterRenderers.Remove(layer.Id);
                    }
                    else if (_imageRenderers.TryGetValue(layer.Id, out var ir))
                    {
                        ir.Dispose();
                        _imageRenderers.Remove(layer.Id);
                    }
                    break;

                case LayerType.Fill:
                case LayerType.Line:
                    if (_vectorRenderers.TryGetValue(layer.Id, out var vr))
                    {
                        vr.Dispose();
                        _vectorRenderers.Remove(layer.Id);
                        _vectorLayerIds.Remove(layer.Id);
                    }
                    break;

                case LayerType.Circle:
                    if (_circleRenderers.TryGetValue(layer.Id, out var cr))
                    {
                        cr.Dispose();
                        _circleRenderers.Remove(layer.Id);
                        _circleLayerIds.Remove(layer.Id);
                    }
                    break;

                case LayerType.FillExtrusion:
                    if (_fillExtrusionRenderers.TryGetValue(layer.Id, out var fer))
                    {
                        fer.Dispose();
                        _fillExtrusionRenderers.Remove(layer.Id);
                        _fillExtrusionLayerIds.Remove(layer.Id);
                    }
                    break;

                case LayerType.Heatmap:
                    if (_heatmapRenderers.TryGetValue(layer.Id, out var hr))
                    {
                        hr.Dispose();
                        _heatmapRenderers.Remove(layer.Id);
                        _heatmapLayerIds.Remove(layer.Id);
                    }
                    break;

                case LayerType.Hillshade:
                    if (_hillshadeRenderers.TryGetValue(layer.Id, out var hsr))
                    {
                        hsr.Dispose();
                        _hillshadeRenderers.Remove(layer.Id);
                        _hillshadeLayerIds.Remove(layer.Id);
                    }
                    break;

                case LayerType.Symbol:
                    if (_symbolRenderers.TryGetValue(layer.Id, out var sr))
                    {
                        sr.Dispose();
                        _symbolRenderers.Remove(layer.Id);
                        _symbolLayerIds.Remove(layer.Id);
                    }
                    break;

                case LayerType.Custom:
                    // No internal renderer to dispose; OnRemove was called by
                    // MapLibreMap.RemoveLayer before we got here.
                    break;
            }

            // Keep the unified view in sync. Custom layers were never registered
            // here, so the Remove is a harmless no-op for them.
            _allTileRenderers.Remove(layer.Id);
        }

        /// <summary>
        /// Tear down the GameObjects/meshes that <paramref name="layerId"/> currently
        /// holds for <paramref name="tileId"/>. Single-lookup dispatch through the
        /// unified <see cref="ILayerRenderer"/> registry, replacing the per-layer-type
        /// switch that used to live in MapLibreMap.OnTileExpired. No-op when the
        /// layer is not a tile-driven renderer (background / image / custom).
        /// </summary>
        public void HideTile(string layerId, CanonicalTileID tileId)
        {
            if (_allTileRenderers.TryGetValue(layerId, out var renderer))
                renderer.HideTile(tileId);
        }

        /// <summary>
        /// Drive ICustomLayer.Prerender() for every visible custom layer.
        /// Called once per frame from MapLibreMap before any layer renders, so
        /// user code can prepare off-screen targets / stencils / etc.
        /// Visibility, minzoom and maxzoom are honoured here so user code does
        /// not need to re-check them.
        /// </summary>
        public void PrerenderCustomLayers(MapLibreMap map,
            IReadOnlyDictionary<string, ICustomLayer> customLayers,
            UnityEngine.Camera camera, float zoom)
        {
            if (customLayers == null || customLayers.Count == 0) return;
            if (_style?.Layers == null) return;

            foreach (var def in _style.Layers)
            {
                if (def.Type != LayerType.Custom) continue;
                if (!def.IsVisibleAtZoom(zoom)) continue;
                if (!customLayers.TryGetValue(def.Id, out var layer)) continue;

                try { layer.Prerender(map, camera); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[MapRenderer] ICustomLayer.Prerender threw for '{def.Id}': {e}");
                }
            }
        }

        /// <summary>
        /// Drive ICustomLayer.Render() for every visible custom layer. Called
        /// once per frame from MapLibreMap, after camera transforms have settled.
        /// Visibility, minzoom and maxzoom are honoured here so user code does
        /// not need to re-check them.
        /// </summary>
        public void RenderCustomLayers(MapLibreMap map,
            IReadOnlyDictionary<string, ICustomLayer> customLayers,
            UnityEngine.Camera camera, float zoom)
        {
            if (customLayers == null || customLayers.Count == 0) return;
            if (_style?.Layers == null) return;

            // Iterate Style.Layers (not the dictionary) so render happens in the
            // user-defined order -- the same order used by every other renderer.
            foreach (var def in _style.Layers)
            {
                if (def.Type != LayerType.Custom) continue;
                if (!def.IsVisibleAtZoom(zoom)) continue;
                if (!customLayers.TryGetValue(def.Id, out var layer)) continue;

                try { layer.Render(map, camera); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[MapRenderer] ICustomLayer.Render threw for '{def.Id}': {e}");
                }
            }
        }

        /// <summary>
        /// Re-apply the render order (z-index) for a layer. Called by MapLibreMap.MoveLayer.
        /// Dispatches to whichever renderer owns the layer id.
        /// </summary>
        public void SetLayerOrder(string layerId, int order)
        {
            if (_vectorRenderers.TryGetValue(layerId, out var vr)) vr.SetLayerOrder(order);
            if (_circleRenderers.TryGetValue(layerId, out var cr)) cr.SetLayerOrder(order);
            if (_fillExtrusionRenderers.TryGetValue(layerId, out var fer)) fer.SetLayerOrder(order);
            if (_heatmapRenderers.TryGetValue(layerId, out var hr)) hr.SetLayerOrder(order);
            if (_hillshadeRenderers.TryGetValue(layerId, out var hsr)) hsr.SetLayerOrder(order);
            if (_imageRenderers.TryGetValue(layerId, out var ir)) ir.SetLayerOrder(order);
            if (_symbolRenderers.TryGetValue(layerId, out var sr)) sr.SetLayerOrder(order);
        }

        internal RasterTileRenderer GetRasterRenderer(string layerId)
        {
            return _rasterRenderers.TryGetValue(layerId, out var renderer) ? renderer : null;
        }

        internal ImageRenderer GetImageRenderer(string layerId)
        {
            return _imageRenderers.TryGetValue(layerId, out var renderer) ? renderer : null;
        }

        /// <summary>True when the layer is an image source rendered as a single quad.</summary>
        internal bool IsImageLayer(string layerId) => _imageRenderers.ContainsKey(layerId);

        internal TerrainTileRenderer GetTerrainRenderer(string layerId)
        {
            return _terrainRenderers.TryGetValue(layerId, out var renderer) ? renderer : null;
        }

        /// <summary>
        /// True when the layer's raster tiles are drawn via the terrain (3D displacement)
        /// renderer rather than the flat raster renderer.
        /// </summary>
        internal bool IsTerrainRasterLayer(string layerId) => _terrainRenderers.ContainsKey(layerId);

        /// <summary>Apply a new light definition to every FillExtrusionRenderer.</summary>
        public void UpdateLight(LightDefinition light)
        {
            if (_style != null) _style.Light = light;
            foreach (var kvp in _fillExtrusionRenderers)
                kvp.Value.SetLight(light);
        }

        /// <summary>Update the style sky. Pass null to hide it.</summary>
        public void UpdateSky(SkyDefinition sky)
        {
            if (_style != null) _style.Sky = sky;
            if (_skyRenderer == null) return;
            _skyRenderer.SetSky(sky);
        }

        internal VectorTileRenderer GetVectorRenderer(string layerId)
        {
            return _vectorRenderers.TryGetValue(layerId, out var renderer) ? renderer : null;
        }

        internal SymbolRenderer GetSymbolRenderer(string layerId)
        {
            return _symbolRenderers.TryGetValue(layerId, out var renderer) ? renderer : null;
        }

        internal CircleRenderer GetCircleRenderer(string layerId)
        {
            return _circleRenderers.TryGetValue(layerId, out var renderer) ? renderer : null;
        }

        internal FillExtrusionRenderer GetFillExtrusionRenderer(string layerId)
        {
            return _fillExtrusionRenderers.TryGetValue(layerId, out var renderer) ? renderer : null;
        }

        internal HeatmapRenderer GetHeatmapRenderer(string layerId)
        {
            return _heatmapRenderers.TryGetValue(layerId, out var renderer) ? renderer : null;
        }

        public HillshadeRenderer GetHillshadeRenderer(string layerId)
        {
            return _hillshadeRenderers.TryGetValue(layerId, out var renderer) ? renderer : null;
        }

        public IReadOnlyList<string> VectorLayerIds => _vectorLayerIds;
        public IReadOnlyList<string> SymbolLayerIds => _symbolLayerIds;
        public IReadOnlyList<string> CircleLayerIds => _circleLayerIds;
        public IReadOnlyList<string> FillExtrusionLayerIds => _fillExtrusionLayerIds;
        public IReadOnlyList<string> HeatmapLayerIds => _heatmapLayerIds;
        public IReadOnlyList<string> HillshadeLayerIds => _hillshadeLayerIds;

        /// <summary>
        /// Call once per frame from the main-thread Update.
        /// Promotes mesh data computed on background threads into Unity objects.
        /// </summary>
        public void ProcessMeshQueues()
        {
            foreach (var kvp in _vectorRenderers)
                kvp.Value.ProcessMeshQueue();
            foreach (var kvp in _circleRenderers)
                kvp.Value.ProcessMeshQueue();
            foreach (var kvp in _fillExtrusionRenderers)
                kvp.Value.ProcessMeshQueue();
            foreach (var kvp in _heatmapRenderers)
                kvp.Value.ProcessMeshQueue();
        }

        public void UpdateMapState(MercatorCoordinate mapCenter, float zoom, float farClipPlane = 0f,
            float frustumHeight = 0f, float bearingDeg = 0f, float pitchDeg = 0f)
        {
            foreach (var kvp in _rasterRenderers)
                kvp.Value.UpdateAllPositions(mapCenter, zoom);
            foreach (var kvp in _imageRenderers)
                kvp.Value.UpdatePosition(mapCenter, zoom);
            foreach (var kvp in _terrainRenderers)
                kvp.Value.UpdateAllPositions(mapCenter, zoom);
            foreach (var kvp in _vectorRenderers)
                kvp.Value.UpdateAllPositions(mapCenter, zoom, frustumHeight, bearingDeg);
            foreach (var kvp in _symbolRenderers)
                kvp.Value.UpdateAllPositions(mapCenter, zoom, frustumHeight, bearingDeg, pitchDeg);
            foreach (var kvp in _circleRenderers)
                kvp.Value.UpdateAllPositions(mapCenter, zoom, frustumHeight);
            foreach (var kvp in _fillExtrusionRenderers)
                kvp.Value.UpdateAllPositions(mapCenter, zoom, frustumHeight, bearingDeg);
            foreach (var kvp in _heatmapRenderers)
                kvp.Value.UpdateAllPositions(mapCenter, zoom, frustumHeight);
            foreach (var kvp in _hillshadeRenderers)
                kvp.Value.UpdateAllPositions(mapCenter, zoom);

            // Keep background plane large enough to fill the camera view
            if (_backgroundRenderer != null && farClipPlane > 0f)
            {
                float size = farClipPlane * 2f;
                _backgroundRenderer.UpdateSize(size, size);
            }

            // Update zoom-dependent background color / pattern
            if (_backgroundRenderer != null && _backgroundPaint != null)
            {
                float opacity = _backgroundPaint.ResolveOpacity(zoom);
                string patternName = _backgroundPaint.ResolveBackgroundPattern(zoom);

                if (!string.IsNullOrEmpty(patternName))
                {
                    if (!_backgroundRenderer.UpdatePattern(patternName, opacity))
                    {
                        // Sprite atlas missing the entry -- fall back to color so
                        // the layer keeps drawing something instead of going blank.
                        _backgroundRenderer.UpdatePattern(null, opacity);
                        _backgroundRenderer.UpdateColor(
                            _backgroundPaint.ResolveBackgroundColor(zoom), opacity);
                    }
                    else if (frustumHeight > 0f)
                    {
                        // CSS pixel → world unit, derived from the camera frustum.
                        float pxToWorld = frustumHeight / Mathf.Max(Screen.height, 480f);
                        _backgroundRenderer.UpdatePatternScale(pxToWorld, opacity);
                    }
                }
                else
                {
                    _backgroundRenderer.UpdatePattern(null, opacity);
                    _backgroundRenderer.UpdateColor(
                        _backgroundPaint.ResolveBackgroundColor(zoom), opacity);
                }
            }
        }

        /// <summary>
        /// Check if a layer should be visible at the given zoom level.
        /// Used by MapLibreMap to skip tile requests for invisible layers.
        /// </summary>
        public bool IsLayerVisibleAtZoom(string layerId, float zoom)
        {
            if (_style == null) return true;
            foreach (var layer in _style.Layers)
            {
                if (layer.Id == layerId)
                    return layer.IsVisibleAtZoom(zoom);
            }
            return true;
        }

        /// <summary>
        /// Re-parse and apply background paint properties.
        /// Called when setPaintProperty changes a background-* property.
        /// </summary>
        public void UpdateBackgroundPaint(Dictionary<string, object> paint)
        {
            _backgroundPaint = StyleParser.ParseBackgroundPaint(paint);
            if (_backgroundRenderer != null)
            {
                _backgroundRenderer.UpdateColor(
                    _backgroundPaint.ResolveBackgroundColor(0f),
                    _backgroundPaint.ResolveOpacity(0f));
            }
        }

        public void Dispose()
        {
            _backgroundRenderer?.Dispose();
            _skyRenderer?.Dispose();
            _skyRenderer = null;
            foreach (var kvp in _rasterRenderers)
                kvp.Value.Clear();
            _rasterRenderers.Clear();
            foreach (var kvp in _imageRenderers)
                kvp.Value.Dispose();
            _imageRenderers.Clear();
            foreach (var kvp in _terrainRenderers)
                kvp.Value.Dispose();
            _terrainRenderers.Clear();
            foreach (var kvp in _vectorRenderers)
                kvp.Value.Dispose();
            _vectorRenderers.Clear();
            _vectorLayerIds.Clear();
            foreach (var kvp in _symbolRenderers)
                kvp.Value.Dispose();
            _symbolRenderers.Clear();
            _symbolLayerIds.Clear();
            foreach (var kvp in _circleRenderers)
                kvp.Value.Dispose();
            _circleRenderers.Clear();
            _circleLayerIds.Clear();
            foreach (var kvp in _fillExtrusionRenderers)
                kvp.Value.Dispose();
            _fillExtrusionRenderers.Clear();
            _fillExtrusionLayerIds.Clear();
            foreach (var kvp in _heatmapRenderers)
                kvp.Value.Dispose();
            _heatmapRenderers.Clear();
            _heatmapLayerIds.Clear();
            foreach (var kvp in _hillshadeRenderers)
                kvp.Value.Dispose();
            _hillshadeRenderers.Clear();
            _hillshadeLayerIds.Clear();
            _allTileRenderers.Clear();
        }
    }
}
