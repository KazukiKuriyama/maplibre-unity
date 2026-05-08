using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using MapLibre.Unity.CameraControl;
using MapLibre.Unity.Pool;
using MapLibre.Unity.Rendering;
using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using MapLibre.Unity.UI;
using MapLibre.Unity.VectorTile;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity
{
    /// <summary>
    /// Main component for displaying a MapLibre-compatible map in Unity.
    /// Add this to a GameObject, set StyleUrl or StyleJson, and press Play.
    ///
    /// <para>
    /// Implementation is split across partial files for navigability:
    /// <list type="bullet">
    /// <item><c>MapLibreMap.cs</c> -- fields, lifecycle, tile event plumbing.</item>
    /// <item><c>MapLibreMap.Async.cs</c> -- Awaitable counterparts of the public API.</item>
    /// <item><c>MapLibreMap.Events.cs</c> -- On / Off / Once + internal Fire helpers.</item>
    /// <item><c>MapLibreMap.Input.cs</c> -- pointer / touch / resize handling.</item>
    /// <item><c>MapLibreMap.Sources.cs</c> -- AddSource / RemoveSource / GetSource.</item>
    /// <item><c>MapLibreMap.Layers.cs</c> -- AddLayer / RemoveLayer / property getters and setters.</item>
    /// <item><c>MapLibreMap.Style.cs</c> -- SetStyle, sprite / glyph / image / light / sky / terrain.</item>
    /// <item><c>MapLibreMap.Camera.cs</c> -- Project / Unproject, EaseTo / FlyTo, free-camera bridge.</item>
    /// <item><c>MapLibreMap.FeatureState.cs</c> -- feature-state CRUD + IFeatureStateStore.</item>
    /// </list>
    /// </para>
    /// </summary>
    public partial class MapLibreMap : MonoBehaviour, Expressions.IFeatureStateStore
    {
        [Header("Style")]
        [SerializeField] private string _styleUrl;
        [SerializeField] private TextAsset _styleFile;
        [SerializeField] [TextArea(5, 20)] private string _styleJson;

        [Header("Initial View (overrides style JSON when set; NaN = use style)")]
        [SerializeField] private double _initialLongitude = double.NaN;
        [SerializeField] private double _initialLatitude = double.NaN;
        [SerializeField] private float _initialZoom = float.NaN;
        [SerializeField] private float _initialBearing = float.NaN;
        [SerializeField] private float _initialPitch = float.NaN;

        [Header("Performance")]
        [SerializeField] private int _tileCacheSize = 256;
        [SerializeField] private int _tilePoolInitialSize = 0;

        [Header("Disk cache")]
        [Tooltip("Persist tile responses on disk. Enables instant cold-start and saves bandwidth.")]
        [SerializeField] private bool _diskCacheEnabled = true;
        [Tooltip("Maximum disk cache size in MB. 0 disables size-based eviction.")]
        [SerializeField] private int _diskCacheMaxMB = 100;
        [Tooltip("Default time-to-live (hours) for responses without Cache-Control: max-age.")]
        [SerializeField] private int _diskCacheDefaultTtlHours = 24;

        [Header("References")]
        [SerializeField] private UnityEngine.Camera _mapCamera;
        [SerializeField, NotNull] private PanelSettings _panelSettings;

        // Built-in layer shaders are loaded via Shader.Find() in InitializeAsync().
        // Build inclusion is guaranteed by ProjectSettings/GraphicsSettings.asset
        // (m_AlwaysIncludedShaders); GraphicsSettingsShaderTests catches drift.
        // Not exposed in Inspector -- matches MapLibre GL JS where rendering
        // primitives are internal. Custom rendering goes through ICustomLayer.
        private Shader _rasterTileShader;
        private Shader _backgroundShader;
        private Shader _fillShader;
        private Shader _fillPatternShader;
        private Shader _lineShader;
        private Shader _linePatternShader;
        private Shader _lineGradientShader;
        private Shader _backgroundPatternShader;
        private Shader _circleShader;
        private Shader _fillExtrusionShader;
        private Shader _fillExtrusionPatternShader;
        private Shader _skyShader;
        private Shader _heatmapShader;
        private Shader _hillshadeShader;
        private Shader _terrainShader;
        private Shader _iconShader;

        [Header("Text")]
        [Tooltip("TMP_FontAsset for symbol labels. Assign a font that covers the required glyphs (e.g. Noto Sans JP for Japanese). Falls back to TMP default if unset.")]
        [SerializeField] private TMP_FontAsset _symbolFont;

        [Tooltip("Fallback fonts used when the primary font lacks a glyph (e.g. mixed Latin + CJK + emoji). Tried in order via TMP's fallbackFontAssetTable.")]
        [SerializeField] private TMP_FontAsset[] _symbolFontFallbacks;

        public MapState State { get; private set; }
        public MapLibreStyle Style { get; private set; }
        public MapAnimator Animator { get; private set; }

        /// <summary>
        /// Hook called for every HTTP request (style, sources, tiles, sprites, glyphs, images).
        /// Use this to inject auth tokens, rewrite URLs, or proxy through a CORS gateway.
        /// Matches transformRequest in MapLibre GL JS. Set before Start() takes effect for
        /// the initial style fetch; later assignments apply to subsequent requests.
        /// </summary>
        public RequestTransformFunction TransformRequest { get; set; }

        /// <summary>
        /// Whether the map has completed initialization.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        private readonly MapEventSystem _eventSystem = new();
        private MapRenderer _mapRenderer;
        private TileManager _tileManager;
        private readonly Dictionary<string, RasterTileSource> _rasterSources = new();
        private readonly Dictionary<string, VectorTileSource> _vectorSources = new();
        private readonly Dictionary<string, GeoJsonSource> _geoJsonSources = new();
        private readonly Dictionary<string, ImageSource> _imageSources = new();
        // User-supplied ICustomLayer instances. The corresponding LayerDefinition
        // (with Type = Custom) lives in Style.Layers so render order, MoveLayer,
        // and minzoom/maxzoom honour them like any other layer.
        private readonly Dictionary<string, ICustomLayer> _customLayers = new();
        private TileObjectPool _tilePool;
        private MapCamera _cameraController;
        private MapInputHandler _inputHandler;
        private MapControlsOverlay _controlsOverlay;
        private MarkerManager _markerManager;
        private Terrain.TerrainManager _terrainManager;
        private Material _rasterTileMaterial;
        private Material _iconMaterial;
        private SpriteAtlas _spriteAtlas;
        // Lazily created when style.glyphs is non-null. Holds the on-demand
        // glyph atlas backing self-rendered SDF text (an alternative to the
        // TMP path). null when the style has no glyphs URL -- TMP remains the
        // only text path in that case.
        private GlyphSource _glyphSource;

        private bool _isInitialized;
        private bool _needsTileUpdate;
        // Set whenever AddLayer is called with triggerRefresh:false. Update()
        // consumes the flag once per frame and runs RefreshTiles, so a batch
        // of AddLayer(false) calls in the same Start/coroutine still ends up
        // dispatching tiles to the new layers without the caller having to
        // remember a manual RefreshTiles() at the end. A user-issued
        // RefreshTiles clears the flag so the safety-net never double-fires.
        private bool _pendingLayerRefresh;
        private readonly List<string> _pendingAttributions = new();
        private int _effectiveMinZoom = 22;
        private int _effectiveMaxZoom = 0;

        // Click / pointer detection
        private Vector2 _mouseDownPos;
        private bool _mouseDownTracking;
        private const float ClickMaxDistance = 5f;
        private const float DblClickMaxInterval = 0.3f;
        private float _lastClickTime;
        private Vector2 _lastClickPos;

        // Mouse enter/leave tracking
        private bool _isMouseOverMap;

        // Touch tracking
        private readonly Dictionary<int, Vector2> _activeTouches = new();

        // Event tracking state
        private bool _isMoving;
        private bool _isZooming;
        private bool _isRotating;
        private bool _isPitching;
        private bool _stateChangedThisFrame;
        private StateChangeFlags _frameChangeFlags;
        private int _idleFrameCount;
        private const int IdleFrameThreshold = 2;

        // Drag tracking (left-button pan): dragstart on first move past threshold, dragend on release.
        private bool _isDragging;
        private const float DragStartThreshold = 3f;

        // Resize tracking
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private void Start()
        {
            // Apply disk cache settings before any source starts fetching.
            TileDiskCache.Enabled = _diskCacheEnabled;
            TileDiskCache.MaxBytes = _diskCacheMaxMB > 0 ? (long)_diskCacheMaxMB * 1024 * 1024 : 0;
            if (_diskCacheDefaultTtlHours > 0)
                TileDiskCache.DefaultMaxAgeSeconds = _diskCacheDefaultTtlHours * 3600;

            _ = InitializeAsync();
        }

        private async Awaitable InitializeAsync()
        {
            // Load shaders if not assigned
            if (_rasterTileShader == null)
                _rasterTileShader = Shader.Find("MapLibre/RasterTile");
            if (_backgroundShader == null)
                _backgroundShader = Shader.Find("MapLibre/Background");
            if (_fillShader == null)
                _fillShader = Shader.Find("MapLibre/Fill");
            if (_fillPatternShader == null)
                _fillPatternShader = Shader.Find("MapLibre/FillPattern");
            if (_linePatternShader == null)
                _linePatternShader = Shader.Find("MapLibre/LinePattern");
            if (_lineGradientShader == null)
                _lineGradientShader = Shader.Find("MapLibre/LineGradient");
            if (_backgroundPatternShader == null)
                _backgroundPatternShader = Shader.Find("MapLibre/BackgroundPattern");
            if (_lineShader == null)
                _lineShader = Shader.Find("MapLibre/Line");
            if (_circleShader == null)
                _circleShader = Shader.Find("MapLibre/Circle");
            if (_fillExtrusionShader == null)
                _fillExtrusionShader = Shader.Find("MapLibre/FillExtrusion");
            if (_fillExtrusionPatternShader == null)
                _fillExtrusionPatternShader = Shader.Find("MapLibre/FillExtrusionPattern");
            if (_skyShader == null)
                _skyShader = Shader.Find("MapLibre/Sky");
            if (_heatmapShader == null)
                _heatmapShader = Shader.Find("MapLibre/Heatmap");
            if (_hillshadeShader == null)
                _hillshadeShader = Shader.Find("MapLibre/Hillshade");
            if (_terrainShader == null)
                _terrainShader = Shader.Find("MapLibre/TerrainTile");
            if (_iconShader == null)
                _iconShader = Shader.Find("MapLibre/Icon");

            if (_rasterTileShader == null)
            {
                FireError("RasterTile shader not found.");
                return;
            }

            _rasterTileMaterial = new Material(_rasterTileShader);

            // 1. Parse or fetch style
            // Priority: StyleFile > StyleJson > StyleUrl
            string json = _styleFile != null ? _styleFile.text : _styleJson;
            if (string.IsNullOrEmpty(json) && !string.IsNullOrEmpty(_styleUrl))
            {
                try { json = await FetchTextAsync(_styleUrl); }
                catch (Exception e) { FireError($"Failed to fetch style: {e.Message}"); }
            }

            if (string.IsNullOrEmpty(json))
            {
                FireError("No style provided. Set StyleUrl or StyleJson.");
                return;
            }

            Style = StyleParser.Parse(json);
            FireEvent(MapEventType.StyleData);

            // 1b. Load sprite atlas if style defines one
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

            // Always have a SpriteAtlas (even empty) so runtime addImage() works
            // regardless of whether the style declared a sprite URL.
            if (_spriteAtlas == null)
                _spriteAtlas = new SpriteAtlas(null, null);

            // 1c. Stand up the glyph source if the style declared a glyphs URL.
            // We don't pre-fetch any range -- the SymbolRenderer (or a custom
            // layer) requests ranges as text is rendered.
            if (!string.IsNullOrEmpty(Style.Glyphs))
            {
                _glyphSource = new GlyphSource();
                _glyphSource.Initialize(Style.Glyphs, this, TransformRequest);
            }

            if (_iconMaterial == null && _iconShader != null)
            {
                _iconMaterial = new Material(_iconShader);
                _iconMaterial.renderQueue = 3002; // Transparent+2
            }

            // 2. Initialize map state
            // Priority: Inspector override > Style JSON > 0 (default).
            // Matches MapLibre GL JS where constructor options override style defaults.
            // An Inspector field is "set" when its value is not NaN.
            bool centerOverridden = !double.IsNaN(_initialLongitude) && !double.IsNaN(_initialLatitude);
            var center = centerOverridden
                ? new LngLat(_initialLongitude, _initialLatitude)
                : Style.Center ?? new LngLat(0, 0);
            float zoom = !float.IsNaN(_initialZoom) ? _initialZoom : (Style.Zoom ?? 0f);
            float bearing = !float.IsNaN(_initialBearing) ? _initialBearing : (Style.Bearing ?? 0f);
            float pitch = !float.IsNaN(_initialPitch) ? _initialPitch : (Style.Pitch ?? 0f);

            State = new MapState();
            State.SetState(center, zoom, bearing, pitch);
            State.OnStateChanged += OnMapStateChanged;

            // 3. Initialize sources
            foreach (var kvp in Style.Sources)
            {
                // Track whether TileJSON provides attribution (overrides source definition)
                bool hasTileJsonAttribution = false;
                string sourceAttribution = kvp.Value.Attribution;

                FireDataLoading(kvp.Key);

                if (kvp.Value.Type == SourceType.Raster || kvp.Value.Type == SourceType.RasterDem)
                {
                    var source = new RasterTileSource();
                    source.Initialize(kvp.Key, kvp.Value, this, _tileCacheSize, TransformRequest);
                    _rasterSources[kvp.Key] = source;

                    // Fetch TileJSON if needed
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

                    // Fetch TileJSON if needed
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

                FireSourceData(kvp.Key, isLoaded: true);

                // Use source definition attribution only if TileJSON didn't provide one
                if (!hasTileJsonAttribution && !string.IsNullOrEmpty(sourceAttribution))
                    _pendingAttributions.Add(sourceAttribution);
            }

            // 3b. Compute effective min/max zoom from all sources
            foreach (var kvp in Style.Sources)
            {
                _effectiveMinZoom = Math.Min(_effectiveMinZoom, kvp.Value.MinZoom);
                _effectiveMaxZoom = Math.Max(_effectiveMaxZoom, kvp.Value.MaxZoom);
            }

            // 4. Initialize rendering
            // 4a. Initialize TerrainManager from style.terrain (if defined and source is valid raster-dem).
            _terrainManager = null;
            if (Style.Terrain != null && !string.IsNullOrEmpty(Style.Terrain.Source) &&
                Style.Sources.TryGetValue(Style.Terrain.Source, out var demSrc) &&
                demSrc.Type == SourceType.RasterDem)
            {
                _terrainManager = new Terrain.TerrainManager();
                _terrainManager.Initialize(Style.Terrain.Source, demSrc.Encoding, demSrc.MaxZoom);
                _terrainManager.Exaggeration =
                    Style.Terrain.ResolveExaggeration(new Expressions.EvaluationContext(State.Zoom));
            }

            _tilePool = new TileObjectPool(transform, _rasterTileMaterial, _tilePoolInitialSize);
            _mapRenderer = new MapRenderer();
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

            // 4b. Set DEM encoding for hillshade renderers
            foreach (var layer in Style.Layers)
            {
                if (layer.Type == LayerType.Hillshade && !string.IsNullOrEmpty(layer.Source))
                {
                    if (Style.Sources.TryGetValue(layer.Source, out var srcDef))
                    {
                        _mapRenderer.GetHillshadeRenderer(layer.Id)?.SetEncoding(srcDef.Encoding);
                    }
                }
            }

            // 5. Initialize tile manager
            _tileManager = new TileManager();
            _tileManager.OnTileNeeded += OnTileNeeded;
            _tileManager.OnTileExpired += OnTileExpired;

            // 6. Initialize camera
            if (_mapCamera == null)
                _mapCamera = UnityEngine.Camera.main;

            if (_mapCamera != null)
            {
                _cameraController = _mapCamera.GetComponent<MapCamera>();
                if (_cameraController == null)
                    _cameraController = _mapCamera.gameObject.AddComponent<MapCamera>();

                _inputHandler = _mapCamera.GetComponent<MapInputHandler>();
                if (_inputHandler == null)
                    _inputHandler = _mapCamera.gameObject.AddComponent<MapInputHandler>();

                _inputHandler.Initialize(State, _mapCamera);
                // Compose the camera-suppression check so a draggable prefab
                // under the cursor blocks map pan / zoom the same way a UITK
                // overlay does. Non-draggable prefabs do not block -- clicks
                // simply route to the prefab and the map's own click event is
                // suppressed for that frame inside HandlePointerEvents.
                _inputHandler.SetPointerOverUICheck(pos =>
                    IsPointerOverOverlayUI(pos) || IsPointerOverDraggablePrefab(pos));
                _cameraController.UpdateFromMapState(State);

                // 6b. Initialize animator
                Animator = _mapCamera.GetComponent<MapAnimator>();
                if (Animator == null)
                    Animator = _mapCamera.gameObject.AddComponent<MapAnimator>();
                Animator.Initialize(State);

                // Cancel animations on user input
                _inputHandler.OnUserInteraction += Animator.CancelAnimation;

                // Wire box-zoom (shift + left drag) → MapEventType + FitBounds.
                _inputHandler.OnBoxZoomStart += OnBoxZoomStart;
                _inputHandler.OnBoxZoomDrag += OnBoxZoomDrag;
                _inputHandler.OnBoxZoomEnd += OnBoxZoomEnd;
            }

            // 7. Initialize UI overlay
            InitializeUIControls();

            _isInitialized = true;
            _needsTileUpdate = true;

            FireEvent(MapEventType.Load);
        }

        private void InitializeUIControls()
        {
            // PanelSettings is required for UIDocument to render. If the user
            // hasn't assigned one in the Inspector, build a runtime fallback.
            // Try to find the built-in UnityDefaultRuntimeTheme via every path
            // Unity has shipped it under across versions; if all of them fail,
            // generate a minimal valid ThemeStyleSheet (empty but with imports)
            // -- leaving themeStyleSheet null causes Unity to log a warning each
            // frame the panel ticks.
            if (_panelSettings == null)
            {
                _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _panelSettings.name = "MapPanelSettings";
                _panelSettings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
                _panelSettings.clearColor = false;
                var defaultTheme = ResolveDefaultRuntimeTheme();
                if (defaultTheme != null)
                    _panelSettings.themeStyleSheet = defaultTheme;
                else
                    SuppressNoThemeWarning(_panelSettings);
            }

            var uiDocGo = new GameObject("MapControls_UI");
            uiDocGo.transform.SetParent(transform);

            var uiDoc = uiDocGo.AddComponent<UIDocument>();
            uiDoc.panelSettings = _panelSettings;
            uiDoc.sortingOrder = 100;

            _controlsOverlay = uiDocGo.AddComponent<MapControlsOverlay>();
            _controlsOverlay.SetMap(this);

            foreach (var attr in _pendingAttributions)
                _controlsOverlay.AddAttribution(attr);
            _pendingAttributions.Clear();
        }

        // Resolve UnityDefaultRuntimeTheme via the same delegate that Unity's
        // own UIDocument inspector uses to populate the default -- exposed
        // through reflection because PanelSettings doesn't surface it
        // publicly. Returns null only if the API moves; the caller is
        // expected to handle that by setting `disableNoThemeWarning = true`
        // so the panel renders silently with whatever Unity falls back to.
        private static ThemeStyleSheet ResolveDefaultRuntimeTheme()
        {
            try
            {
                var f = typeof(PanelSettings).GetField("GetOrCreateDefaultTheme",
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);
                if (f?.GetValue(null) is Delegate getDefault)
                    return getDefault.DynamicInvoke() as ThemeStyleSheet;
            }
            catch { /* fall through to null */ }
            return null;
        }

        // Fallback: tell PanelSettings not to warn when no theme is set. The
        // property is internal so we set it via reflection. No-op if the
        // field disappears in a future Unity version.
        private static void SuppressNoThemeWarning(PanelSettings ps)
        {
            try
            {
                var prop = typeof(PanelSettings).GetProperty("disableNoThemeWarning",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);
                prop?.SetValue(ps, true);
            }
            catch { /* best-effort */ }
        }

        private void Update()
        {
            if (!_isInitialized) return;

            // Safety net for AddLayer(triggerRefresh:false) batches whose
            // caller forgot the trailing RefreshTiles. Coalesces every
            // AddLayer(false) call in the previous frame into a single
            // RefreshTiles here, so layers always become visible in the
            // following frame instead of waiting for a pan/zoom. Manual
            // RefreshTiles invocations clear the flag, so the explicit
            // batch pattern still produces exactly one refresh.
            if (_pendingLayerRefresh)
            {
                _pendingLayerRefresh = false;
                RefreshTiles();
            }

            // Coalesced glyph-load refresh runs *before* anything else this
            // frame. The flag is set inside FetchRange when the coroutine
            // completes synchronously on a disk-cache hit; deferring the
            // RefreshTiles call to here keeps it out of the in-flight
            // SymbolRenderer.ShowTile path.
            DrainPendingGlyphRefresh();

            _mapRenderer?.ProcessMeshQueues();

            // Always update positions/collision every frame so newly created
            // labels and zoom-dependent sizes are applied immediately.
            var centerMerc = CoordinateConversion.LngLatToMercator(State.Center);
            float farClip = _mapCamera != null ? _mapCamera.farClipPlane : 0f;
            float frustumHeight = ComputeFrustumHeight();
            _mapRenderer?.UpdateMapState(centerMerc, State.Zoom, farClip, frustumHeight,
                State.Bearing, State.Pitch);

            // ICustomLayer.Prerender runs first across all visible custom
            // layers, then Render runs in style order. Both observe the
            // camera/state already settled for this frame because they happen
            // after UpdateMapState.
            if (_customLayers.Count > 0)
            {
                _mapRenderer?.PrerenderCustomLayers(this, _customLayers, _mapCamera, State.Zoom);
                _mapRenderer?.RenderCustomLayers(this, _customLayers, _mapCamera, State.Zoom);
            }

            if (_needsTileUpdate)
            {
                UpdateTiles();
                _needsTileUpdate = false;
            }

            UpdatePrefabSources();

            HandleResize();
            ProcessEvents();
            UpdatePrefabPointer();
            HandlePointerEvents();
            HandleTouchEvents();
        }

        private void OnMapStateChanged()
        {
            _needsTileUpdate = true;
            _stateChangedThisFrame = true;
            _frameChangeFlags |= State.LastChangeFlags;

            _cameraController?.UpdateFromMapState(State);

            var centerMerc = CoordinateConversion.LngLatToMercator(State.Center);
            float farClip = _mapCamera != null ? _mapCamera.farClipPlane : 0f;
            float frustumHeight = ComputeFrustumHeight();
            _mapRenderer?.UpdateMapState(centerMerc, State.Zoom, farClip, frustumHeight,
                State.Bearing, State.Pitch);
        }

        /// <summary>
        /// Compute the actual camera frustum height at the ground plane (Y=0).
        /// Uses the real camera position and FOV instead of hardcoded constants.
        /// </summary>
        private float ComputeFrustumHeight()
        {
            if (_mapCamera == null) return 0f;
            float cameraHeight = _mapCamera.transform.position.y;
            float fovRad = _mapCamera.fieldOfView * Mathf.Deg2Rad;
            return 2f * cameraHeight * Mathf.Tan(fovRad * 0.5f);
        }

        private void UpdateTiles()
        {
            if (_cameraController == null)
            {
                Debug.LogWarning("[MapLibreMap] UpdateTiles: _cameraController is null");
                return;
            }

            // No tile-backed sources → nothing to compute. The sentinels stay at
            // their initial values (min=22, max=0) when Style.Sources is empty,
            // which would make GetVisibleTilesLOD root at z=22 (1<<22 squared
            // iterations) and freeze the main thread.
            if (_effectiveMinZoom > _effectiveMaxZoom)
            {
                _tileManager.UpdateVisibleTiles(new HashSet<CanonicalTileID>());
                return;
            }

            // Use LOD-based tile selection: tiles closer to the camera are at the
            // map's current zoom; tiles further away use lower (parent) zoom levels
            // so the on-screen pixel size stays roughly constant. This keeps the
            // total tile count manageable even at high pitch (75°-85°) where the
            // horizon would otherwise demand thousands of same-zoom tiles.
            var visibleTiles = TileGrid.GetVisibleTilesLOD(_mapCamera, State.Center, State.Zoom,
                _effectiveMinZoom, _effectiveMaxZoom);
            _tileManager.UpdateVisibleTiles(visibleTiles);
        }

        private void OnTileNeeded(CanonicalTileID tileId)
        {
            _tileManager.SetTileState(tileId, TileState.Loading);
            float zoom = State.Zoom;

            // Raster sources: one request per layer
            foreach (var layer in Style.Layers)
            {
                if (!layer.IsVisibleAtZoom(zoom)) continue;

                if ((layer.Type == LayerType.Raster || layer.Type == LayerType.Hillshade) &&
                    _rasterSources.TryGetValue(layer.Source, out var source))
                {
                    var capturedSourceId = layer.Source;
                    FireDataLoading(capturedSourceId);

                    // Parent-tile fallback: if any ancestor of this tile is already
                    // cached, render it immediately as a placeholder. The real child
                    // texture replaces it when the request completes.
                    if (layer.Type == LayerType.Raster &&
                        !_mapRenderer.IsTerrainRasterLayer(layer.Id) &&
                        source.TryGetCachedAncestor(tileId, out var parentId, out var parentTex))
                    {
                        var centerMercFallback = CoordinateConversion.LngLatToMercator(State.Center);
                        _mapRenderer.GetRasterRenderer(layer.Id)?.ShowParentFallback(
                            tileId, parentId, parentTex, centerMercFallback, State.Zoom);
                    }

                    source.RequestTile(tileId,
                        (id, texture) =>
                        {
                            OnTileTextureLoaded(layer.Id, layer.Type, id, texture);
                            FireSourceData(capturedSourceId, isLoaded: true);
                        },
                        (id, error) =>
                        {
                            _tileManager.SetTileState(id, TileState.Error);
                            FireError($"Tile {id} failed: {error}", capturedSourceId);
                        }
                    );
                }
            }

            // Terrain DEM source: ensure the DEM tile is fetched even if no layer references it.
            // Beyond the DEM source's maxzoom, walk up to a parent tile.
            if (_terrainManager != null &&
                _rasterSources.TryGetValue(_terrainManager.SourceId, out var demSource))
            {
                var demTileId = tileId;
                while (demTileId.Z > demSource.Definition.MaxZoom && demTileId.Z > 0)
                    demTileId = demTileId.Parent();

                var demSourceId = _terrainManager.SourceId;
                demSource.RequestTile(demTileId,
                    (id, texture) =>
                    {
                        // Register with TerrainManager so dependent renderers can pick it up.
                        _terrainManager.RegisterTile(id, texture);
                        FireSourceData(demSourceId, isLoaded: true);
                    },
                    (id, error) =>
                    {
                        FireError($"Terrain DEM tile {id} failed: {error}", demSourceId);
                    }
                );
            }

            // Vector and GeoJSON sources: one request per source, dispatched to all layers
            var requestedVectorSources = new HashSet<string>();
            foreach (var layer in Style.Layers)
            {
                if ((layer.Type == LayerType.Fill || layer.Type == LayerType.Line ||
                     layer.Type == LayerType.Symbol || layer.Type == LayerType.Circle ||
                     layer.Type == LayerType.FillExtrusion || layer.Type == LayerType.Heatmap) &&
                    !string.IsNullOrEmpty(layer.Source) &&
                    !requestedVectorSources.Contains(layer.Source))
                {
                    if (_vectorSources.TryGetValue(layer.Source, out var source))
                    {
                        requestedVectorSources.Add(layer.Source);

                        // Fall back to parent tile if beyond source maxzoom
                        var requestTileId = tileId;
                        while (requestTileId.Z > source.Definition.MaxZoom && requestTileId.Z > 0)
                        {
                            requestTileId = requestTileId.Parent();
                        }

                        var capturedTileId = tileId;
                        var capturedSourceId = layer.Source;
                        FireDataLoading(capturedSourceId);
                        source.RequestTile(requestTileId,
                            (id, data) =>
                            {
                                OnVectorTileLoaded(capturedSourceId, capturedTileId, data);
                                FireSourceData(capturedSourceId, isLoaded: true);
                            },
                            (id, error) =>
                            {
                                _tileManager.SetTileState(capturedTileId, TileState.Error);
                                // Parse timeouts come from VectorTileSource cancelling a slow
                                // background parse -- transient under load, not a real fault.
                                bool isTransient = error != null && error.StartsWith("Parse timeout");
                                FireError($"Vector tile {id} failed: {error}", capturedSourceId,
                                    isTransient: isTransient);
                            }
                        );
                    }
                    else if (_geoJsonSources.TryGetValue(layer.Source, out var geoJsonSource))
                    {
                        requestedVectorSources.Add(layer.Source);

                        var capturedTileId = tileId;
                        var capturedSourceId = layer.Source;
                        FireDataLoading(capturedSourceId);
                        geoJsonSource.RequestTile(tileId,
                            (id, data) =>
                            {
                                OnVectorTileLoaded(capturedSourceId, capturedTileId, data);
                                FireSourceData(capturedSourceId, isLoaded: true);
                            },
                            (id, error) =>
                            {
                                _tileManager.SetTileState(capturedTileId, TileState.Error);
                                FireError($"GeoJSON tile {id} failed: {error}", capturedSourceId);
                            }
                        );
                    }
                }
            }
        }

        private void OnTileTextureLoaded(string layerId, LayerType layerType, CanonicalTileID tileId, Texture2D texture)
        {
            if (!_tileManager.IsActive(tileId)) return;

            _tileManager.SetTileState(tileId, TileState.Loaded);
            var centerMerc = CoordinateConversion.LngLatToMercator(State.Center);

            if (layerType == LayerType.Hillshade)
            {
                _mapRenderer.GetHillshadeRenderer(layerId)?.ShowTile(tileId, texture, centerMerc, State.Zoom);
            }
            else if (_mapRenderer.IsTerrainRasterLayer(layerId))
            {
                _mapRenderer.GetTerrainRenderer(layerId)?.ShowTile(tileId, texture, centerMerc, State.Zoom);
            }
            else
            {
                _mapRenderer.GetRasterRenderer(layerId)?.ShowTile(tileId, texture, centerMerc, State.Zoom);
            }
        }

        /// <summary>
        /// Dispatch received vector tile data to all layers sharing the same source.
        /// </summary>
        private void OnVectorTileLoaded(string sourceId, CanonicalTileID tileId, VectorTileData data)
        {
            if (!_tileManager.IsActive(tileId)) return;

            _tileManager.SetTileState(tileId, TileState.Loaded);
            var centerMerc = CoordinateConversion.LngLatToMercator(State.Center);
            float zoom = State.Zoom;

            foreach (var layer in Style.Layers)
            {
                if (layer.Source != sourceId) continue;
                if (!layer.IsVisibleAtZoom(zoom)) continue;

                if (layer.Type == LayerType.Fill || layer.Type == LayerType.Line)
                {
                    _mapRenderer.GetVectorRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                }
                else if (layer.Type == LayerType.Circle)
                {
                    _mapRenderer.GetCircleRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                }
                else if (layer.Type == LayerType.FillExtrusion)
                {
                    _mapRenderer.GetFillExtrusionRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                }
                else if (layer.Type == LayerType.Heatmap)
                {
                    _mapRenderer.GetHeatmapRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                }
                else if (layer.Type == LayerType.Symbol)
                {
                    _mapRenderer.GetSymbolRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                }
            }
        }

        private void OnTileExpired(CanonicalTileID tileId)
        {
            // Note: DEM tiles are intentionally NOT unregistered here. A single DEM tile
            // (especially after zoom-clamping to source maxzoom) can be shared by multiple
            // render tiles, so unregistering on individual expiry would yank data still
            // needed by sibling tiles. The underlying RasterTileSource's LRU cache bounds
            // memory growth.

            foreach (var layer in Style.Layers)
            {
                // Renderer-side teardown: ILayerRenderer-based dispatch handles every
                // tile-driven layer type (raster / terrain / hillshade / fill / line /
                // circle / fill-extrusion / heatmap / symbol). Background / image /
                // custom layers are not registered, so the call is a no-op for them.
                _mapRenderer.HideTile(layer.Id, tileId);

                // Source-side cancellation: raster-backed layers cancel against the
                // raster source; vector/geojson-backed layers cancel against the matching
                // vector or geojson source. Layers with no source (background) have
                // nothing to cancel.
                if (string.IsNullOrEmpty(layer.Source)) continue;

                if (layer.Type == LayerType.Raster || layer.Type == LayerType.Hillshade)
                {
                    if (_rasterSources.TryGetValue(layer.Source, out var rasterSource))
                        rasterSource.CancelRequest(tileId);
                }
                else if (layer.Type == LayerType.Fill || layer.Type == LayerType.Line ||
                         layer.Type == LayerType.Circle || layer.Type == LayerType.FillExtrusion ||
                         layer.Type == LayerType.Heatmap || layer.Type == LayerType.Symbol)
                {
                    if (_vectorSources.TryGetValue(layer.Source, out var vectorSource))
                        vectorSource.CancelRequest(tileId);
                    else if (_geoJsonSources.TryGetValue(layer.Source, out var geoJsonSource))
                        geoJsonSource.CancelRequest(tileId);
                }
            }
        }

        /// <summary>
        /// Fetches a UTF-8 text resource through TransformRequest. Returns
        /// <c>null</c> if the request is aborted by transformRequest. Throws
        /// on HTTP failure.
        /// </summary>
        private async Awaitable<string> FetchTextAsync(string url)
        {
            var transformed = RequestTransformer.Apply(TransformRequest, url, ResourceKind.Style);
            if (transformed.Abort)
                throw new Exception($"Request aborted by transformRequest: {url}");

            using var request = UnityEngine.Networking.UnityWebRequest.Get(transformed.Url);
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

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                throw new Exception(request.error);
            return request.downloadHandler.text;
        }

        private void OnDestroy()
        {
            // Fire Remove before clearing handlers so observers receive the signal.
            if (_isInitialized)
                FireEvent(MapEventType.Remove);

            // ICustomLayer owns its own GameObjects/assets. Notify it now so it
            // can release them -- Unity is about to tear down the map.
            foreach (var custom in _customLayers.Values)
            {
                try { custom.OnRemove(this); }
                catch (Exception e)
                {
                    Debug.LogError($"[MapLibreMap] OnDestroy: ICustomLayer.OnRemove threw: {e}");
                }
            }
            _customLayers.Clear();

            _eventSystem.Clear();

            if (State != null)
                State.OnStateChanged -= OnMapStateChanged;

            if (_tileManager != null)
            {
                _tileManager.OnTileNeeded -= OnTileNeeded;
                _tileManager.OnTileExpired -= OnTileExpired;
                _tileManager.Clear();
            }

            foreach (var source in _rasterSources.Values)
                source.Dispose();
            foreach (var source in _vectorSources.Values)
                source.Dispose();
            foreach (var source in _geoJsonSources.Values)
                source.Dispose();
            foreach (var source in _imageSources.Values)
                source.Dispose();
            DisposePrefabSources();

            _mapRenderer?.Dispose();

            if (_rasterTileMaterial != null)
                Destroy(_rasterTileMaterial);
            if (_iconMaterial != null)
                Destroy(_iconMaterial);
        }
    }
}
