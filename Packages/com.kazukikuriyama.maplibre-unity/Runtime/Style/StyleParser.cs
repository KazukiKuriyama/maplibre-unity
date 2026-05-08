using System;
using System.Collections.Generic;
using System.Globalization;
using MapLibre.Unity.Expressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Style
{
    public static class StyleParser
    {
        public static MapLibreStyle Parse(string json)
        {
            var root = JObject.Parse(json);
            var style = new MapLibreStyle
            {
                Version = root["version"]?.ToObject<int>() ?? 8,
                Name = root["name"]?.ToString(),
                Sprite = root["sprite"]?.ToString(),
                Glyphs = root["glyphs"]?.ToString()
            };

            // Center
            if (root["center"] is JArray centerArray && centerArray.Count >= 2)
            {
                style.Center = new LngLat(
                    centerArray[0].ToObject<double>(),
                    centerArray[1].ToObject<double>()
                );
            }

            style.Zoom = root["zoom"]?.ToObject<float>();
            style.Bearing = root["bearing"]?.ToObject<float>();
            style.Pitch = root["pitch"]?.ToObject<float>();

            // Transition
            if (root["transition"] is JObject transObj)
            {
                style.Transition = new TransitionDefinition
                {
                    Duration = transObj["duration"]?.ToObject<int>() ?? 300,
                    Delay = transObj["delay"]?.ToObject<int>() ?? 0
                };
            }

            // Sources
            if (root["sources"] is JObject sourcesObj)
            {
                foreach (var kvp in sourcesObj)
                {
                    var sourceDef = ParseSource(kvp.Value as JObject);
                    if (sourceDef != null)
                        style.Sources[kvp.Key] = sourceDef;
                }
            }

            // Layers
            if (root["layers"] is JArray layersArray)
            {
                foreach (var layerToken in layersArray)
                {
                    var layerDef = ParseLayer(layerToken as JObject);
                    if (layerDef != null)
                        style.Layers.Add(layerDef);
                }
            }

            // Terrain (root-level property)
            if (root["terrain"] is JObject terrainObj)
            {
                style.Terrain = ParseTerrain(terrainObj);
            }

            // Light (root-level property)
            if (root["light"] is JObject lightObj)
            {
                style.Light = ParseLight(lightObj);
            }

            // Sky (root-level property)
            if (root["sky"] is JObject skyObj)
            {
                style.Sky = ParseSky(skyObj);
            }

            return style;
        }

        private static SkyDefinition ParseSky(JObject obj)
        {
            if (obj == null) return null;
            var def = new SkyDefinition();
            var ctx0 = new EvaluationContext(0f);
            if (obj["sky-color"] != null)
            {
                var expr = ExpressionParser.Parse(obj["sky-color"], isColor: true);
                if (expr != null) def.SkyColor = expr.EvaluateColor(ctx0, def.SkyColor);
            }
            if (obj["horizon-color"] != null)
            {
                var expr = ExpressionParser.Parse(obj["horizon-color"], isColor: true);
                if (expr != null) def.HorizonColor = expr.EvaluateColor(ctx0, def.HorizonColor);
            }
            if (obj["sky-horizon-blend"] != null)
            {
                var expr = ExpressionParser.Parse(obj["sky-horizon-blend"]);
                def.SkyHorizonBlend = expr != null
                    ? expr.EvaluateFloat(ctx0, def.SkyHorizonBlend)
                    : obj["sky-horizon-blend"].ToObject<float>();
            }
            if (obj["horizon-fog-blend"] != null)
            {
                var expr = ExpressionParser.Parse(obj["horizon-fog-blend"]);
                def.HorizonFogBlend = expr != null
                    ? expr.EvaluateFloat(ctx0, def.HorizonFogBlend)
                    : obj["horizon-fog-blend"].ToObject<float>();
            }
            if (obj["fog-color"] != null)
            {
                var expr = ExpressionParser.Parse(obj["fog-color"], isColor: true);
                if (expr != null) def.FogColor = expr.EvaluateColor(ctx0, def.FogColor);
            }
            if (obj["fog-ground-blend"] != null)
            {
                var expr = ExpressionParser.Parse(obj["fog-ground-blend"]);
                def.FogGroundBlend = expr != null
                    ? expr.EvaluateFloat(ctx0, def.FogGroundBlend)
                    : obj["fog-ground-blend"].ToObject<float>();
            }
            if (obj["atmosphere-blend"] != null)
            {
                var expr = ExpressionParser.Parse(obj["atmosphere-blend"]);
                def.AtmosphereBlend = expr != null
                    ? expr.EvaluateFloat(ctx0, def.AtmosphereBlend)
                    : obj["atmosphere-blend"].ToObject<float>();
            }

            // Legacy Mapbox v8 sky properties -- folded into the closest MapLibre
            // equivalent so old styles still render reasonably instead of going
            // blank. sky-gradient (interpolate over sky-radial-progress) gets
            // sampled at 0 (top) and 1 (horizon) to derive the two endpoint
            // colors. Atmosphere variants are coarser approximations.
            if (obj["sky-type"] != null || obj["sky-gradient"] != null
                || obj["sky-atmosphere-color"] != null)
            {
                if (obj["sky-gradient"] != null)
                {
                    var expr = ExpressionParser.Parse(obj["sky-gradient"], isColor: true);
                    if (expr != null)
                    {
                        // sky-radial-progress 0 = center (top), 1 = edge (horizon).
                        // Without the dedicated input we approximate via zoom 0/1.
                        def.SkyColor = expr.EvaluateColor(new EvaluationContext(0f), def.SkyColor);
                        def.HorizonColor = expr.EvaluateColor(new EvaluationContext(1f), def.HorizonColor);
                    }
                }
                if (obj["sky-atmosphere-color"] != null)
                {
                    var expr = ExpressionParser.Parse(obj["sky-atmosphere-color"], isColor: true);
                    if (expr != null) def.SkyColor = expr.EvaluateColor(ctx0, def.SkyColor);
                }
                if (obj["sky-atmosphere-halo-color"] != null)
                {
                    var expr = ExpressionParser.Parse(obj["sky-atmosphere-halo-color"], isColor: true);
                    if (expr != null) def.HorizonColor = expr.EvaluateColor(ctx0, def.HorizonColor);
                }
                if (obj["sky-opacity"] != null)
                {
                    var expr = ExpressionParser.Parse(obj["sky-opacity"]);
                    float opacity = expr != null
                        ? expr.EvaluateFloat(ctx0, 1f)
                        : obj["sky-opacity"].ToObject<float>();
                    var s = def.SkyColor; s.a *= opacity; def.SkyColor = s;
                    var h = def.HorizonColor; h.a *= opacity; def.HorizonColor = h;
                }
            }
            return def;
        }

        private static LightDefinition ParseLight(JObject obj)
        {
            if (obj == null) return null;
            var def = new LightDefinition();
            if (obj["anchor"] != null) def.Anchor = obj["anchor"].ToString();
            if (obj["position"] is JArray posArr && posArr.Count >= 3)
            {
                def.Position = new[]
                {
                    posArr[0].ToObject<float>(),
                    posArr[1].ToObject<float>(),
                    posArr[2].ToObject<float>()
                };
            }
            if (obj["color"] != null)
            {
                var colorExpr = ExpressionParser.Parse(obj["color"], isColor: true);
                def.Color = colorExpr != null
                    ? colorExpr.EvaluateColor(new EvaluationContext(0f), Color.white)
                    : Color.white;
            }
            if (obj["intensity"] != null)
                def.Intensity = obj["intensity"].ToObject<float>();
            return def;
        }

        private static TerrainDefinition ParseTerrain(JObject obj)
        {
            if (obj == null) return null;
            var def = new TerrainDefinition
            {
                Source = obj["source"]?.ToString()
            };
            if (obj.TryGetValue("exaggeration", out var ex))
                def.Exaggeration = ExpressionParser.Parse(ex);
            return def;
        }

        private static SourceDefinition ParseSource(JObject obj)
        {
            if (obj == null) return null;

            var source = new SourceDefinition();
            string typeStr = obj["type"]?.ToString();

            source.Type = typeStr switch
            {
                "vector" => SourceType.Vector,
                "raster" => SourceType.Raster,
                "raster-dem" => SourceType.RasterDem,
                "geojson" => SourceType.GeoJson,
                "image" => SourceType.Image,
                "video" => SourceType.Video,
                _ => SourceType.Raster
            };

            source.Url = obj["url"]?.ToString();
            source.MinZoom = obj["minzoom"]?.ToObject<int>() ?? 0;
            source.MaxZoom = obj["maxzoom"]?.ToObject<int>() ?? 22;
            source.TileSize = obj["tilesize"]?.ToObject<int>() ?? obj["tileSize"]?.ToObject<int>() ?? 256;
            source.Scheme = obj["scheme"]?.ToString() ?? "xyz";
            source.Attribution = obj["attribution"]?.ToString();
            source.Volatile = obj["volatile"]?.ToObject<bool>() ?? false;
            source.Encoding = obj["encoding"]?.ToString() ?? "mapbox";

            if (obj["tiles"] is JArray tilesArray)
            {
                source.Tiles = new List<string>();
                foreach (var t in tilesArray)
                    source.Tiles.Add(t.ToString());
            }

            if (obj["bounds"] is JArray boundsArray)
                source.Bounds = boundsArray.ToObject<double[]>();

            // GeoJSON: inline data or URL string + clustering options
            if (source.Type == SourceType.GeoJson)
            {
                var dataToken = obj["data"];
                if (dataToken != null)
                    source.Data = dataToken;

                source.Cluster = obj["cluster"]?.ToObject<bool>() ?? false;
                source.ClusterRadius = obj["clusterRadius"]?.ToObject<int>() ?? 50;
                // Per MapLibre spec the default is (source maxzoom - 1).
                source.ClusterMaxZoom = obj["clusterMaxZoom"]?.ToObject<int>() ?? -1;
                source.ClusterMinPoints = obj["clusterMinPoints"]?.ToObject<int>() ?? 2;
            }

            // lineMetrics -- enables ["line-progress"] expressions for line-gradient.
            // Spec applies to vector and geojson sources.
            if (source.Type == SourceType.Vector || source.Type == SourceType.GeoJson)
                source.LineMetrics = obj["lineMetrics"]?.ToObject<bool>() ?? false;

            // Image / Video: 4-corner coordinates [lng, lat]
            // Per spec order: [top-left, top-right, bottom-right, bottom-left]
            if ((source.Type == SourceType.Image || source.Type == SourceType.Video)
                && obj["coordinates"] is JArray coordsArr)
            {
                source.Coordinates = new double[coordsArr.Count][];
                for (int i = 0; i < coordsArr.Count; i++)
                {
                    if (coordsArr[i] is JArray pair && pair.Count >= 2)
                    {
                        source.Coordinates[i] = new[]
                        {
                            pair[0].ToObject<double>(),
                            pair[1].ToObject<double>()
                        };
                    }
                }
            }

            return source;
        }

        private static LayerDefinition ParseLayer(JObject obj)
        {
            if (obj == null) return null;

            string typeStr = obj["type"]?.ToString();
            LayerType layerType;
            switch (typeStr)
            {
                case "background": layerType = LayerType.Background; break;
                case "fill": layerType = LayerType.Fill; break;
                case "line": layerType = LayerType.Line; break;
                case "symbol": layerType = LayerType.Symbol; break;
                case "raster": layerType = LayerType.Raster; break;
                case "circle": layerType = LayerType.Circle; break;
                case "fill-extrusion": layerType = LayerType.FillExtrusion; break;
                case "heatmap": layerType = LayerType.Heatmap; break;
                case "hillshade": layerType = LayerType.Hillshade; break;
                default:
                    Debug.LogWarning($"[MapLibre] Unknown layer type: {typeStr}");
                    return null;
            }

            var layer = new LayerDefinition
            {
                Id = obj["id"]?.ToString(),
                Type = layerType,
                Source = obj["source"]?.ToString(),
                SourceLayer = obj["source-layer"]?.ToString(),
                MinZoom = obj["minzoom"]?.ToObject<float>(),
                MaxZoom = obj["maxzoom"]?.ToObject<float>()
            };

            if (obj["paint"] is JObject paintObj)
                layer.Paint = paintObj.ToObject<Dictionary<string, object>>();

            if (obj["layout"] is JObject layoutObj)
            {
                layer.Layout = layoutObj.ToObject<Dictionary<string, object>>();
                if (layer.Layout.TryGetValue("visibility", out var vis))
                    layer.Visibility = vis?.ToString() ?? "visible";
            }

            // Parse filter expression
            if (obj["filter"] is JArray filterArr)
                layer.Filter = ExpressionParser.ParseFilter(filterArr);

            return layer;
        }

        public static RasterPaintProperties ParseRasterPaint(Dictionary<string, object> paint)
        {
            var props = new RasterPaintProperties();
            if (paint == null) return props;

            if (paint.TryGetValue("raster-opacity", out var opacity))
                props.Opacity = ExpressionParser.Parse(opacity);
            if (paint.TryGetValue("raster-hue-rotate", out var hue))
                props.HueRotate = Convert.ToSingle(hue);
            if (paint.TryGetValue("raster-brightness-min", out var bMin))
                props.BrightnessMin = Convert.ToSingle(bMin);
            if (paint.TryGetValue("raster-brightness-max", out var bMax))
                props.BrightnessMax = Convert.ToSingle(bMax);
            if (paint.TryGetValue("raster-saturation", out var sat))
                props.Saturation = Convert.ToSingle(sat);
            if (paint.TryGetValue("raster-contrast", out var contrast))
                props.Contrast = Convert.ToSingle(contrast);
            if (paint.TryGetValue("raster-fade-duration", out var fade))
                props.FadeDuration = Convert.ToSingle(fade);
            if (paint.TryGetValue("raster-resampling", out var resamp))
                props.Resampling = resamp?.ToString() ?? "linear";

            return props;
        }

        public static BackgroundPaintProperties ParseBackgroundPaint(Dictionary<string, object> paint)
        {
            var props = new BackgroundPaintProperties();
            if (paint == null) return props;

            if (paint.TryGetValue("background-color", out var colorVal))
                props.BackgroundColor = ExpressionParser.Parse(colorVal, isColor: true);
            if (paint.TryGetValue("background-opacity", out var opacity))
                props.Opacity = ExpressionParser.Parse(opacity);
            if (paint.TryGetValue("background-pattern", out var pattern))
                props.BackgroundPattern = ExpressionParser.Parse(pattern);

            return props;
        }

        public static FillPaintProperties ParseFillPaint(Dictionary<string, object> paint)
        {
            var props = new FillPaintProperties();
            if (paint == null) return props;

            if (paint.TryGetValue("fill-color", out var colorVal))
                props.FillColor = ExpressionParser.Parse(colorVal, isColor: true);
            if (paint.TryGetValue("fill-opacity", out var opacity))
                props.FillOpacity = ExpressionParser.Parse(opacity);
            if (paint.TryGetValue("fill-outline-color", out var outlineVal))
                props.FillOutlineColor = ExpressionParser.Parse(outlineVal, isColor: true);
            if (paint.TryGetValue("fill-antialias", out var antialias))
                props.Antialias = ExpressionParser.Parse(antialias);
            if (paint.TryGetValue("fill-pattern", out var pattern))
                props.FillPattern = ExpressionParser.Parse(pattern);
            if (paint.TryGetValue("fill-translate", out var translate))
                props.FillTranslate = ParseFloatArray(translate);
            if (paint.TryGetValue("fill-translate-anchor", out var transAnchor))
                props.FillTranslateAnchor = transAnchor?.ToString() ?? "map";

            return props;
        }

        public static LinePaintProperties ParseLinePaint(Dictionary<string, object> paint)
        {
            var props = new LinePaintProperties();
            if (paint == null) return props;

            if (paint.TryGetValue("line-color", out var colorVal))
                props.LineColor = ExpressionParser.Parse(colorVal, isColor: true);
            if (paint.TryGetValue("line-opacity", out var opacity))
                props.LineOpacity = ExpressionParser.Parse(opacity);
            if (paint.TryGetValue("line-width", out var width))
                props.LineWidth = ExpressionParser.Parse(width);
            if (paint.TryGetValue("line-gap-width", out var gapWidth))
                props.LineGapWidth = ExpressionParser.Parse(gapWidth);
            if (paint.TryGetValue("line-blur", out var blur))
                props.LineBlur = ExpressionParser.Parse(blur);
            if (paint.TryGetValue("line-offset", out var offset))
                props.LineOffset = ExpressionParser.Parse(offset);

            if (paint.TryGetValue("line-cap", out var cap))
                props.LineCap = cap?.ToString() ?? "butt";
            if (paint.TryGetValue("line-join", out var join))
                props.LineJoin = join?.ToString() ?? "miter";

            if (paint.TryGetValue("line-dasharray", out var dasharray))
                props.LineDasharray = ParseFloatArray(dasharray);
            if (paint.TryGetValue("line-pattern", out var linePattern))
                props.LinePattern = ExpressionParser.Parse(linePattern);
            if (paint.TryGetValue("line-gradient", out var lineGradient))
                props.LineGradient = ExpressionParser.Parse(lineGradient, isColor: true);
            if (paint.TryGetValue("line-translate", out var lineTranslate))
                props.LineTranslate = ParseFloatArray(lineTranslate);
            if (paint.TryGetValue("line-translate-anchor", out var lineTransAnchor))
                props.LineTranslateAnchor = lineTransAnchor?.ToString() ?? "map";

            return props;
        }

        public static CirclePaintProperties ParseCirclePaint(Dictionary<string, object> paint)
        {
            var props = new CirclePaintProperties();
            if (paint == null) return props;

            if (paint.TryGetValue("circle-radius", out var radius))
                props.CircleRadius = ExpressionParser.Parse(radius);
            if (paint.TryGetValue("circle-color", out var colorVal))
                props.CircleColor = ExpressionParser.Parse(colorVal, isColor: true);
            if (paint.TryGetValue("circle-opacity", out var opacity))
                props.CircleOpacity = ExpressionParser.Parse(opacity);
            if (paint.TryGetValue("circle-blur", out var blur))
                props.CircleBlur = ExpressionParser.Parse(blur);
            if (paint.TryGetValue("circle-stroke-width", out var strokeWidth))
                props.CircleStrokeWidth = ExpressionParser.Parse(strokeWidth);
            if (paint.TryGetValue("circle-stroke-color", out var strokeColor))
                props.CircleStrokeColor = ExpressionParser.Parse(strokeColor, isColor: true);
            if (paint.TryGetValue("circle-stroke-opacity", out var strokeOpacity))
                props.CircleStrokeOpacity = ExpressionParser.Parse(strokeOpacity);
            if (paint.TryGetValue("circle-translate", out var circleTranslate))
                props.CircleTranslate = ParseFloatArray(circleTranslate);
            if (paint.TryGetValue("circle-translate-anchor", out var circleTransAnchor))
                props.CircleTranslateAnchor = circleTransAnchor?.ToString() ?? "map";
            if (paint.TryGetValue("circle-pitch-alignment", out var pitchAlign))
                props.CirclePitchAlignment = pitchAlign?.ToString() ?? "viewport";
            if (paint.TryGetValue("circle-pitch-scale", out var pitchScale))
                props.CirclePitchScale = pitchScale?.ToString() ?? "map";

            return props;
        }

        public static FillExtrusionPaintProperties ParseFillExtrusionPaint(Dictionary<string, object> paint)
        {
            var props = new FillExtrusionPaintProperties();
            if (paint == null) return props;

            if (paint.TryGetValue("fill-extrusion-color", out var colorVal))
                props.FillExtrusionColor = ExpressionParser.Parse(colorVal, isColor: true);
            if (paint.TryGetValue("fill-extrusion-height", out var height))
                props.FillExtrusionHeight = ExpressionParser.Parse(height);
            if (paint.TryGetValue("fill-extrusion-base", out var baseVal))
                props.FillExtrusionBase = ExpressionParser.Parse(baseVal);
            if (paint.TryGetValue("fill-extrusion-opacity", out var opacity))
                props.FillExtrusionOpacity = ExpressionParser.Parse(opacity);
            if (paint.TryGetValue("fill-extrusion-vertical-scale", out var vertScale))
                props.FillExtrusionVerticalScale = ExpressionParser.Parse(vertScale);
            if (paint.TryGetValue("fill-extrusion-pattern", out var extPattern))
                props.FillExtrusionPattern = ExpressionParser.Parse(extPattern);

            return props;
        }

        public static HeatmapPaintProperties ParseHeatmapPaint(Dictionary<string, object> paint)
        {
            var props = new HeatmapPaintProperties();
            if (paint == null) return props;

            if (paint.TryGetValue("heatmap-radius", out var radius))
                props.HeatmapRadius = ExpressionParser.Parse(radius);
            if (paint.TryGetValue("heatmap-weight", out var weight))
                props.HeatmapWeight = ExpressionParser.Parse(weight);
            if (paint.TryGetValue("heatmap-intensity", out var intensity))
                props.HeatmapIntensity = ExpressionParser.Parse(intensity);
            if (paint.TryGetValue("heatmap-color", out var colorVal))
                props.HeatmapColor = ExpressionParser.Parse(colorVal, isColor: true);
            if (paint.TryGetValue("heatmap-opacity", out var opacity))
                props.HeatmapOpacity = ExpressionParser.Parse(opacity);

            return props;
        }

        public static HillshadePaintProperties ParseHillshadePaint(Dictionary<string, object> paint)
        {
            var props = new HillshadePaintProperties();
            if (paint == null) return props;

            if (paint.TryGetValue("hillshade-illumination-direction", out var dir))
                props.IlluminationDirection = ExpressionParser.Parse(dir);
            if (paint.TryGetValue("hillshade-illumination-anchor", out var anchor))
                props.IlluminationAnchor = anchor?.ToString() ?? "viewport";
            if (paint.TryGetValue("hillshade-exaggeration", out var exag))
                props.Exaggeration = ExpressionParser.Parse(exag);
            if (paint.TryGetValue("hillshade-shadow-color", out var shadow))
                props.ShadowColor = ExpressionParser.Parse(shadow, isColor: true);
            if (paint.TryGetValue("hillshade-highlight-color", out var highlight))
                props.HighlightColor = ExpressionParser.Parse(highlight, isColor: true);
            if (paint.TryGetValue("hillshade-accent-color", out var accent))
                props.AccentColor = ExpressionParser.Parse(accent, isColor: true);
            if (paint.TryGetValue("hillshade-opacity", out var opacity))
                props.Opacity = ExpressionParser.Parse(opacity);

            return props;
        }

        public static SymbolLayoutProperties ParseSymbolLayout(Dictionary<string, object> layout)
        {
            var props = new SymbolLayoutProperties();
            if (layout == null) return props;

            if (layout.TryGetValue("text-field", out var textField))
                props.TextField = ExpressionParser.Parse(textField);
            if (layout.TryGetValue("text-size", out var textSize))
                props.TextSize = ExpressionParser.Parse(textSize);
            if (layout.TryGetValue("text-font", out var textFont))
                props.TextFont = ExpressionParser.Parse(textFont);
            if (layout.TryGetValue("text-max-width", out var maxWidth))
                props.TextMaxWidth = ExpressionParser.Parse(maxWidth);
            if (layout.TryGetValue("text-anchor", out var anchor))
                props.TextAnchor = ExpressionParser.Parse(anchor);
            if (layout.TryGetValue("text-offset", out var textOffset))
                props.TextOffset = ExpressionParser.Parse(textOffset);
            if (layout.TryGetValue("icon-image", out var iconImage))
                props.IconImage = ExpressionParser.Parse(iconImage);
            if (layout.TryGetValue("icon-size", out var iconSize))
                props.IconSize = ExpressionParser.Parse(iconSize);
            if (layout.TryGetValue("icon-offset", out var iconOffset))
                props.IconOffset = ExpressionParser.Parse(iconOffset);
            if (layout.TryGetValue("icon-anchor", out var iconAnchor))
                props.IconAnchor = ExpressionParser.Parse(iconAnchor);
            if (layout.TryGetValue("icon-rotate", out var iconRotate))
                props.IconRotate = ExpressionParser.Parse(iconRotate);
            if (layout.TryGetValue("icon-padding", out var iconPadding))
                props.IconPadding = ExpressionParser.Parse(iconPadding);
            if (layout.TryGetValue("icon-allow-overlap", out var iconAllowOverlap))
                props.IconAllowOverlap = ExpressionParser.Parse(iconAllowOverlap);
            if (layout.TryGetValue("icon-ignore-placement", out var iconIgnore))
                props.IconIgnorePlacement = ExpressionParser.Parse(iconIgnore);
            if (layout.TryGetValue("text-padding", out var textPadding))
                props.TextPadding = ExpressionParser.Parse(textPadding);
            if (layout.TryGetValue("text-allow-overlap", out var textAllowOverlap))
                props.TextAllowOverlap = ExpressionParser.Parse(textAllowOverlap);
            if (layout.TryGetValue("text-ignore-placement", out var textIgnore))
                props.TextIgnorePlacement = ExpressionParser.Parse(textIgnore);
            if (layout.TryGetValue("symbol-sort-key", out var sortKey))
                props.SymbolSortKey = ExpressionParser.Parse(sortKey);
            if (layout.TryGetValue("symbol-placement", out var placement))
                props.SymbolPlacement = placement?.ToString() ?? "point";
            if (layout.TryGetValue("symbol-spacing", out var symbolSpacing))
                props.SymbolSpacing = ExpressionParser.Parse(symbolSpacing);

            if (layout.TryGetValue("text-rotation-alignment", out var textRotAlign))
                props.TextRotationAlignment = textRotAlign?.ToString() ?? "auto";
            if (layout.TryGetValue("text-pitch-alignment", out var textPitchAlign))
                props.TextPitchAlignment = textPitchAlign?.ToString() ?? "auto";
            if (layout.TryGetValue("icon-rotation-alignment", out var iconRotAlign))
                props.IconRotationAlignment = iconRotAlign?.ToString() ?? "auto";
            if (layout.TryGetValue("icon-pitch-alignment", out var iconPitchAlign))
                props.IconPitchAlignment = iconPitchAlign?.ToString() ?? "auto";
            if (layout.TryGetValue("text-keep-upright", out var textKeep))
                props.TextKeepUpright = Convert.ToBoolean(textKeep);
            if (layout.TryGetValue("icon-keep-upright", out var iconKeep))
                props.IconKeepUpright = Convert.ToBoolean(iconKeep);

            if (layout.TryGetValue("icon-text-fit", out var iconTextFit))
                props.IconTextFit = iconTextFit?.ToString() ?? "none";
            if (layout.TryGetValue("icon-text-fit-padding", out var fitPadding))
            {
                // Spec: array of 4 numbers, [top, right, bottom, left]. Some
                // shorter forms are allowed; normalise to length 4 with the
                // missing entries copied from the previous (CSS-style).
                if (fitPadding is List<object> list && list.Count > 0)
                {
                    var p = new float[4];
                    for (int i = 0; i < 4; i++)
                        p[i] = Convert.ToSingle(list[Math.Min(i, list.Count - 1)]);
                    props.IconTextFitPadding = p;
                }
            }

            return props;
        }

        public static SymbolPaintProperties ParseSymbolPaint(Dictionary<string, object> paint)
        {
            var props = new SymbolPaintProperties();
            if (paint == null) return props;

            if (paint.TryGetValue("text-color", out var colorVal))
                props.TextColor = ExpressionParser.Parse(colorVal, isColor: true);
            if (paint.TryGetValue("text-opacity", out var opacity))
                props.TextOpacity = ExpressionParser.Parse(opacity);
            if (paint.TryGetValue("text-halo-color", out var haloColor))
                props.TextHaloColor = ExpressionParser.Parse(haloColor, isColor: true);
            if (paint.TryGetValue("text-halo-width", out var haloWidth))
                props.TextHaloWidth = ExpressionParser.Parse(haloWidth);
            if (paint.TryGetValue("icon-opacity", out var iconOpacity))
                props.IconOpacity = ExpressionParser.Parse(iconOpacity);
            if (paint.TryGetValue("icon-color", out var iconColor))
                props.IconColor = ExpressionParser.Parse(iconColor, isColor: true);
            if (paint.TryGetValue("icon-halo-color", out var iconHaloColor))
                props.IconHaloColor = ExpressionParser.Parse(iconHaloColor, isColor: true);
            if (paint.TryGetValue("icon-halo-width", out var iconHaloWidth))
                props.IconHaloWidth = ExpressionParser.Parse(iconHaloWidth);

            if (paint.TryGetValue("text-translate", out var textTranslate))
                props.TextTranslate = ParseFloatArray(textTranslate);
            if (paint.TryGetValue("text-translate-anchor", out var textTransAnchor))
                props.TextTranslateAnchor = textTransAnchor?.ToString() ?? "map";
            if (paint.TryGetValue("icon-translate", out var iconTranslate))
                props.IconTranslate = ParseFloatArray(iconTranslate);
            if (paint.TryGetValue("icon-translate-anchor", out var iconTransAnchor))
                props.IconTranslateAnchor = iconTransAnchor?.ToString() ?? "map";

            return props;
        }

        /// <summary>
        /// Parse a JSON value as a float array (e.g. line-dasharray: [2, 1]).
        /// Returns null if the value is not a valid array.
        /// </summary>
        private static float[] ParseFloatArray(object value)
        {
            if (value is JArray jArr)
            {
                var result = new float[jArr.Count];
                for (int i = 0; i < jArr.Count; i++)
                    result[i] = jArr[i].ToObject<float>();
                return result.Length > 0 ? result : null;
            }

            if (value is List<object> list)
            {
                var result = new float[list.Count];
                for (int i = 0; i < list.Count; i++)
                    result[i] = Convert.ToSingle(list[i]);
                return result.Length > 0 ? result : null;
            }

            return null;
        }

        public static Color ParseColor(string colorStr)
        {
            if (string.IsNullOrEmpty(colorStr))
                return Color.black;

            colorStr = colorStr.Trim();

            // #RRGGBB or #RGB
            if (colorStr.StartsWith("#"))
            {
                string hex = colorStr.Substring(1);
                if (hex.Length == 3)
                {
                    hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
                }
                if (hex.Length == 6 || hex.Length == 8)
                {
                    if (ColorUtility.TryParseHtmlString("#" + hex, out Color c))
                        return c;
                }
            }

            // rgb(r,g,b) or rgba(r,g,b,a)
            if (colorStr.StartsWith("rgb"))
            {
                int start = colorStr.IndexOf('(') + 1;
                int end = colorStr.IndexOf(')');
                if (start > 0 && end > start)
                {
                    string[] parts = colorStr.Substring(start, end - start).Split(',');
                    if (parts.Length >= 3)
                    {
                        float r = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture) / 255f;
                        float g = float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture) / 255f;
                        float b = float.Parse(parts[2].Trim(), CultureInfo.InvariantCulture) / 255f;
                        float a = parts.Length >= 4
                            ? float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture)
                            : 1f;
                        return new Color(r, g, b, a);
                    }
                }
            }

            // hsl(h,s%,l%) or hsla(h,s%,l%,a)
            if (colorStr.StartsWith("hsl"))
            {
                int start = colorStr.IndexOf('(') + 1;
                int end = colorStr.IndexOf(')');
                if (start > 0 && end > start)
                {
                    string[] parts = colorStr.Substring(start, end - start).Split(',');
                    if (parts.Length >= 3)
                    {
                        float h = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture) / 360f;
                        float s = float.Parse(parts[1].Trim().TrimEnd('%'), CultureInfo.InvariantCulture) / 100f;
                        float l = float.Parse(parts[2].Trim().TrimEnd('%'), CultureInfo.InvariantCulture) / 100f;
                        float a = parts.Length >= 4
                            ? float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture)
                            : 1f;
                        return HslToRgb(h, s, l, a);
                    }
                }
            }

            // Named CSS colors (common ones)
            return colorStr.ToLowerInvariant() switch
            {
                "white" => Color.white,
                "black" => Color.black,
                "red" => Color.red,
                "green" => Color.green,
                "blue" => Color.blue,
                "yellow" => Color.yellow,
                "cyan" => Color.cyan,
                "magenta" => Color.magenta,
                "transparent" => new Color(0, 0, 0, 0),
                "gray" or "grey" => Color.gray,
                "lightgray" or "lightgrey" => new Color(0.827f, 0.827f, 0.827f),
                "darkgray" or "darkgrey" => new Color(0.663f, 0.663f, 0.663f),
                "orange" => new Color(1f, 0.647f, 0f),
                "purple" => new Color(0.502f, 0f, 0.502f),
                "brown" => new Color(0.647f, 0.165f, 0.165f),
                "pink" => new Color(1f, 0.753f, 0.796f),
                "navy" => new Color(0f, 0f, 0.502f),
                "steelblue" => new Color(0.275f, 0.51f, 0.706f),
                _ => Color.black
            };
        }

        private static Color HslToRgb(float h, float s, float l, float a)
        {
            float r, g, b;
            if (s == 0f)
            {
                r = g = b = l;
            }
            else
            {
                float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
                float p = 2f * l - q;
                r = HueToRgb(p, q, h + 1f / 3f);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1f / 3f);
            }
            return new Color(r, g, b, a);
        }

        private static float HueToRgb(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }
    }
}
