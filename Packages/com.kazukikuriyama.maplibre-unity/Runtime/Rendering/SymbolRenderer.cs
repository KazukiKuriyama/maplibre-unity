using System.Collections.Generic;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Symbol layer renderer using TextMeshPro for SDF text labels
    /// and quad meshes for sprite/icon rendering from the style's sprite atlas.
    /// </summary>
    public partial class SymbolRenderer : ILayerRenderer
    {
        private struct LabelData
        {
            public GameObject Go;
            public CanonicalTileID TileId;
            /// <summary>Tile-local offset from tile center (range roughly -0.5..0.5).</summary>
            public float LocalX;
            public float LocalZ;
            /// <summary>Character count for collision box estimation (0 for icon-only).</summary>
            public int CharCount;
            /// <summary>True if this is an icon, false if text label.</summary>
            public bool IsIcon;
            /// <summary>Icon world-space width for collision (only valid when IsIcon=true).</summary>
            public float IconWorldWidth;
            /// <summary>Icon world-space height for collision (only valid when IsIcon=true).</summary>
            public float IconWorldHeight;
            /// <summary>Per-feature collision padding in CSS pixels.</summary>
            public float PaddingCSS;
            /// <summary>True if this label opts out of collision checks.</summary>
            public bool AllowOverlap;
            /// <summary>True if this label does not block other labels even when placed.</summary>
            public bool IgnorePlacement;
            /// <summary>symbol-sort-key value. Higher = placed first.</summary>
            public float SortKey;
            /// <summary>
            /// True when text/icon-rotation-alignment is "viewport": the label counter-rotates
            /// around Y by the map bearing each frame so it stays screen-upright.
            /// False (default) keeps the label fixed in map space.
            /// </summary>
            public bool ViewportRotation;
            /// <summary>
            /// True when text/icon-pitch-alignment is "viewport": the label tilts toward the
            /// camera each frame so it remains face-on in pitched views.
            /// </summary>
            public bool ViewportPitch;
        }

        /// <summary>Data for a text label placed along a line path (symbol-placement: "line").</summary>
        private struct LineLabelData
        {
            public GameObject Go;
            public TextMeshPro Tmp;
            public CanonicalTileID TileId;
            /// <summary>Line path in tile-local normalized coordinates (0-1 range).</summary>
            public Vector2[] NormalizedPath;
            /// <summary>Character count for collision estimation.</summary>
            public int CharCount;
            public float PaddingCSS;
            public bool AllowOverlap;
            public bool IgnorePlacement;
            public float SortKey;
            /// <summary>text-keep-upright: when true, reverse path on each re-warp if text would read right-to-left.</summary>
            public bool KeepUpright;
        }

        private readonly Dictionary<CanonicalTileID, List<LabelData>> _activeTileObjects = new();
        private readonly Dictionary<CanonicalTileID, List<LineLabelData>> _activeLineLabels = new();
        private readonly HashSet<CanonicalTileID> _requestedTiles = new();
        private readonly Transform _parent;
        private int _layerOrder;
        private readonly int _maxLabelsPerTile;
        private readonly TMP_FontAsset _fontAsset;
        private readonly SpriteAtlas _spriteAtlas;
        private readonly Material _iconMaterial;

        /// <summary>Text-size Expression from the style, re-evaluated per zoom.</summary>
        private Expression _textSizeExpr;
        /// <summary>Icon-size Expression from the style, re-evaluated per zoom.</summary>
        private Expression _iconSizeExpr;

        // text-translate / icon-translate paint properties. Spec: CSS-pixel
        // [x, y] offset applied at render time. Anchor "map" rotates with the
        // world; "viewport" stays screen-aligned. Updated per-dispatch via
        // <see cref="ApplyPaintTranslate"/>.
        private Vector2 _textTranslateCSS;
        private string _textTranslateAnchor = "map";
        private Vector2 _iconTranslateCSS;
        private string _iconTranslateAnchor = "map";

        private const float MinScreenHeight = 480f;

        /// <summary>Last frustumHeight received from UpdateAllPositions, used for initial placement.</summary>
        private float _lastFrustumHeight;

        /// <summary>
        /// TMP font size used internally. A higher value provides better SDF quality.
        /// The visual size is controlled by the GameObject scale, not this value.
        /// </summary>
        private const float TmpFontSize = 36f;

        /// <summary>Lazily created fallback font from OS system fonts.</summary>
        private static TMP_FontAsset _systemFontFallback;
        private static bool _systemFontFallbackAttempted;

        // Shader property IDs for icon material
        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int OpacityPropertyId = Shader.PropertyToID("_Opacity");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int IsSdfPropertyId = Shader.PropertyToID("_IsSdf");

        // Shared unit quad mesh for all icons (UVs are set per-instance via MaterialPropertyBlock)
        private static Mesh _sharedIconQuad;

        private IFeatureStateStore _featureStateStore;
        public void SetFeatureStateStore(IFeatureStateStore store) => _featureStateStore = store;

        // SDF text path. When _glyphSource is non-null and a label's first
        // text-font fontstack has glyph data available (or can be fetched
        // on-demand), the renderer builds a self-rendered SDF mesh instead of
        // the TextMeshPro one. _sdfTextShader is the URP "MapLibre/SdfText"
        // material template; _onGlyphsLoaded re-fires ShowTile for any tile
        // that had a missing-glyph label so newly-fetched ranges complete it.
        private GlyphSource _glyphSource;
        private Shader _sdfTextShader;
        private System.Action _onGlyphsLoaded;
        private readonly HashSet<CanonicalTileID> _tilesWithMissingGlyphs = new();
        // SDF property ids -- the values are pushed via MaterialPropertyBlock
        // so labels with different colours share a single Material.
        private static readonly int SdfTextColorPropertyId = Shader.PropertyToID("_TextColor");
        private static readonly int SdfHaloColorPropertyId = Shader.PropertyToID("_HaloColor");
        private static readonly int SdfHaloWidthPropertyId = Shader.PropertyToID("_HaloWidth");
        private static readonly int SdfOpacityPropertyId = Shader.PropertyToID("_Opacity");
        private static readonly int SdfGlyphAtlasPropertyId = Shader.PropertyToID("_GlyphAtlas");

        /// <summary>
        /// Wire the glyph fetching source and SDF shader. Pass null to disable
        /// SDF text -- the renderer falls back to TextMeshPro for every label.
        /// MapRenderer calls this when MapLibreMap.Style.Glyphs is non-empty.
        /// </summary>
        public void SetGlyphSource(GlyphSource glyphSource, Shader sdfTextShader)
        {
            _glyphSource = glyphSource;
            _sdfTextShader = sdfTextShader;
        }

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

        public SymbolRenderer(Transform parent, int layerOrder = 0, int maxLabelsPerTile = 50,
            TMP_FontAsset fontAsset = null, SpriteAtlas spriteAtlas = null, Material iconMaterial = null,
            TMP_FontAsset[] fontFallbacks = null)
        {
            _parent = parent;
            _layerOrder = layerOrder;
            _maxLabelsPerTile = maxLabelsPerTile;
            _fontAsset = fontAsset ?? GetOrCreateSystemFontFallback();
            _spriteAtlas = spriteAtlas;
            _iconMaterial = iconMaterial;

            // Wire up TMP's per-glyph fallback chain so labels mixing scripts
            // (e.g. Latin and CJK in the same string) render correctly even
            // when the primary font lacks some glyphs. fallbackFontAssetTable
            // is searched in order.
            if (_fontAsset != null && fontFallbacks != null && fontFallbacks.Length > 0)
            {
                if (_fontAsset.fallbackFontAssetTable == null)
                    _fontAsset.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();

                foreach (var fb in fontFallbacks)
                {
                    if (fb == null || fb == _fontAsset) continue;
                    if (!_fontAsset.fallbackFontAssetTable.Contains(fb))
                        _fontAsset.fallbackFontAssetTable.Add(fb);
                }
            }
        }

        public void SetLayerOrder(int layerOrder)
        {
            _layerOrder = layerOrder;
            // SymbolRenderer computes y-offset per frame from _layerOrder, so no extra work needed.
            // Text/icon sort order is driven by the per-label sorting in TMP + _layerOrder.
        }

        /// <summary>
        /// Font file name fragments to match against OS font paths, in priority order.
        /// These are matched case-insensitively against the file name portion of the path.
        /// </summary>
        private static readonly string[] SystemFontFilePatterns =
        {
            "HiraginoSans",          // macOS
            "HiraKakuProN",          // macOS (older)
            "YuGothic", "YuGothB",  // Windows / macOS
            "Meiryo", "meiryo",     // Windows
            "NotoSansCJK",          // Linux / installed
            "NotoSansJP",           // Linux / installed
            "Arial Unicode",        // macOS / Windows
        };

        /// <summary>
        /// Creates a dynamic TMP_FontAsset from an available OS system font file.
        /// Uses Font.GetPathsToOSFonts() to locate actual font files, which provides
        /// the font data needed by TMP's FontEngine (unlike CreateDynamicFontFromOSFont).
        /// The result is cached so it is only created once across all SymbolRenderer instances.
        /// </summary>
        private static TMP_FontAsset GetOrCreateSystemFontFallback()
        {
            if (_systemFontFallbackAttempted)
                return _systemFontFallback;

            _systemFontFallbackAttempted = true;

            // TMP_Settings asset must exist for CreateFontAsset to work.
            // If the project hasn't imported TMP Essentials, skip gracefully.
            if (TMP_Settings.instance == null)
            {
                Debug.LogWarning("[MapLibre] TMP_Settings not found. Import TMP Essentials " +
                                 "(Window > TextMeshPro > Import TMP Essential Resources) or assign a Symbol Font manually.");
                return null;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL Player has no API to enumerate or read OS-installed font
            // files (Font.GetPathsToOSFonts returns an empty array). Fall back
            // to Unity's built-in LegacyRuntime.ttf so Latin labels still
            // render -- CJK / non-Latin scripts still require the user to
            // assign a TMP_FontAsset via MapLibreMap > Text > Symbol Font.
            var builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtinFont == null)
            {
                LogFontWarning();
                return null;
            }
            try
            {
                _systemFontFallback = TMP_FontAsset.CreateFontAsset(builtinFont);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MapLibre] Failed to create font asset from LegacyRuntime.ttf: {e.Message}");
                LogFontWarning();
                return null;
            }
            if (_systemFontFallback != null)
            {
                _systemFontFallback.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                if (_systemFontFallback.atlasTexture == null)
                {
                    var tex = new Texture2D(512, 512, TextureFormat.Alpha8, false);
                    tex.name = "MapLibre-SystemFont Atlas";
                    _systemFontFallback.atlasTextures = new[] { tex };
                    if (_systemFontFallback.material != null)
                        _systemFontFallback.material.SetTexture(ShaderUtilities.ID_MainTex, tex);
                }
                _systemFontFallback.name = "MapLibre-SystemFont (LegacyRuntime.ttf)";
                Debug.Log("[MapLibre] No Symbol Font assigned. Using built-in LegacyRuntime.ttf " +
                          "as fallback on WebGL (Latin only -- assign a TMP_FontAsset for CJK).");
                return _systemFontFallback;
            }
            LogFontWarning();
            return null;
#else
            var fontPaths = Font.GetPathsToOSFonts();
            if (fontPaths == null || fontPaths.Length == 0)
            {
                LogFontWarning();
                return null;
            }

            foreach (var pattern in SystemFontFilePatterns)
            {
                foreach (var path in fontPaths)
                {
                    var fileName = System.IO.Path.GetFileName(path);
                    if (fileName.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var font = new Font(path);
                    if (font == null)
                        continue;

                    try
                    {
                        _systemFontFallback = TMP_FontAsset.CreateFontAsset(font);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[MapLibre] Failed to create font asset from \"{fileName}\": {e.Message}");
                        continue;
                    }

                    if (_systemFontFallback != null)
                    {
                        _systemFontFallback.atlasPopulationMode = AtlasPopulationMode.Dynamic;

                        // Ensure atlas texture exists for dynamic glyph population
                        if (_systemFontFallback.atlasTexture == null)
                        {
                            var tex = new Texture2D(512, 512, TextureFormat.Alpha8, false);
                            tex.name = "MapLibre-SystemFont Atlas";
                            _systemFontFallback.atlasTextures = new[] { tex };
                            if (_systemFontFallback.material != null)
                                _systemFontFallback.material.SetTexture(ShaderUtilities.ID_MainTex, tex);
                        }

                        _systemFontFallback.name = $"MapLibre-SystemFont ({fileName})";
                        Debug.Log($"[MapLibre] No Symbol Font assigned. Using OS font \"{fileName}\" as fallback.");
                        return _systemFontFallback;
                    }
                }
            }

            LogFontWarning();
            return null;
#endif
        }

        private static void LogFontWarning()
        {
            Debug.LogWarning("[MapLibre] No Symbol Font assigned and no suitable OS font found. " +
                             "CJK text may not render correctly. Use MapLibre > Font Setup or assign a TMP_FontAsset in MapLibreMap > Text > Symbol Font.");
        }

        /// <summary>
        /// Compute Y offset that scales with camera height so labels always
        /// render above tile geometry regardless of zoom level.
        /// </summary>
        private float ComputeYOffset(float frustumHeight)
        {
            // frustumHeight is proportional to camera height; use a fraction of it
            return frustumHeight * 0.005f + _layerOrder * 0.001f;
        }

        /// <summary>
        /// Cache the layer's text-translate / icon-translate paint properties.
        /// Called from each ShowTile* dispatcher; the cached vectors are then
        /// applied per-frame by <see cref="UpdateAllPositions"/>.
        /// </summary>
        private void ApplyPaintTranslate(SymbolPaintProperties paintProps)
        {
            _textTranslateCSS = paintProps.TextTranslate != null && paintProps.TextTranslate.Length >= 2
                ? new Vector2(paintProps.TextTranslate[0], paintProps.TextTranslate[1])
                : Vector2.zero;
            _textTranslateAnchor = paintProps.TextTranslateAnchor ?? "map";
            _iconTranslateCSS = paintProps.IconTranslate != null && paintProps.IconTranslate.Length >= 2
                ? new Vector2(paintProps.IconTranslate[0], paintProps.IconTranslate[1])
                : Vector2.zero;
            _iconTranslateAnchor = paintProps.IconTranslateAnchor ?? "map";
        }

        /// <summary>
        /// Translate a CSS-pixel paint offset ([x, y]) into a world XZ delta.
        /// Same convention as VectorTileRenderer.TranslateToWorld: CSS y points
        /// down on screen, so screen-down maps to -Z in our XZ plane.
        /// </summary>
        private static Vector2 CssTranslateToWorld(Vector2 cssOffset, string anchor,
            float pxToWorld, float bearingDeg)
        {
            if (cssOffset == Vector2.zero || pxToWorld <= 0f) return Vector2.zero;
            Vector2 world = new(cssOffset.x * pxToWorld, -cssOffset.y * pxToWorld);
            if (anchor == "viewport")
            {
                float rad = -bearingDeg * Mathf.Deg2Rad;
                float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                world = new Vector2(world.x * c - world.y * s, world.x * s + world.y * c);
            }
            return world;
        }

        public void ShowTile(CanonicalTileID tileId, VectorTileData tileData,
            LayerDefinition layerDef, MercatorCoordinate mapCenter, float zoom)
        {
            if (_requestedTiles.Contains(tileId))
                return;

            var tileLayer = tileData.GetLayer(layerDef.SourceLayer ?? layerDef.Id);
            if (tileLayer == null || tileLayer.Features.Count == 0)
                return;

            _requestedTiles.Add(tileId);

            var layoutProps = StyleParser.ParseSymbolLayout(layerDef.Layout);
            var paintProps = StyleParser.ParseSymbolPaint(layerDef.Paint);
            var ctx = MakeContext(zoom, layerDef.Source, layerDef.SourceLayer);

            // Branch on symbol-placement
            if (layoutProps.SymbolPlacement == "line")
            {
                bool useSdfLine = _glyphSource != null
                    && _sdfTextShader != null
                    && _glyphSource.Atlas != null;
                if (useSdfLine)
                {
                    ShowTileLinePlacementSdf(tileId, tileLayer, layerDef, layoutProps, paintProps,
                        ctx, mapCenter, zoom);
                }
                else
                {
                    ShowTileLinePlacement(tileId, tileLayer, layerDef, layoutProps, paintProps,
                        ctx, mapCenter, zoom);
                }
                return;
            }

            // Store expressions for zoom-dependent re-evaluation
            if (layoutProps.TextSize != null)
                _textSizeExpr = layoutProps.TextSize;
            if (layoutProps.IconSize != null)
                _iconSizeExpr = layoutProps.IconSize;

            ApplyPaintTranslate(paintProps);

            float textSize = layoutProps.ResolveTextSize(ctx);
            Color textColor = paintProps.ResolveTextColor(ctx);
            float textOpacity = paintProps.ResolveTextOpacity(ctx);
            Color haloColor = paintProps.ResolveTextHaloColor(ctx);
            float haloWidth = paintProps.ResolveTextHaloWidth(ctx);

            float iconSizeMultiplier = layoutProps.ResolveIconSize(ctx);
            float iconOpacity = paintProps.ResolveIconOpacity(ctx);
            Color iconColor = paintProps.ResolveIconColor(ctx);

            var features = FilterFeatures(tileLayer, layerDef.Filter, zoom,
                layerDef.Source, layerDef.SourceLayer);
            if (features.Count == 0) return;

            var labels = new List<LabelData>();
            int count = 0;

            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            int n = 1 << tileId.Z;
            double tileSize = 1.0 / n;
            double tileCenterX = (tileId.X + 0.5) * tileSize;
            double tileCenterY = (tileId.Y + 0.5) * tileSize;
            var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
            Vector3 tileWorldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);
            float tileWorldSize = (float)(tileSize * worldScale);

            float invExtent = 1f / tileLayer.Extent;

            foreach (var feature in features)
            {
                if (count >= _maxLabelsPerTile) break;

                var featureCtx = MakeContext(zoom, layerDef.Source, layerDef.SourceLayer, feature);

                // Per-feature collision metadata
                float textPadding = layoutProps.ResolveTextPadding(featureCtx);
                float iconPadding = layoutProps.ResolveIconPadding(featureCtx);
                bool textAllowOverlap = layoutProps.ResolveTextAllowOverlap(featureCtx);
                bool iconAllowOverlap = layoutProps.ResolveIconAllowOverlap(featureCtx);
                bool textIgnorePlacement = layoutProps.ResolveTextIgnorePlacement(featureCtx);
                bool iconIgnorePlacement = layoutProps.ResolveIconIgnorePlacement(featureCtx);
                float sortKey = layoutProps.ResolveSymbolSortKey(featureCtx);

                // Layer-level alignment (not per-feature in MapLibre spec).
                bool textViewportRot = layoutProps.ResolveTextRotationAlignment() == "viewport";
                bool textViewportPitch = layoutProps.ResolveTextPitchAlignment() == "viewport";
                bool iconViewportRot = layoutProps.ResolveIconRotationAlignment() == "viewport";
                bool iconViewportPitch = layoutProps.ResolveIconPitchAlignment() == "viewport";

                // Get centroid position
                var centroid = GetFeatureCentroid(feature, invExtent);
                if (!centroid.HasValue) continue;

                float localX = centroid.Value.x - 0.5f;
                float localZ = -(centroid.Value.y - 0.5f);
                float fh = _lastFrustumHeight;
                float yOffset = ComputeYOffset(fh);
                Vector3 worldPos = tileWorldPos + new Vector3(
                    localX * tileWorldSize, yOffset, localZ * tileWorldSize);

                // Try to create icon
                string iconName = layoutProps.ResolveIconImage(featureCtx);
                bool hasIcon = false;
                SpriteEntry spriteEntry = null;

                if (!string.IsNullOrEmpty(iconName) && _spriteAtlas != null && _iconMaterial != null
                    && _spriteAtlas.TryGetEntry(iconName, out spriteEntry, out var uvRect, out var iconTexture))
                {
                    hasIcon = true;
                    // Store base sprite dimensions (before iconSizeMultiplier) to avoid
                    // double-multiplication in UpdateAllPositions.
                    float baseWidth = spriteEntry.Width / spriteEntry.PixelRatio;
                    float baseHeight = spriteEntry.Height / spriteEntry.PixelRatio;
                    float iconCssWidth = baseWidth * iconSizeMultiplier;
                    float iconCssHeight = baseHeight * iconSizeMultiplier;

                    // icon-text-fit: stretch the icon to wrap the paired
                    // text. Measure the text envelope first (a quick CSS-px
                    // approximation -- exact glyph metrics aren't available
                    // until the mesh is built and over-fitting wastes pixels
                    // for the highway-shield use case anyway). Padding goes
                    // [top, right, bottom, left] per spec.
                    if (layoutProps.IconTextFit != null && layoutProps.IconTextFit != "none")
                    {
                        string fitText = ResolveText(layoutProps, featureCtx);
                        if (!string.IsNullOrEmpty(fitText))
                        {
                            // 0.55x font size is a workable mean glyph advance
                            // for Latin / numeric text; CJK runs slightly wider
                            // but symbol-as-shield labels are short enough that
                            // the approximation lands close enough.
                            float textWidthCss = fitText.Length * textSize * 0.55f;
                            float textHeightCss = textSize;
                            var pad = layoutProps.IconTextFitPadding;
                            float padW = (pad?.Length >= 4) ? pad[1] + pad[3] : 0f;
                            float padH = (pad?.Length >= 4) ? pad[0] + pad[2] : 0f;

                            string fit = layoutProps.IconTextFit;
                            if (fit == "width" || fit == "both")
                                iconCssWidth = textWidthCss + padW;
                            if (fit == "height" || fit == "both")
                                iconCssHeight = textHeightCss + padH;
                        }
                    }

                    var iconGo = CreateIconObject(spriteEntry, uvRect, iconTexture, iconOpacity, iconColor);
                    iconGo.SetActive(false); // Hidden until collision detection runs
                    float iconScaleW = ComputeIconScale(iconCssWidth, fh);
                    float iconScaleH = ComputeIconScale(iconCssHeight, fh);

                    iconGo.transform.localPosition = worldPos;
                    // Match text label convention: 3-axis scale with negative Y for flip.
                    iconGo.transform.localScale = new Vector3(iconScaleW, -iconScaleH, iconScaleW);

                    labels.Add(new LabelData
                    {
                        Go = iconGo,
                        TileId = tileId,
                        LocalX = localX,
                        LocalZ = localZ,
                        CharCount = 0,
                        IsIcon = true,
                        IconWorldWidth = baseWidth,
                        IconWorldHeight = baseHeight,
                        PaddingCSS = iconPadding,
                        AllowOverlap = iconAllowOverlap,
                        IgnorePlacement = iconIgnorePlacement,
                        SortKey = sortKey,
                        ViewportRotation = iconViewportRot,
                        ViewportPitch = iconViewportPitch,
                    });
                    count++;
                }

                // Try to create text label
                string text = ResolveText(layoutProps, featureCtx);
                if (!string.IsNullOrEmpty(text))
                {
                    // Pull the primary fontstack only when SDF path is wired --
                    // saves one expression evaluation per feature when the
                    // style does not declare a glyphs URL.
                    string fontstack = _glyphSource != null
                        ? ResolveFontstack(layoutProps, featureCtx)
                        : null;
                    var textGo = CreateTextObject(text, textSize, textColor, textOpacity,
                        haloColor, haloWidth, fontstack);
                    textGo.SetActive(false); // Hidden until collision detection runs

                    // If we have an icon, offset text below it
                    Vector3 textPos = worldPos;
                    if (hasIcon && spriteEntry != null)
                    {
                        // Offset text below the icon (half icon height + half text height)
                        float spriteH = spriteEntry.Height / spriteEntry.PixelRatio * iconSizeMultiplier;
                        float offsetZ = ComputeIconScale(spriteH * 0.5f + textSize * 0.5f, fh);
                        textPos.z -= offsetZ;
                    }

                    textGo.transform.localPosition = textPos;
                    float scale = ComputeTextScale(textSize, fh);
                    textGo.transform.localScale = new Vector3(scale, -scale, scale);

                    labels.Add(new LabelData
                    {
                        Go = textGo,
                        TileId = tileId,
                        LocalX = localX,
                        LocalZ = localZ,
                        CharCount = text.Length,
                        IsIcon = false,
                        PaddingCSS = textPadding,
                        AllowOverlap = textAllowOverlap,
                        IgnorePlacement = textIgnorePlacement,
                        SortKey = sortKey,
                        ViewportRotation = textViewportRot,
                        ViewportPitch = textViewportPitch,
                    });
                    count++;
                }
            }

            if (labels.Count > 0)
                _activeTileObjects[tileId] = labels;
        }

        // ResolveText / ResolveTemplateString moved to SymbolRenderer.TextAndIcons.cs

        private List<VectorTileFeature> FilterFeatures(VectorTileLayer layer, Expression filter,
            float zoom, string sourceId, string sourceLayer)
        {
            var result = new List<VectorTileFeature>();
            foreach (var feature in layer.Features)
            {
                // Symbol layers primarily render Point features
                if (feature.Type != GeometryType.Point && feature.Type != GeometryType.LineString)
                    continue;

                if (filter != null)
                {
                    var ctx = MakeContext(zoom, sourceId, sourceLayer, feature);
                    if (!filter.EvaluateBool(ctx)) continue;
                }

                result.Add(feature);
            }
            return result;
        }

        // CSSPixelRatio / ComputeTextScale / ComputeIconScale / GetFeatureCentroid /
        // ResolveFontstack / CreateSdfTextObject / CreateTextObject / CreateIconObject /
        // CreateIconQuadMesh moved to SymbolRenderer.TextAndIcons.cs.
        // ShowTileLinePlacement(*) and the path / EnsureLeftToRight / WarpTextAlongPath
        // helpers moved to SymbolRenderer.LinePlacement.cs.


        public void HideTile(CanonicalTileID tileId)
        {
            _requestedTiles.Remove(tileId);

            if (_activeTileObjects.TryGetValue(tileId, out var labels))
            {
                foreach (var label in labels)
                {
                    if (label.Go != null)
                    {
                        // Destroy icon meshes to avoid leaks
                        if (label.IsIcon)
                        {
                            var mf = label.Go.GetComponent<MeshFilter>();
                            if (mf != null && mf.sharedMesh != null)
                                Object.Destroy(mf.sharedMesh);
                        }
                        Object.Destroy(label.Go);
                    }
                }
                _activeTileObjects.Remove(tileId);
            }

            if (_activeLineLabels.TryGetValue(tileId, out var lineLabels))
            {
                foreach (var ll in lineLabels)
                {
                    if (ll.Go == null) continue;
                    // SDF entries own their mesh + material instance and won't
                    // be cleaned up by TMP. (TMP entries have Tmp != null and
                    // delegate mesh ownership to TextMeshPro.)
                    if (ll.Tmp == null)
                    {
                        var mf = ll.Go.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                            Object.Destroy(mf.sharedMesh);
                        var mr = ll.Go.GetComponent<MeshRenderer>();
                        if (mr != null && mr.sharedMaterial != null)
                            Object.Destroy(mr.sharedMaterial);
                    }
                    Object.Destroy(ll.Go);
                }
                _activeLineLabels.Remove(tileId);
            }
        }

        public void UpdateAllPositions(MercatorCoordinate mapCenter, float zoom,
            float frustumHeight = 0f, float bearingDeg = 0f, float pitchDeg = 0f)
        {
            if (frustumHeight > 0f)
                _lastFrustumHeight = frustumHeight;
            float fh = _lastFrustumHeight;
            if (fh <= 0f) return;

            // The text/icon mesh is built in the XY plane; basePlate lays it flat on
            // the XZ ground so it faces +Y for the default top-down camera. Every
            // rotation below stacks on basePlate -- without it, the label stands up in
            // the XY plane and disappears edge-on to a top-down camera.
            var basePlate = Quaternion.Euler(-90f, 0f, 0f);
            var bearingY = Quaternion.Euler(0f, bearingDeg, 0f);
            // Tilt the page backward by pitchDeg so it keeps facing the camera as the
            // camera pitches forward. Camera rotation is Euler(90-pitch, bearing, 0):
            // its forward is (0, -sin(pitch), cos(pitch)), so the label normal must be
            // (0, sin(pitch), -cos(pitch)) to face it. Starting from basePlate (normal
            // +Y), that requires rotating by -pitch around X -- not +pitch.
            var pitchTilt = Quaternion.Euler(-pitchDeg, 0f, 0f);
            var viewportRotOnly = bearingY * basePlate;
            var viewportPitchOnly = pitchTilt * basePlate;
            var viewportFull = bearingY * pitchTilt * basePlate;

            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            float yOffset = ComputeYOffset(fh);

            // Re-evaluate text size at current zoom
            var ctx = new EvaluationContext(zoom);
            float textSize = _textSizeExpr != null ? _textSizeExpr.EvaluateFloat(ctx, 16f) : 16f;
            float textScale = ComputeTextScale(textSize, fh);

            // Re-evaluate icon size at current zoom
            float iconSizeMultiplier = _iconSizeExpr != null ? _iconSizeExpr.EvaluateFloat(ctx, 1f) : 1f;

            // Resolve text/icon-translate (CSS px) into a world XZ delta. Spec:
            // viewport anchor counter-rotates with bearing so the offset stays
            // screen-aligned; map anchor rotates with the world.
            float pxToWorld = fh > 0f ? fh / Mathf.Max(Screen.height, MinScreenHeight) : 0f;
            Vector2 textOffWorld = CssTranslateToWorld(_textTranslateCSS, _textTranslateAnchor,
                pxToWorld, bearingDeg);
            Vector2 iconOffWorld = CssTranslateToWorld(_iconTranslateCSS, _iconTranslateAnchor,
                pxToWorld, bearingDeg);

            // Collect all labels with their screen-space positions for collision detection
            var allLabels = new List<(LabelData label, Vector3 worldPos)>();

            foreach (var kvp in _activeTileObjects)
            {
                var tileId = kvp.Key;
                int n = 1 << tileId.Z;
                double tileSize = 1.0 / n;
                double tileCenterX = (tileId.X + 0.5) * tileSize;
                double tileCenterY = (tileId.Y + 0.5) * tileSize;
                var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
                Vector3 tileWorldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);
                float tileWorldSize = (float)(tileSize * worldScale);

                foreach (var label in kvp.Value)
                {
                    if (label.Go == null) continue;

                    Vector3 worldPos = tileWorldPos + new Vector3(
                        label.LocalX * tileWorldSize, yOffset, label.LocalZ * tileWorldSize);
                    Vector2 paintOffset = label.IsIcon ? iconOffWorld : textOffWorld;
                    if (paintOffset != Vector2.zero)
                    {
                        worldPos.x += paintOffset.x;
                        worldPos.z += paintOffset.y;
                    }
                    label.Go.transform.localPosition = worldPos;

                    if (label.IsIcon)
                    {
                        float iconCssW = label.IconWorldWidth * iconSizeMultiplier;
                        float iconCssH = label.IconWorldHeight * iconSizeMultiplier;
                        float iconScaleW = ComputeIconScale(iconCssW, fh);
                        float iconScaleH = ComputeIconScale(iconCssH, fh);
                        // Match text label convention: 3-axis scale with negative Y for flip.
                        label.Go.transform.localScale = new Vector3(iconScaleW, -iconScaleH, iconScaleW);
                    }
                    else
                    {
                        label.Go.transform.localScale = new Vector3(textScale, -textScale, textScale);
                    }

                    // Apply alignment. The mesh is built in XY, so every branch
                    // composes onto basePlate (Euler(-90,0,0)) which lays it flat on
                    // XZ for the default top-down camera. "viewport" branches add the
                    // bearing/pitch counter-rotation to stay screen-upright.
                    if (label.ViewportRotation && label.ViewportPitch)
                        label.Go.transform.localRotation = viewportFull;
                    else if (label.ViewportRotation)
                        label.Go.transform.localRotation = viewportRotOnly;
                    else if (label.ViewportPitch)
                        label.Go.transform.localRotation = viewportPitchOnly;
                    else
                        label.Go.transform.localRotation = basePlate;

                    allLabels.Add((label, worldPos));
                }
            }

            // ── Re-warp line labels ──
            var allLineLabels = new List<(LineLabelData label, Vector2 center, float halfW)>();

            foreach (var kvp in _activeLineLabels)
            {
                var tileId = kvp.Key;
                int n = 1 << tileId.Z;
                double tileSize = 1.0 / n;
                double tileCenterX = (tileId.X + 0.5) * tileSize;
                double tileCenterY = (tileId.Y + 0.5) * tileSize;
                var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
                Vector3 tileWorldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);
                float tileWorldSize = (float)(tileSize * worldScale);

                foreach (var ll in kvp.Value)
                {
                    if (ll.Go == null) continue;

                    // Convert normalized path to world space (used by both branches --
                    // collision center for SDF, full re-warp source for TMP).
                    // text-translate shifts the entire path so warped glyphs and
                    // collision bounds stay consistent.
                    var worldPath = new Vector2[ll.NormalizedPath.Length];
                    for (int i = 0; i < ll.NormalizedPath.Length; i++)
                    {
                        float wx = (ll.NormalizedPath[i].x - 0.5f) * tileWorldSize;
                        float wz = -(ll.NormalizedPath[i].y - 0.5f) * tileWorldSize;
                        worldPath[i] = new Vector2(
                            tileWorldPos.x + wx + textOffWorld.x,
                            tileWorldPos.z + wz + textOffWorld.y);
                    }

                    float pathLen = ComputePathLength(worldPath);
                    float midDist = pathLen * 0.5f;

                    if (ll.Tmp == null)
                    {
                        // SDF line label -- mesh is baked in tile-local coordinates.
                        // Track tile movement by setting localPosition; the mesh
                        // shape stays frozen at the dispatch zoom (re-issued by
                        // TileManager on zoom-step boundaries).
                        ll.Go.transform.localPosition = new Vector3(
                            tileWorldPos.x + textOffWorld.x, yOffset,
                            tileWorldPos.z + textOffWorld.y);
                        // Use a glyph-pixel advance estimate (~12 px) for collision
                        // halfW since there is no TMP width to query. CharCount
                        // includes whitespace, which slightly over-pads -- fine for
                        // collision rejection.
                        float sdfHalfW = ll.CharCount * textScale * 12f * 0.5f;
                        SamplePathAtDistance(worldPath, midDist, out var sdfCenter, out _);
                        allLineLabels.Add((ll, sdfCenter, sdfHalfW));
                        continue;
                    }

                    float textWorldWidth = ll.Tmp.preferredWidth * textScale;

                    // text-keep-upright: re-evaluate path direction in screen space each
                    // frame so labels never invert as the user rotates the map. When
                    // disabled the original path order is preserved.
                    Vector2[] orientedPath;
                    float orientedMid;
                    if (ll.KeepUpright)
                    {
                        orientedPath = EnsureLeftToRight(worldPath, midDist, textWorldWidth,
                            bearingDeg, out bool wasReversed);
                        orientedMid = wasReversed ? pathLen - midDist : midDist;
                    }
                    else
                    {
                        orientedPath = worldPath;
                        orientedMid = midDist;
                    }

                    // Re-warp text -- temporarily activate so ForceMeshUpdate works
                    bool wasActive = ll.Go.activeSelf;
                    if (!wasActive) ll.Go.SetActive(true);
                    ll.Tmp.transform.localRotation = Quaternion.identity;
                    ll.Tmp.transform.localScale = Vector3.one;
                    ll.Tmp.ForceMeshUpdate();
                    if (!wasActive) ll.Go.SetActive(false);
                    WarpTextAlongPath(ll.Tmp, orientedPath, orientedMid, textScale, yOffset,
                        tileWorldPos);

                    // Compute center for collision
                    SamplePathAtDistance(orientedPath, orientedMid, out var center, out _);
                    allLineLabels.Add((ll, center, textWorldWidth * 0.5f));
                }
            }

            // Collision detection on XZ plane (top-down camera).
            // TMP renders text at TmpFontSize in local units; textScale converts to world units.
            // Average character width ≈ 50% of height for proportional fonts.
            float charHeight = textScale * TmpFontSize;
            float charWidth = charHeight * 0.5f;

            // CSS-pixel padding ratio: 1 CSS pixel ≈ frustumHeight / screenHeight in world units.
            // (pxToWorld was already computed earlier for paint translate; reuse it.)

            var placedBoxes = new List<Rect>();

            // Sort labels by symbol-sort-key DESC so higher-priority features place first.
            // Labels with the same sort key keep insertion order -- stable sort.
            allLabels.Sort((a, b) => b.label.SortKey.CompareTo(a.label.SortKey));
            allLineLabels.Sort((a, b) => b.label.SortKey.CompareTo(a.label.SortKey));

            foreach (var (label, worldPos) in allLabels)
            {
                float halfW, halfH;
                if (label.IsIcon)
                {
                    float iconW = ComputeIconScale(label.IconWorldWidth * iconSizeMultiplier, fh);
                    float iconH = ComputeIconScale(label.IconWorldHeight * iconSizeMultiplier, fh);
                    halfW = iconW * 0.5f;
                    halfH = iconH * 0.5f;
                }
                else
                {
                    halfW = label.CharCount * charWidth * 0.5f;
                    halfH = charHeight * 0.5f;
                }
                // Per-label padding from text-padding / icon-padding (in CSS pixels).
                float pad = label.PaddingCSS * pxToWorld;
                halfW += pad;
                halfH += pad;

                var rect = new Rect(worldPos.x - halfW, worldPos.z - halfH, halfW * 2f, halfH * 2f);

                bool overlaps = false;
                if (!label.AllowOverlap)
                {
                    for (int i = 0; i < placedBoxes.Count; i++)
                    {
                        if (rect.Overlaps(placedBoxes[i]))
                        {
                            overlaps = true;
                            break;
                        }
                    }
                }

                if (overlaps)
                {
                    label.Go.SetActive(false);
                }
                else
                {
                    label.Go.SetActive(true);
                    // ignore-placement labels do not block subsequent placements.
                    if (!label.IgnorePlacement)
                        placedBoxes.Add(rect);
                }
            }

            // Collision detection for line-placed labels
            foreach (var (ll, center, halfWidth) in allLineLabels)
            {
                float pad = ll.PaddingCSS * pxToWorld;
                float halfH = charHeight * 0.5f + pad;
                float halfW = halfWidth + pad;
                var rect = new Rect(center.x - halfW, center.y - halfH, halfW * 2f, halfH * 2f);

                bool overlaps = false;
                if (!ll.AllowOverlap)
                {
                    for (int i = 0; i < placedBoxes.Count; i++)
                    {
                        if (rect.Overlaps(placedBoxes[i]))
                        {
                            overlaps = true;
                            break;
                        }
                    }
                }

                if (overlaps)
                {
                    ll.Go.SetActive(false);
                }
                else
                {
                    ll.Go.SetActive(true);
                    if (!ll.IgnorePlacement)
                        placedBoxes.Add(rect);
                }
            }
        }

        public bool HasTile(CanonicalTileID tileId) =>
            _activeTileObjects.ContainsKey(tileId) || _activeLineLabels.ContainsKey(tileId);

        public void Clear()
        {
            _requestedTiles.Clear();
            // Collect all keys from both dictionaries to pass through HideTile
            var keys = new HashSet<CanonicalTileID>(_activeTileObjects.Keys);
            foreach (var k in _activeLineLabels.Keys) keys.Add(k);
            foreach (var key in keys)
                HideTile(key);
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
