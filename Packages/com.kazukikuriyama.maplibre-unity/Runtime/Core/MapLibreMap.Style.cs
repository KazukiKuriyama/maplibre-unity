using System;
using System.Collections.Generic;
using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using MapLibre.Unity.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace MapLibre.Unity
{
    /// <summary>
    /// Style mutation surface -- SetStyle / SetStyleJson, sprite atlas + glyph
    /// management, runtime image registry, light / sky / terrain mutators, and
    /// the marker / popup composition layer. The bulk of the work happens in
    /// <see cref="ApplyStyleJsonInternalAsync"/>, which tears down sources /
    /// renderers / pools and rebuilds them from the new style JSON.
    /// </summary>
    public partial class MapLibreMap
    {
        /// <summary>
        /// Replace the map's style. Matches map.setStyle() in MapLibre GL JS.
        /// All sources, layers, and tile data are cleared and replaced.
        /// Camera position (center, zoom, bearing, pitch) is preserved.
        /// </summary>
        /// <param name="styleUrl">URL to a MapLibre Style JSON.</param>
        public void SetStyle(string styleUrl) => _ = SetStyleAsync(styleUrl);

        /// <summary>
        /// Replace the map's style using a JSON string.
        /// </summary>
        /// <param name="json">MapLibre Style JSON string.</param>
        public void SetStyleJson(string json) => _ = SetStyleJsonAsync(json);

        private async Awaitable ApplyStyleJsonInternalAsync(string json)
        {
            // 1. Tear down current state
            _isInitialized = false;

            // Stop tile lifecycle
            _tileManager.Clear();

            // Dispose all sources
            foreach (var source in _rasterSources.Values)
                source.Dispose();
            foreach (var source in _vectorSources.Values)
                source.Dispose();
            foreach (var source in _geoJsonSources.Values)
                source.Dispose();
            foreach (var source in _imageSources.Values)
                source.Dispose();
            _rasterSources.Clear();
            _vectorSources.Clear();
            _geoJsonSources.Clear();
            _imageSources.Clear();

            // Dispose renderers and tile pool
            _mapRenderer?.Dispose();
            _mapRenderer = null;
            _tilePool?.Clear();
            _tilePool = null;

            // Destroy per-style materials
            if (_iconMaterial != null)
            {
                Destroy(_iconMaterial);
                _iconMaterial = null;
            }
            _spriteAtlas = null;

            // Clear attributions
            _controlsOverlay?.ClearAttributions();
            _pendingAttributions.Clear();

            // Reset effective zoom range
            _effectiveMinZoom = 22;
            _effectiveMaxZoom = 0;

            // 2. Parse new style
            Style = StyleParser.Parse(json);
            FireEvent(MapEventType.StyleData);

            // 3. Load sprite atlas if defined
            if (!string.IsNullOrEmpty(Style.Sprite))
            {
                try
                {
                    _spriteAtlas = await SpriteLoader.LoadAsync(Style.Sprite, TransformRequest);
                    Debug.Log($"[MapLibreMap] Sprite atlas loaded ({_spriteAtlas.Texture.width}x{_spriteAtlas.Texture.height})");
                }
                catch (SpriteLoadException e)
                {
                    Debug.LogWarning($"[MapLibreMap] Sprite load failed: {e.Message}");
                }
            }

            // Always ensure a SpriteAtlas exists so runtime addImage() works.
            if (_spriteAtlas == null)
                _spriteAtlas = new SpriteAtlas(null, null);

            // (Re)create the glyph source if the new style declared one.
            // SetStyle replaces everything, so old glyph state is dropped.
            DetachGlyphRangeListener();
            _glyphSource = null;
            if (!string.IsNullOrEmpty(Style.Glyphs))
            {
                _glyphSource = new GlyphSource();
                _glyphSource.Initialize(Style.Glyphs, this, TransformRequest);
                AttachGlyphRangeListener();
            }

            if (_iconMaterial == null && _iconShader != null)
            {
                _iconMaterial = new Material(_iconShader);
                _iconMaterial.renderQueue = 3002;
            }

            // 4. Initialize sources
            foreach (var kvp in Style.Sources)
            {
                bool hasTileJsonAttribution = false;
                string sourceAttribution = kvp.Value.Attribution;

                if (kvp.Value.Type == SourceType.Raster)
                {
                    var source = new RasterTileSource();
                    source.Initialize(kvp.Key, kvp.Value, this, _tileCacheSize, TransformRequest);
                    _rasterSources[kvp.Key] = source;

                    if (!string.IsNullOrEmpty(kvp.Value.Url) && !source.HasTileUrls)
                    {
                        try
                        {
                            var data = await TileJSONFetcher.FetchAsync(kvp.Value.Url, TransformRequest);
                            source.SetTileUrls(data.Tiles);
                            kvp.Value.MinZoom = data.MinZoom;
                            kvp.Value.MaxZoom = data.MaxZoom;
                            if (!string.IsNullOrEmpty(data.Attribution))
                            {
                                _pendingAttributions.Add(data.Attribution);
                                hasTileJsonAttribution = true;
                            }
                        }
                        catch (Exception e)
                        {
                            FireError($"TileJSON fetch failed for '{kvp.Key}': {e.Message}", kvp.Key);
                        }
                    }
                }
                else if (kvp.Value.Type == SourceType.Vector)
                {
                    var source = new VectorTileSource();
                    source.Initialize(kvp.Key, kvp.Value, this, _tileCacheSize, TransformRequest);
                    _vectorSources[kvp.Key] = source;

                    if (!string.IsNullOrEmpty(kvp.Value.Url) && !source.HasTileUrls)
                    {
                        try
                        {
                            var data = await TileJSONFetcher.FetchAsync(kvp.Value.Url, TransformRequest);
                            source.SetTileUrls(data.Tiles);
                            kvp.Value.MinZoom = data.MinZoom;
                            kvp.Value.MaxZoom = data.MaxZoom;
                            if (!string.IsNullOrEmpty(data.Attribution))
                            {
                                _pendingAttributions.Add(data.Attribution);
                                hasTileJsonAttribution = true;
                            }
                        }
                        catch (Exception e)
                        {
                            FireError($"TileJSON fetch failed for vector source '{kvp.Key}': {e.Message}", kvp.Key);
                        }
                    }
                }
                else if (kvp.Value.Type == SourceType.GeoJson)
                {
                    var source = new GeoJsonSource();
                    source.Initialize(kvp.Key, kvp.Value, this, _tileCacheSize, TransformRequest);
                    _geoJsonSources[kvp.Key] = source;
                    await source.LoadDataAsync();
                }
                else if (kvp.Value.Type == SourceType.Image)
                {
                    var source = new ImageSource();
                    source.Initialize(kvp.Key, kvp.Value, this, TransformRequest);
                    _imageSources[kvp.Key] = source;

                    if (!string.IsNullOrEmpty(kvp.Value.Url))
                        await source.LoadFromUrlAsync();
                }

                if (!hasTileJsonAttribution && !string.IsNullOrEmpty(sourceAttribution))
                    _pendingAttributions.Add(sourceAttribution);
            }

            // Recompute effective min/max zoom
            foreach (var kvp in Style.Sources)
            {
                _effectiveMinZoom = Math.Min(_effectiveMinZoom, kvp.Value.MinZoom);
                _effectiveMaxZoom = Math.Max(_effectiveMaxZoom, kvp.Value.MaxZoom);
            }

            // 5. Initialize rendering
            _terrainManager = null;
            if (Style.Terrain != null && !string.IsNullOrEmpty(Style.Terrain.Source) &&
                Style.Sources.TryGetValue(Style.Terrain.Source, out var demSrc2) &&
                demSrc2.Type == SourceType.RasterDem)
            {
                _terrainManager = new Terrain.TerrainManager();
                _terrainManager.Initialize(Style.Terrain.Source, demSrc2.Encoding, demSrc2.MaxZoom);
                _terrainManager.Exaggeration =
                    Style.Terrain.ResolveExaggeration(new Expressions.EvaluationContext(State.Zoom));
            }

            _tilePool = new Pool.TileObjectPool(transform, _rasterTileMaterial, _tilePoolInitialSize);
            _mapRenderer = new Rendering.MapRenderer();
            _mapRenderer.Initialize(transform, Style, _tilePool, _backgroundShader, _fillShader, _lineShader,
                _circleShader, _fillExtrusionShader, _heatmapShader, _hillshadeShader,
                _terrainShader, _terrainManager,
                _symbolFont, _spriteAtlas, _iconMaterial,
                _rasterTileShader, ResolveSourceType, _fillPatternShader, _linePatternShader,
                _symbolFontFallbacks, _fillExtrusionPatternShader, _skyShader, _lineGradientShader,
                ResolveSourceLineMetrics, _backgroundPatternShader);
            _mapRenderer.SetFeatureStateStore(this);
            // Wire SDF text path. The shader is loaded lazily -- bundles that
            // don't include MapLibreSdfText.shader simply fall back to TMP.
            _mapRenderer.SetGlyphSource(_glyphSource, Shader.Find("MapLibre/SdfText"));

            // Wire up image sources to their renderers
            foreach (var layer in Style.Layers)
            {
                if (layer.Type == LayerType.Raster && !string.IsNullOrEmpty(layer.Source)
                    && _imageSources.TryGetValue(layer.Source, out var imageSource))
                {
                    _mapRenderer.GetImageRenderer(layer.Id)?.SetSource(imageSource);
                }
            }

            // 6. Apply pending attributions
            foreach (var attr in _pendingAttributions)
                _controlsOverlay?.AddAttribution(attr);
            _pendingAttributions.Clear();

            // 7. Resume
            _isInitialized = true;
            _needsTileUpdate = true;

            FireEvent(MapEventType.Load);
            Debug.Log($"[MapLibreMap] Style switched: {Style.Name ?? "(unnamed)"}");
        }

        // === Runtime image API (matches MapLibre GL JS map.addImage / removeImage / etc.) ===
        // Runtime images are drawn by Symbol layers that reference them by name in
        // icon-image. They take precedence over same-named atlas entries.

        /// <summary>
        /// Register a texture under <paramref name="id"/> so Symbol layers can
        /// reference it via icon-image. Matches map.addImage() in MapLibre GL JS.
        /// The caller retains ownership of the texture. Call RefreshTiles() after
        /// adding an image if existing tiles should re-query the icon.
        /// </summary>
        public void AddImage(string id, Texture2D texture, ImageOptions options = default)
        {
            if (_spriteAtlas == null)
            {
                Debug.LogWarning("[MapLibreMap] AddImage: map not initialized yet");
                return;
            }
            _spriteAtlas.AddImage(id, texture, options);
        }

        /// <summary>Replace an existing runtime image. Matches map.updateImage().</summary>
        public bool UpdateImage(string id, Texture2D texture)
            => _spriteAtlas != null && _spriteAtlas.UpdateImage(id, texture);

        /// <summary>Remove a runtime image. Matches map.removeImage().</summary>
        public bool RemoveImage(string id)
            => _spriteAtlas != null && _spriteAtlas.RemoveImage(id);

        /// <summary>Check whether an icon exists (atlas or runtime). Matches map.hasImage().</summary>
        public bool HasImage(string id)
            => _spriteAtlas != null && _spriteAtlas.HasIcon(id);

        /// <summary>Return all known icon names. Matches map.listImages().</summary>
        public List<string> ListImages()
            => _spriteAtlas != null ? _spriteAtlas.ListImages() : new List<string>();

        // === Sprite / Glyphs runtime API ===

        /// <summary>
        /// Replace the active sprite atlas at runtime by fetching a new sprite URL
        /// (PNG + JSON pair, with optional @2x). Existing renderers continue to use the
        /// same SpriteAtlas instance; the atlas is mutated in place once loading
        /// finishes, so previously-added runtime images are preserved.
        /// Pass null/empty to clear the bundled atlas (runtime images remain).
        /// Matches map.setSprite() in MapLibre GL JS.
        /// </summary>
        public void SetSprite(string spriteUrl,
            Action onComplete = null, Action<string> onError = null)
        {
            if (_spriteAtlas == null) _spriteAtlas = new SpriteAtlas(null, null);
            if (Style != null) Style.Sprite = spriteUrl;

            if (string.IsNullOrEmpty(spriteUrl))
            {
                _spriteAtlas.Replace(null, null);
                onComplete?.Invoke();
                return;
            }

            _ = SetSpriteAsyncImpl(spriteUrl, onComplete, onError);
        }

        private async Awaitable SetSpriteAsyncImpl(string spriteUrl,
            Action onComplete, Action<string> onError)
        {
            try
            {
                var atlas = await SpriteLoader.LoadAsync(spriteUrl, TransformRequest);
                _spriteAtlas.Replace(atlas.Texture, atlas.RawEntries);
                onComplete?.Invoke();
            }
            catch (SpriteLoadException e)
            {
                Debug.LogWarning($"[MapLibreMap] SetSprite failed: {e.Message}");
                onError?.Invoke(e.Message);
            }
        }

        /// <summary>
        /// Update the style's glyphs URL. When set to a non-empty URL, a
        /// <see cref="GlyphSource"/> is lazily created so callers can render
        /// SDF text directly from MapLibre-format glyph PBFs (see
        /// <see cref="GetGlyphSource"/>). Pass null/empty to clear and fall
        /// back to the TMP path. Matches map.setGlyphs().
        /// </summary>
        public void SetGlyphs(string glyphsUrl)
        {
            if (Style != null) Style.Glyphs = glyphsUrl;
            DetachGlyphRangeListener();
            if (string.IsNullOrEmpty(glyphsUrl))
            {
                _glyphSource = null;
                _mapRenderer?.SetGlyphSource(null, null);
                return;
            }
            if (_glyphSource == null)
                _glyphSource = new GlyphSource();
            _glyphSource.Initialize(glyphsUrl, this, TransformRequest);
            AttachGlyphRangeListener();
            _mapRenderer?.SetGlyphSource(_glyphSource, Shader.Find("MapLibre/SdfText"));
        }

        // SDF labels render with a null mesh on first dispatch when glyph PBFs
        // are still in flight. The atlas is populated asynchronously, so we
        // need a hook that re-fires tile dispatch once new glyphs land --
        // otherwise SdfLabel(partial) GameObjects stay empty forever.
        private void AttachGlyphRangeListener()
        {
            if (_glyphSource == null) return;
            _glyphSource.OnAnyRangeLoaded += OnGlyphRangeLoaded;
        }

        private void DetachGlyphRangeListener()
        {
            if (_glyphSource == null) return;
            _glyphSource.OnAnyRangeLoaded -= OnGlyphRangeLoaded;
        }

        // Coalesce multiple range-loaded notifications into a single refresh
        // and ensure the refresh runs *after* the current dispatch cycle has
        // unwound. Without this, a disk-cache hit completes the FetchRange
        // coroutine synchronously inside SymbolRenderer.ShowTile, so the
        // RefreshTiles → TileManager.Clear → HideTile chain would run
        // mid-ShowTile and corrupt _requestedTiles / _activeTileObjects state.
        private bool _glyphRefreshPending;

        private void OnGlyphRangeLoaded()
        {
            if (!_isInitialized) return;
            if (_glyphRefreshPending) return;
            _glyphRefreshPending = true;
        }

        // Drained from Update on the frame after a glyph range loaded. Driving
        // the deferral through the existing Update tick (rather than a fresh
        // coroutine) avoids races with scene unload and keeps the work on the
        // main thread.
        private void DrainPendingGlyphRefresh()
        {
            if (!_glyphRefreshPending) return;
            _glyphRefreshPending = false;
            RefreshTiles();
        }

        /// <summary>
        /// Returns the active <see cref="GlyphSource"/> (or null when style.glyphs
        /// is unset). Custom layers can use this to lay out text via
        /// <c>SdfTextMeshBuilder</c> while the built-in <c>SymbolRenderer</c>
        /// continues to use TMP.
        /// </summary>
        public GlyphSource GetGlyphSource() => _glyphSource;

        /// <summary>Returns the current style sprite URL (or null).</summary>
        public string GetSprite() => Style?.Sprite;

        /// <summary>Returns the current style glyphs URL (or null).</summary>
        public string GetGlyphs() => Style?.Glyphs;

        /// <summary>
        /// Asynchronously fetch an image from a URL and pass it to <paramref name="onSuccess"/>
        /// as a <see cref="Texture2D"/>. Matches map.loadImage(url, callback) in MapLibre GL JS.
        /// The texture ownership transfers to the caller -- pair with AddImage(id, texture)
        /// to register it for use in icon-image, or destroy it when finished.
        ///
        /// The request is routed through <see cref="TransformRequest"/> so auth headers and
        /// URL rewrites apply, exactly like style/source/tile fetches.
        /// </summary>
        public void LoadImage(string url, Action<Texture2D> onSuccess,
            Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(url))
            {
                onError?.Invoke("LoadImage: url is null or empty");
                return;
            }
            _ = LoadImageInternalAsync(url, onSuccess, onError);
        }

        private async Awaitable LoadImageInternalAsync(string url, Action<Texture2D> onSuccess,
            Action<string> onError)
        {
            var transformed = RequestTransformer.Apply(TransformRequest, url, ResourceKind.Image);
            if (transformed.Abort)
            {
                onError?.Invoke("LoadImage: aborted by transformRequest");
                return;
            }

            using var request = UnityWebRequestTexture.GetTexture(transformed.Url, true);
            request.SetRequestHeader("User-Agent", "MapLibre-Unity/0.1");
            if (transformed.Headers != null)
            {
                foreach (var kvp in transformed.Headers)
                {
                    if (!string.IsNullOrEmpty(kvp.Key))
                        request.SetRequestHeader(kvp.Key, kvp.Value ?? string.Empty);
                }
            }
            await request.SendAsync();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var texture = DownloadHandlerTexture.GetContent(request);
                texture.wrapMode = TextureWrapMode.Clamp;
                onSuccess?.Invoke(texture);
            }
            else
            {
                onError?.Invoke($"{url}: {request.error}");
            }
        }

        // === Terrain runtime API matching MapLibre GL JS ===

        /// <summary>
        /// Sample terrain elevation in meters at the given geographic coordinate.
        /// Returns NaN when no terrain source is configured or no DEM tile is loaded
        /// for the location. Matches map.queryTerrainElevation().
        /// </summary>
        public float QueryTerrainElevation(LngLat lngLat)
        {
            if (_terrainManager == null) return float.NaN;
            var merc = CoordinateConversion.LngLatToMercator(lngLat);
            return _terrainManager.QueryElevationMeters(merc);
        }

        /// <summary>
        /// Currently active terrain definition (from style.terrain). Returns null when
        /// no terrain is configured. Matches map.getTerrain().
        /// </summary>
        public TerrainDefinition GetTerrain() => Style?.Terrain;

        /// <summary>
        /// Update the terrain exaggeration multiplier at runtime. 0 = flat, 1 = real
        /// scale. Matches programmatic update of map.terrain.exaggeration in GL JS.
        /// </summary>
        public void SetTerrainExaggeration(float exaggeration)
        {
            if (_terrainManager == null) return;
            _terrainManager.Exaggeration = exaggeration;
        }

        /// <summary>
        /// Returns the current exaggeration value, or 1f when no terrain is active.
        /// </summary>
        public float GetTerrainExaggeration()
            => _terrainManager?.Exaggeration ?? 1f;

        /// <summary>
        /// Enable, disable, or update terrain at runtime. Matches map.setTerrain() in
        /// MapLibre GL JS. Pass null to disable terrain (raster layers fall back to a
        /// flat draping). Pass a definition with a valid raster-dem source to enable.
        ///
        /// Note: switching terrain ON/OFF after Start() requires the renderer pipeline
        /// to be rebuilt. The implementation re-applies the current style via
        /// <see cref="SetStyleJson"/>, which causes a brief reflow. If you only need to
        /// change the exaggeration on already-active terrain, prefer
        /// <see cref="SetTerrainExaggeration"/> for a cheaper update.
        /// </summary>
        public void SetTerrain(TerrainDefinition definition)
        {
            if (Style == null)
            {
                Debug.LogWarning("[MapLibreMap] SetTerrain: style not loaded yet");
                return;
            }

            // Cheap path: terrain already running, only the exaggeration / source matches → in-place update.
            if (_terrainManager != null && definition != null
                && _terrainManager.SourceId == definition.Source)
            {
                Style.Terrain = definition;
                _terrainManager.Exaggeration =
                    definition.ResolveExaggeration(new Expressions.EvaluationContext(State.Zoom));
                return;
            }

            // Otherwise we need to rebuild renderers. Easiest path: poke the style.
            Style.Terrain = definition;
            // Re-serialize the style and re-apply. SetStyleJson teardown handles
            // _terrainManager, _mapRenderer, raster routing etc. uniformly.
            try
            {
                var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(StyleToJObject(Style));
                SetStyleJson(serialized);
            }
            catch (Exception e)
            {
                FireError($"SetTerrain rebuild failed: {e.Message}");
            }
        }

        /// <summary>
        /// Best-effort serialization of the in-memory style back to a JObject the parser
        /// can consume. Only fields the parser reads round-trip; any unknown extensions
        /// in the original JSON are lost (acceptable for SetTerrain since terrain is the
        /// only path that needs this).
        /// </summary>
        private static JObject StyleToJObject(MapLibreStyle s)
        {
            var root = new JObject
            {
                ["version"] = s.Version,
                ["name"] = s.Name,
                ["sprite"] = s.Sprite,
                ["glyphs"] = s.Glyphs,
            };
            if (s.Center.HasValue)
                root["center"] = new JArray(s.Center.Value.Longitude, s.Center.Value.Latitude);
            if (s.Zoom.HasValue) root["zoom"] = s.Zoom.Value;
            if (s.Bearing.HasValue) root["bearing"] = s.Bearing.Value;
            if (s.Pitch.HasValue) root["pitch"] = s.Pitch.Value;

            var sources = new JObject();
            foreach (var kv in s.Sources)
                sources[kv.Key] = SourceDefinitionToJObject(kv.Value);
            root["sources"] = sources;

            var layers = new JArray();
            foreach (var layer in s.Layers)
                layers.Add(LayerDefinitionToJObject(layer));
            root["layers"] = layers;

            if (s.Terrain != null)
            {
                var t = new JObject { ["source"] = s.Terrain.Source };
                root["terrain"] = t;
            }
            return root;
        }

        private static JObject SourceDefinitionToJObject(SourceDefinition d)
        {
            string typeStr = d.Type switch
            {
                SourceType.Vector => "vector",
                SourceType.Raster => "raster",
                SourceType.RasterDem => "raster-dem",
                SourceType.GeoJson => "geojson",
                SourceType.Image => "image",
                SourceType.Video => "video",
                _ => "raster",
            };
            var o = new JObject
            {
                ["type"] = typeStr,
                ["minzoom"] = d.MinZoom,
                ["maxzoom"] = d.MaxZoom,
                ["tileSize"] = d.TileSize,
                ["scheme"] = d.Scheme,
                ["encoding"] = d.Encoding,
            };
            if (!string.IsNullOrEmpty(d.Url)) o["url"] = d.Url;
            if (!string.IsNullOrEmpty(d.Attribution)) o["attribution"] = d.Attribution;
            if (d.Tiles != null && d.Tiles.Count > 0) o["tiles"] = new JArray(d.Tiles);
            if (d.Bounds != null && d.Bounds.Length == 4)
                o["bounds"] = new JArray(d.Bounds[0], d.Bounds[1], d.Bounds[2], d.Bounds[3]);
            if (d.Data != null) o["data"] = d.Data;
            if (d.Cluster) o["cluster"] = true;
            if (d.ClusterRadius != 50) o["clusterRadius"] = d.ClusterRadius;
            if (d.ClusterMaxZoom >= 0) o["clusterMaxZoom"] = d.ClusterMaxZoom;
            if (d.ClusterMinPoints != 2) o["clusterMinPoints"] = d.ClusterMinPoints;
            return o;
        }

        private static JObject LayerDefinitionToJObject(LayerDefinition l)
        {
            string typeStr = l.Type switch
            {
                LayerType.Background => "background",
                LayerType.Fill => "fill",
                LayerType.Line => "line",
                LayerType.Symbol => "symbol",
                LayerType.Raster => "raster",
                LayerType.Circle => "circle",
                LayerType.FillExtrusion => "fill-extrusion",
                LayerType.Heatmap => "heatmap",
                LayerType.Hillshade => "hillshade",
                _ => "background",
            };
            var o = new JObject
            {
                ["id"] = l.Id,
                ["type"] = typeStr,
            };
            if (!string.IsNullOrEmpty(l.Source)) o["source"] = l.Source;
            if (!string.IsNullOrEmpty(l.SourceLayer)) o["source-layer"] = l.SourceLayer;
            if (l.MinZoom.HasValue) o["minzoom"] = l.MinZoom.Value;
            if (l.MaxZoom.HasValue) o["maxzoom"] = l.MaxZoom.Value;
            if (l.Paint != null) o["paint"] = JObject.FromObject(l.Paint);
            if (l.Layout != null) o["layout"] = JObject.FromObject(l.Layout);
            return o;
        }

        // === Style "light" runtime API matching MapLibre GL JS ===

        /// <summary>
        /// Returns the current style's light definition (or null when unset).
        /// Matches map.getLight() in MapLibre GL JS.
        /// </summary>
        public LightDefinition GetLight() => Style?.Light;

        /// <summary>
        /// Apply a new light definition. Affects fill-extrusion shading immediately --
        /// no tile reload is required. Pass null to revert to defaults (white, 0.5
        /// intensity, position [1.15, 210, 30]).
        /// Matches map.setLight() in MapLibre GL JS.
        /// </summary>
        public void SetLight(LightDefinition light)
        {
            if (Style != null) Style.Light = light;
            _mapRenderer?.UpdateLight(light);
        }

        // === Style "sky" runtime API matching MapLibre GL JS ===

        /// <summary>
        /// Returns the current style's sky definition (or null when unset).
        /// Matches map.getSky() in MapLibre GL JS.
        /// </summary>
        public SkyDefinition GetSky() => Style?.Sky;

        /// <summary>
        /// Apply a new sky definition. The screen-space sky gradient updates
        /// immediately -- tile reload is not required. Pass null to hide the sky
        /// (Unity's camera clear color takes over).
        /// Matches map.setSky() in MapLibre GL JS.
        /// </summary>
        public void SetSky(SkyDefinition sky)
        {
            if (Style != null) Style.Sky = sky;
            _mapRenderer?.UpdateSky(sky);
        }

        // === Marker / Popup API ===

        private MarkerManager GetOrCreateMarkerManager()
        {
            if (_markerManager != null) return _markerManager;

            var go = new GameObject("MapMarkers_UI");
            go.transform.SetParent(transform);
            go.AddComponent<UIDocument>();
            _markerManager = go.AddComponent<MarkerManager>();
            _markerManager.Initialize(this, _panelSettings);

            return _markerManager;
        }

        /// <summary>
        /// Add a marker to the map. Convenience wrapper for marker.addTo(map) in MapLibre GL JS.
        /// Logs a warning and no-ops if the map has not finished initializing yet --
        /// MarkerManager depends on the PanelSettings stand-up done during Start.
        /// </summary>
        public void AddMarker(Marker marker)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] AddMarker: map not initialized yet -- await LoadAsync() first.");
                return;
            }
            marker.AddTo(this);
            GetOrCreateMarkerManager().AddMarker(marker);
        }

        /// <summary>
        /// Remove a marker from the map. No-op when the map has not been
        /// initialized -- there is nothing to remove yet.
        /// </summary>
        public void RemoveMarker(Marker marker)
        {
            if (!_isInitialized) return;
            GetOrCreateMarkerManager().RemoveMarker(marker);
        }

        /// <summary>
        /// Add a standalone popup (not attached to a marker) to the map.
        /// Logs a warning and no-ops if the map has not finished initializing yet.
        /// </summary>
        public void AddPopup(Popup popup, bool startOpen = true)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] AddPopup: map not initialized yet -- await LoadAsync() first.");
                return;
            }
            popup.AddTo(this);
            GetOrCreateMarkerManager().AddPopup(popup, startOpen);
        }

        /// <summary>
        /// Remove a standalone popup from the map. No-op when the map has not
        /// been initialized.
        /// </summary>
        public void RemovePopup(Popup popup)
        {
            if (!_isInitialized) return;
            GetOrCreateMarkerManager().RemovePopup(popup);
        }
    }
}
