using System.Collections.Generic;
using MapLibre.Unity.Expressions;
using UnityEngine;

namespace MapLibre.Unity.Style
{
    public enum LayerType
    {
        Background,
        Fill,
        Line,
        Symbol,
        Raster,
        Circle,
        FillExtrusion,
        Heatmap,
        Hillshade,
        /// <summary>
        /// Layer rendered by user code via <see cref="ICustomLayer"/>. The map
        /// owns the slot in render order (so it respects beforeId / minzoom /
        /// maxzoom / visibility) but defers all rendering to the supplied
        /// callback. Matches MapLibre GL JS <c>type: "custom"</c>.
        /// </summary>
        Custom,
    }

    /// <summary>
    /// User-supplied layer that draws into the map's render order. Implementations
    /// typically attach a Unity GameObject (MeshRenderer / particle system / etc.)
    /// in <see cref="OnAdd"/> and update its transform in <see cref="Render"/>.
    /// Mirrors MapLibre GL JS <c>CustomLayerInterface</c>.
    /// </summary>
    public interface ICustomLayer
    {
        /// <summary>Stable identifier -- used by RemoveLayer / MoveLayer.</summary>
        string Id { get; }

        /// <summary>
        /// Called once when the layer is added to the map. Use to instantiate
        /// GameObjects, load assets, or subscribe to events. The supplied map
        /// reference is the same instance the user passed to AddLayer().
        /// </summary>
        void OnAdd(MapLibreMap map);

        /// <summary>
        /// Called once when the layer is removed (or the map is destroyed).
        /// Use to free GameObjects and unsubscribe from events. Implementations
        /// should be safe to call when OnAdd was never reached.
        /// </summary>
        void OnRemove(MapLibreMap map);

        /// <summary>
        /// Called every frame while the layer is visible at the current zoom.
        /// Most implementations only need to update Transform values here --
        /// Unity's renderer pipeline does the actual draw.
        /// </summary>
        void Render(MapLibreMap map, UnityEngine.Camera camera);

        /// <summary>
        /// Optional pre-render pass. Called for every visible custom layer
        /// before any layer's <see cref="Render"/> runs, so use this for work
        /// that has to happen before the main pass -- render-to-texture passes,
        /// stencil setup, copying state from another camera, and so on.
        /// Mirrors MapLibre GL JS <c>prerender</c> in <c>CustomLayerInterface</c>.
        ///
        /// <para>
        /// Default implementation is empty so existing <see cref="ICustomLayer"/>
        /// types remain source-compatible. Override only when you need it.
        /// </para>
        /// </summary>
        void Prerender(MapLibreMap map, UnityEngine.Camera camera) { }
    }

    [System.Serializable]
    public class LayerDefinition
    {
        public string Id;
        public LayerType Type;
        public string Source;
        public string SourceLayer;
        public float? MinZoom;
        public float? MaxZoom;
        public Dictionary<string, object> Paint;
        public Dictionary<string, object> Layout;
        public string Visibility = "visible";
        public Expression Filter;

        /// <summary>
        /// Check if this layer should be visible at the given zoom level.
        /// </summary>
        public bool IsVisibleAtZoom(float zoom)
        {
            if (Visibility == "none") return false;
            if (MinZoom.HasValue && zoom < MinZoom.Value) return false;
            if (MaxZoom.HasValue && zoom >= MaxZoom.Value) return false;
            return true;
        }

        // Cached result of the feature-state usage scan. Layers are immutable
        // for the purposes of this check (paint/layout/filter changes go
        // through SetPaintProperty / SetFilter, which clear the flag below).
        private bool _usesFeatureStateChecked;
        private bool _usesFeatureState;

        /// <summary>
        /// True when this layer's paint, layout, or filter references runtime
        /// feature state via <c>["feature-state", ...]</c>. Used by MapLibreMap
        /// to skip tile rebuilds for layers that would not observe the change.
        /// Result is cached after the first call; call <see cref="InvalidateFeatureStateUsage"/>
        /// after mutating paint/layout/filter.
        /// </summary>
        public bool UsesFeatureState()
        {
            if (_usesFeatureStateChecked) return _usesFeatureState;
            _usesFeatureState = ComputeUsesFeatureState();
            _usesFeatureStateChecked = true;
            return _usesFeatureState;
        }

        /// <summary>
        /// Drop the cached <see cref="UsesFeatureState"/> result so the next
        /// call recomputes. Call after mutating <see cref="Paint"/>,
        /// <see cref="Layout"/>, or <see cref="Filter"/>.
        /// </summary>
        public void InvalidateFeatureStateUsage()
        {
            _usesFeatureStateChecked = false;
            _usesFeatureState = false;
        }

        private bool ComputeUsesFeatureState()
        {
            if (Filter != null && Filter.UsesFeatureState()) return true;
            return DictHasFeatureStateMarker(Paint) || DictHasFeatureStateMarker(Layout);
        }

        // Cheap structural scan over the raw paint/layout JSON. Looks for an
        // "feature-state" operator at any nesting depth. Operates on the raw
        // Dictionary<string, object> form because paint/layout is parsed lazily
        // by the typed property parsers in StyleParser. False positives (a
        // literal "feature-state" string in user data) only over-trigger
        // refreshes, never under-trigger.
        private static bool DictHasFeatureStateMarker(Dictionary<string, object> dict)
        {
            if (dict == null) return false;
            foreach (var kv in dict)
            {
                if (ValueHasFeatureStateMarker(kv.Value)) return true;
            }
            return false;
        }

        private static bool ValueHasFeatureStateMarker(object value)
        {
            if (value == null) return false;
            if (value is string s) return s == "feature-state";
            if (value is System.Collections.IDictionary nestedDict)
            {
                foreach (System.Collections.DictionaryEntry e in nestedDict)
                    if (ValueHasFeatureStateMarker(e.Value)) return true;
                return false;
            }
            if (value is System.Collections.IEnumerable list)
            {
                foreach (var item in list)
                    if (ValueHasFeatureStateMarker(item)) return true;
                return false;
            }
            return false;
        }
    }

    public class RasterPaintProperties
    {
        public Expression Opacity;
        public float HueRotate = 0f;
        public float BrightnessMin = 0f;
        public float BrightnessMax = 1f;
        public float Saturation = 0f;
        public float Contrast = 0f;
        public float FadeDuration = 300f;
        public string Resampling = "linear";

        public float ResolveOpacity(float zoom)
        {
            if (Opacity == null) return 1f;
            return Opacity.EvaluateFloat(new EvaluationContext(zoom), 1f);
        }
    }

    public class BackgroundPaintProperties
    {
        public Expression BackgroundColor;
        public Expression Opacity;
        /// <summary>
        /// Sprite name to tile across the visible background. When non-null,
        /// takes precedence over background-color. Per MapLibre Style Spec,
        /// pattern dimensions should be powers of two for seamless tiling.
        /// </summary>
        public Expression BackgroundPattern;

        public string ResolveBackgroundPattern(float zoom)
        {
            if (BackgroundPattern == null) return null;
            return BackgroundPattern.EvaluateString(new EvaluationContext(zoom));
        }

        public Color ResolveBackgroundColor(float zoom)
        {
            if (BackgroundColor == null) return Color.black;
            return BackgroundColor.EvaluateColor(new EvaluationContext(zoom), Color.black);
        }

        public float ResolveOpacity(float zoom)
        {
            if (Opacity == null) return 1f;
            return Opacity.EvaluateFloat(new EvaluationContext(zoom), 1f);
        }
    }

    public class FillPaintProperties
    {
        public Expression FillColor;
        public Expression FillOpacity;
        public Expression FillOutlineColor;
        public Expression Antialias;
        /// <summary>
        /// Sprite name to tile across the polygon. When set, takes precedence over fill-color.
        /// Matches MapLibre Style Spec fill-pattern.
        /// </summary>
        public Expression FillPattern;
        /// <summary>
        /// Translation offset in CSS pixels [x, y] applied at render time.
        /// Useful for soft-shadow / hover effects.
        /// </summary>
        public float[] FillTranslate;
        /// <summary>"map" (default) or "viewport". Anchor for fill-translate.</summary>
        public string FillTranslateAnchor = "map";

        public string ResolveFillPattern(EvaluationContext ctx)
        {
            if (FillPattern == null) return null;
            return FillPattern.EvaluateString(ctx);
        }

        public Color ResolveFillColor(EvaluationContext ctx)
        {
            if (FillColor == null) return new Color(0f, 0f, 0f, 1f);
            return FillColor.EvaluateColor(ctx, new Color(0f, 0f, 0f, 1f));
        }

        public float ResolveFillOpacity(EvaluationContext ctx)
        {
            if (FillOpacity == null) return 1f;
            return FillOpacity.EvaluateFloat(ctx, 1f);
        }

        public Color? ResolveFillOutlineColor(EvaluationContext ctx)
        {
            if (FillOutlineColor == null) return null;
            return FillOutlineColor.EvaluateColor(ctx);
        }

        public bool ResolveAntialias(EvaluationContext ctx)
        {
            if (Antialias == null) return true;
            return Antialias.EvaluateBool(ctx, true);
        }
    }

    public class LinePaintProperties
    {
        public Expression LineColor;
        public Expression LineOpacity;
        public Expression LineWidth;
        public Expression LineGapWidth;
        public Expression LineBlur;
        public Expression LineOffset;
        public string LineCap = "butt";
        public string LineJoin = "miter";

        /// <summary>
        /// Dash pattern as alternating dash/gap lengths in line-width units.
        /// null means solid line.
        /// </summary>
        public float[] LineDasharray;

        /// <summary>
        /// Sprite name to tile along the line stroke. When set, overrides line-color.
        /// Per-feature: features within one layer are bucketed by their resolved
        /// pattern name and rendered as separate meshes through MapLibreLinePattern.shader.
        /// </summary>
        public Expression LinePattern;

        /// <summary>
        /// Gradient stops as an interpolate expression of <c>line-progress</c>. Requires
        /// the source to have <c>lineMetrics: true</c>. When set, takes precedence over
        /// line-color. Currently parsed and evaluated to a 256-pixel ramp texture sampled
        /// per-vertex in the line shader using a 0..1 progress attribute.
        /// </summary>
        public Expression LineGradient;

        /// <summary>Translation offset in CSS pixels [x, y].</summary>
        public float[] LineTranslate;
        /// <summary>"map" (default) or "viewport". Anchor for line-translate.</summary>
        public string LineTranslateAnchor = "map";

        public string ResolveLinePattern(EvaluationContext ctx)
        {
            if (LinePattern == null) return null;
            return LinePattern.EvaluateString(ctx);
        }

        public Color ResolveLineColor(EvaluationContext ctx)
        {
            if (LineColor == null) return new Color(0f, 0f, 0f, 1f);
            return LineColor.EvaluateColor(ctx, new Color(0f, 0f, 0f, 1f));
        }

        public float ResolveLineOpacity(EvaluationContext ctx)
        {
            if (LineOpacity == null) return 1f;
            return LineOpacity.EvaluateFloat(ctx, 1f);
        }

        public float ResolveLineWidth(EvaluationContext ctx)
        {
            if (LineWidth == null) return 1f;
            return LineWidth.EvaluateFloat(ctx, 1f);
        }

        public float ResolveLineGapWidth(EvaluationContext ctx)
        {
            if (LineGapWidth == null) return 0f;
            return LineGapWidth.EvaluateFloat(ctx, 0f);
        }

        public float ResolveLineOffset(EvaluationContext ctx)
        {
            if (LineOffset == null) return 0f;
            return LineOffset.EvaluateFloat(ctx, 0f);
        }
    }

    public class CirclePaintProperties
    {
        public Expression CircleRadius;
        public Expression CircleColor;
        public Expression CircleOpacity;
        public Expression CircleBlur;
        public Expression CircleStrokeWidth;
        public Expression CircleStrokeColor;
        public Expression CircleStrokeOpacity;
        /// <summary>Translation offset in CSS pixels [x, y].</summary>
        public float[] CircleTranslate;
        /// <summary>"map" (default) or "viewport". Anchor for circle-translate.</summary>
        public string CircleTranslateAnchor = "map";
        /// <summary>
        /// circle-pitch-alignment: orientation of the circle when the map is pitched.
        /// "viewport" (default per spec) keeps the circle facing the camera;
        /// "map" lays it flat on the map plane.
        /// </summary>
        public string CirclePitchAlignment = "viewport";
        /// <summary>
        /// circle-pitch-scale: how the circle's size scales with map pitch. "map"
        /// (default per spec) shrinks the circle at oblique angles (size sticks to
        /// the map plane); "viewport" keeps the screen-pixel size constant.
        /// </summary>
        public string CirclePitchScale = "map";

        public float ResolveCircleRadius(EvaluationContext ctx)
        {
            if (CircleRadius == null) return 5f;
            return CircleRadius.EvaluateFloat(ctx, 5f);
        }

        public Color ResolveCircleColor(EvaluationContext ctx)
        {
            if (CircleColor == null) return new Color(0f, 0f, 0f, 1f);
            return CircleColor.EvaluateColor(ctx, new Color(0f, 0f, 0f, 1f));
        }

        public float ResolveCircleOpacity(EvaluationContext ctx)
        {
            if (CircleOpacity == null) return 1f;
            return CircleOpacity.EvaluateFloat(ctx, 1f);
        }

        public float ResolveCircleBlur(EvaluationContext ctx)
        {
            if (CircleBlur == null) return 0f;
            return CircleBlur.EvaluateFloat(ctx, 0f);
        }

        public float ResolveCircleStrokeWidth(EvaluationContext ctx)
        {
            if (CircleStrokeWidth == null) return 0f;
            return CircleStrokeWidth.EvaluateFloat(ctx, 0f);
        }

        public Color ResolveCircleStrokeColor(EvaluationContext ctx)
        {
            if (CircleStrokeColor == null) return new Color(0f, 0f, 0f, 1f);
            return CircleStrokeColor.EvaluateColor(ctx, new Color(0f, 0f, 0f, 1f));
        }

        public float ResolveCircleStrokeOpacity(EvaluationContext ctx)
        {
            if (CircleStrokeOpacity == null) return 1f;
            return CircleStrokeOpacity.EvaluateFloat(ctx, 1f);
        }
    }

    public class FillExtrusionPaintProperties
    {
        public Expression FillExtrusionColor;
        public Expression FillExtrusionHeight;
        public Expression FillExtrusionBase;
        public Expression FillExtrusionOpacity;
        public Expression FillExtrusionVerticalScale;
        /// <summary>
        /// Sprite name to texture the extrusion sides/top. Per-feature: features
        /// within one layer are bucketed by their resolved pattern name and
        /// rendered through MapLibreFillExtrusionPattern.shader.
        /// </summary>
        public Expression FillExtrusionPattern;

        public string ResolveFillExtrusionPattern(EvaluationContext ctx)
        {
            if (FillExtrusionPattern == null) return null;
            return FillExtrusionPattern.EvaluateString(ctx);
        }

        public Color ResolveFillExtrusionColor(EvaluationContext ctx)
        {
            if (FillExtrusionColor == null) return new Color(0f, 0f, 0f, 1f);
            return FillExtrusionColor.EvaluateColor(ctx, new Color(0f, 0f, 0f, 1f));
        }

        public float ResolveFillExtrusionHeight(EvaluationContext ctx)
        {
            if (FillExtrusionHeight == null) return 0f;
            return FillExtrusionHeight.EvaluateFloat(ctx, 0f);
        }

        public float ResolveFillExtrusionBase(EvaluationContext ctx)
        {
            if (FillExtrusionBase == null) return 0f;
            return FillExtrusionBase.EvaluateFloat(ctx, 0f);
        }

        public float ResolveFillExtrusionOpacity(EvaluationContext ctx)
        {
            if (FillExtrusionOpacity == null) return 1f;
            return FillExtrusionOpacity.EvaluateFloat(ctx, 1f);
        }

        public float ResolveFillExtrusionVerticalScale(EvaluationContext ctx)
        {
            if (FillExtrusionVerticalScale == null) return 1f;
            return FillExtrusionVerticalScale.EvaluateFloat(ctx, 1f);
        }
    }

    public class SymbolLayoutProperties
    {
        public Expression TextField;
        public Expression TextSize;
        public Expression TextFont;
        public Expression TextMaxWidth;
        public Expression TextAnchor;
        public Expression TextOffset;
        public Expression TextPadding;
        public Expression TextAllowOverlap;
        public Expression TextIgnorePlacement;
        public Expression IconImage;
        public Expression IconSize;
        public Expression IconOffset;
        public Expression IconAnchor;
        public Expression IconRotate;
        public Expression IconPadding;
        public Expression IconAllowOverlap;
        public Expression IconIgnorePlacement;

        /// <summary>
        /// icon-text-fit: scales the icon to fit the text it is paired with.
        /// One of: "none" (default -- independent sizing), "width", "height",
        /// "both". When the icon is anchored to a text label, the renderer
        /// stretches the icon mesh to match the text bounding box on the
        /// requested axes.
        /// </summary>
        public string IconTextFit = "none";

        /// <summary>
        /// icon-text-fit-padding: extra space (in CSS px) around the text
        /// when computing the icon's fit rectangle. Spec order is
        /// [top, right, bottom, left]. Default zero on every side.
        /// </summary>
        public float[] IconTextFitPadding = new[] { 0f, 0f, 0f, 0f };
        /// <summary>
        /// Optional sort key. Higher value = drawn (and considered for collision) first,
        /// matching MapLibre GL JS symbol-sort-key semantics.
        /// </summary>
        public Expression SymbolSortKey;
        public string SymbolPlacement = "point";
        public Expression SymbolSpacing;

        /// <summary>
        /// Whether text rotates with the map ("map") or stays viewport-aligned ("viewport").
        /// "auto" (default) = "viewport" for point placement, "map" for line placement.
        /// </summary>
        public string TextRotationAlignment = "auto";

        /// <summary>
        /// Whether text tilts with the map ("map") or stays viewport-aligned ("viewport").
        /// "auto" (default) inherits from text-rotation-alignment.
        /// </summary>
        public string TextPitchAlignment = "auto";

        /// <summary>Icon counterpart of text-rotation-alignment.</summary>
        public string IconRotationAlignment = "auto";

        /// <summary>Icon counterpart of text-pitch-alignment.</summary>
        public string IconPitchAlignment = "auto";

        /// <summary>
        /// If true (default), line-placed text labels never render upside-down: when the
        /// path direction would produce inverted text on screen, the path is walked in
        /// the opposite direction. Matches text-keep-upright in MapLibre Style Spec.
        /// </summary>
        public bool TextKeepUpright = true;

        /// <summary>Icon counterpart of text-keep-upright. Default true.</summary>
        public bool IconKeepUpright = true;

        /// <summary>
        /// Resolve text-rotation-alignment per the spec: "auto" maps to "map" for line
        /// placement and "viewport" otherwise.
        /// </summary>
        public string ResolveTextRotationAlignment()
        {
            if (TextRotationAlignment == "auto")
                return SymbolPlacement == "line" || SymbolPlacement == "line-center"
                    ? "map" : "viewport";
            return TextRotationAlignment;
        }

        /// <summary>
        /// Resolve text-pitch-alignment per the spec: "auto" follows text-rotation-alignment.
        /// </summary>
        public string ResolveTextPitchAlignment()
        {
            if (TextPitchAlignment == "auto")
                return ResolveTextRotationAlignment();
            return TextPitchAlignment;
        }

        public string ResolveIconRotationAlignment()
        {
            if (IconRotationAlignment == "auto")
                return SymbolPlacement == "line" || SymbolPlacement == "line-center"
                    ? "map" : "viewport";
            return IconRotationAlignment;
        }

        public string ResolveIconPitchAlignment()
        {
            if (IconPitchAlignment == "auto")
                return ResolveIconRotationAlignment();
            return IconPitchAlignment;
        }

        public string ResolveTextField(EvaluationContext ctx)
        {
            if (TextField == null) return null;
            return TextField.EvaluateString(ctx);
        }

        public float ResolveTextSize(EvaluationContext ctx)
        {
            if (TextSize == null) return 16f;
            return TextSize.EvaluateFloat(ctx, 16f);
        }

        public float ResolveTextMaxWidth(EvaluationContext ctx)
        {
            if (TextMaxWidth == null) return 10f;
            return TextMaxWidth.EvaluateFloat(ctx, 10f);
        }

        public string ResolveIconImage(EvaluationContext ctx)
        {
            if (IconImage == null) return null;
            return IconImage.EvaluateString(ctx);
        }

        public float ResolveIconSize(EvaluationContext ctx)
        {
            if (IconSize == null) return 1f;
            return IconSize.EvaluateFloat(ctx, 1f);
        }

        public Vector2 ResolveIconOffset(EvaluationContext ctx)
        {
            if (IconOffset == null) return Vector2.zero;
            var result = IconOffset.Evaluate(ctx);
            if (result is IList<object> list && list.Count >= 2)
            {
                return new Vector2(
                    System.Convert.ToSingle(list[0]),
                    System.Convert.ToSingle(list[1]));
            }
            return Vector2.zero;
        }

        public float ResolveIconRotate(EvaluationContext ctx)
        {
            if (IconRotate == null) return 0f;
            return IconRotate.EvaluateFloat(ctx, 0f);
        }

        public float ResolveIconPadding(EvaluationContext ctx)
        {
            if (IconPadding == null) return 2f;
            return IconPadding.EvaluateFloat(ctx, 2f);
        }

        public bool ResolveIconAllowOverlap(EvaluationContext ctx)
        {
            if (IconAllowOverlap == null) return false;
            return IconAllowOverlap.EvaluateBool(ctx, false);
        }

        /// <summary>
        /// Padding around text bounding box, in CSS pixels. Default 2 (per MapLibre spec).
        /// Symbols within this padding distance of an already-placed label are suppressed.
        /// </summary>
        public float ResolveTextPadding(EvaluationContext ctx)
        {
            if (TextPadding == null) return 2f;
            return TextPadding.EvaluateFloat(ctx, 2f);
        }

        public bool ResolveTextAllowOverlap(EvaluationContext ctx)
        {
            if (TextAllowOverlap == null) return false;
            return TextAllowOverlap.EvaluateBool(ctx, false);
        }

        /// <summary>
        /// Whether this label still consumes a collision slot when allow-overlap is true.
        /// Default false: a label that ignores placement does NOT block other labels.
        /// </summary>
        public bool ResolveTextIgnorePlacement(EvaluationContext ctx)
        {
            if (TextIgnorePlacement == null) return false;
            return TextIgnorePlacement.EvaluateBool(ctx, false);
        }

        public bool ResolveIconIgnorePlacement(EvaluationContext ctx)
        {
            if (IconIgnorePlacement == null) return false;
            return IconIgnorePlacement.EvaluateBool(ctx, false);
        }

        /// <summary>
        /// Per-feature placement priority. Features with the highest sort key are placed
        /// first; later features that collide with already-placed ones are dropped.
        /// Default 0.
        /// </summary>
        public float ResolveSymbolSortKey(EvaluationContext ctx)
        {
            if (SymbolSortKey == null) return 0f;
            return SymbolSortKey.EvaluateFloat(ctx, 0f);
        }

        /// <summary>
        /// Distance between repeated symbol labels along a line, in CSS pixels.
        /// Default 250 (matches MapLibre GL JS).
        /// </summary>
        public float ResolveSymbolSpacing(EvaluationContext ctx)
        {
            if (SymbolSpacing == null) return 250f;
            return SymbolSpacing.EvaluateFloat(ctx, 250f);
        }
    }

    public class HeatmapPaintProperties
    {
        public Expression HeatmapRadius;
        public Expression HeatmapWeight;
        public Expression HeatmapIntensity;
        public Expression HeatmapColor;
        public Expression HeatmapOpacity;

        public float ResolveHeatmapRadius(EvaluationContext ctx)
        {
            if (HeatmapRadius == null) return 30f;
            return HeatmapRadius.EvaluateFloat(ctx, 30f);
        }

        public float ResolveHeatmapWeight(EvaluationContext ctx)
        {
            if (HeatmapWeight == null) return 1f;
            return HeatmapWeight.EvaluateFloat(ctx, 1f);
        }

        public float ResolveHeatmapIntensity(EvaluationContext ctx)
        {
            if (HeatmapIntensity == null) return 1f;
            return HeatmapIntensity.EvaluateFloat(ctx, 1f);
        }

        public float ResolveHeatmapOpacity(EvaluationContext ctx)
        {
            if (HeatmapOpacity == null) return 1f;
            return HeatmapOpacity.EvaluateFloat(ctx, 1f);
        }
    }

    public class HillshadePaintProperties
    {
        public Expression IlluminationDirection;
        public string IlluminationAnchor = "viewport";
        public Expression Exaggeration;
        public Expression ShadowColor;
        public Expression HighlightColor;
        public Expression AccentColor;
        public Expression Opacity;

        public float ResolveIlluminationDirection(EvaluationContext ctx)
        {
            if (IlluminationDirection == null) return 335f;
            return IlluminationDirection.EvaluateFloat(ctx, 335f);
        }

        public float ResolveExaggeration(EvaluationContext ctx)
        {
            if (Exaggeration == null) return 0.5f;
            return Exaggeration.EvaluateFloat(ctx, 0.5f);
        }

        public Color ResolveShadowColor(EvaluationContext ctx)
        {
            if (ShadowColor == null) return Color.black;
            return ShadowColor.EvaluateColor(ctx, Color.black);
        }

        public Color ResolveHighlightColor(EvaluationContext ctx)
        {
            if (HighlightColor == null) return Color.white;
            return HighlightColor.EvaluateColor(ctx, Color.white);
        }

        public Color ResolveAccentColor(EvaluationContext ctx)
        {
            if (AccentColor == null) return Color.black;
            return AccentColor.EvaluateColor(ctx, Color.black);
        }

        public float ResolveOpacity(EvaluationContext ctx)
        {
            if (Opacity == null) return 1f;
            return Opacity.EvaluateFloat(ctx, 1f);
        }
    }

    public class SymbolPaintProperties
    {
        public Expression TextColor;
        public Expression TextOpacity;
        public Expression TextHaloColor;
        public Expression TextHaloWidth;
        public Expression IconOpacity;
        public Expression IconColor;
        public Expression IconHaloColor;
        public Expression IconHaloWidth;

        /// <summary>Text-translate offset in CSS pixels [x, y]. Default zero.</summary>
        public float[] TextTranslate;
        /// <summary>"map" (default) or "viewport". Anchor for text-translate.</summary>
        public string TextTranslateAnchor = "map";
        /// <summary>Icon-translate offset in CSS pixels [x, y]. Default zero.</summary>
        public float[] IconTranslate;
        /// <summary>"map" (default) or "viewport". Anchor for icon-translate.</summary>
        public string IconTranslateAnchor = "map";

        public Color ResolveTextColor(EvaluationContext ctx)
        {
            if (TextColor == null) return new Color(0f, 0f, 0f, 1f);
            return TextColor.EvaluateColor(ctx, new Color(0f, 0f, 0f, 1f));
        }

        public float ResolveTextOpacity(EvaluationContext ctx)
        {
            if (TextOpacity == null) return 1f;
            return TextOpacity.EvaluateFloat(ctx, 1f);
        }

        public Color ResolveTextHaloColor(EvaluationContext ctx)
        {
            if (TextHaloColor == null) return new Color(1f, 1f, 1f, 0f);
            return TextHaloColor.EvaluateColor(ctx, new Color(1f, 1f, 1f, 0f));
        }

        public float ResolveTextHaloWidth(EvaluationContext ctx)
        {
            if (TextHaloWidth == null) return 0f;
            return TextHaloWidth.EvaluateFloat(ctx, 0f);
        }

        public float ResolveIconOpacity(EvaluationContext ctx)
        {
            if (IconOpacity == null) return 1f;
            return IconOpacity.EvaluateFloat(ctx, 1f);
        }

        public Color ResolveIconColor(EvaluationContext ctx)
        {
            if (IconColor == null) return new Color(0f, 0f, 0f, 1f);
            return IconColor.EvaluateColor(ctx, new Color(0f, 0f, 0f, 1f));
        }

        public Color ResolveIconHaloColor(EvaluationContext ctx)
        {
            if (IconHaloColor == null) return new Color(0f, 0f, 0f, 0f);
            return IconHaloColor.EvaluateColor(ctx, new Color(0f, 0f, 0f, 0f));
        }

        public float ResolveIconHaloWidth(EvaluationContext ctx)
        {
            if (IconHaloWidth == null) return 0f;
            return IconHaloWidth.EvaluateFloat(ctx, 0f);
        }
    }
}
