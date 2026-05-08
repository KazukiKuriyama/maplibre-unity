using System;
using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using MapLibre.Unity.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Interactive demo for setPaintProperty() / setLayoutProperty() API.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class PropertyDemo : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;

        private MapLibreMap _map;
        private Label _logLabel;
        private Font _font;
        private readonly List<string> _logLines = new();
        private const int MaxLogLines = 8;

        private bool _parksVisible = true;
        private int _fillColorIndex;
        private int _outlineWidthIndex;
        private int _circleColorIndex;
        private int _textSizeIndex;

        private static readonly string[] FillColors = { "#4CAF50", "#2196F3", "#FF9800", "#9C27B0", "#F44336" };
        private static readonly object[] OutlineWidths = { 2, 4, 6, 8 };
        private static readonly string[] CircleColors = { "#E91E63", "#00BCD4", "#FF5722", "#3F51B5", "#8BC34A" };
        private static readonly object[] TextSizes = { 14, 18, 22, 12 };

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[PropertyDemo] MapLibreMap not found");
                yield break;
            }

            while (!_map.IsInitialized)
                yield return null;

            yield return null;

            // Add demo sources and layers
            SetupDemoData();

            _font = SystemFontFallback.Resolve("Arial", 16);
            BuildUI();
        }

        private void SetupDemoData()
        {
            // Parks source (polygons)
            _map.AddSource("parks", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        {
                            ""type"": ""Feature"",
                            ""geometry"": {
                                ""type"": ""Polygon"",
                                ""coordinates"": [[[139.7594,35.6762],[139.7594,35.6832],[139.7708,35.6832],[139.7708,35.6762],[139.7594,35.6762]]]
                            },
                            ""properties"": { ""name"": ""Imperial Palace"" }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": {
                                ""type"": ""Polygon"",
                                ""coordinates"": [[[139.6950,35.6715],[139.6950,35.6780],[139.7045,35.6780],[139.7045,35.6715],[139.6950,35.6715]]]
                            },
                            ""properties"": { ""name"": ""Yoyogi Park"" }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": {
                                ""type"": ""Polygon"",
                                ""coordinates"": [[[139.7700,35.7100],[139.7700,35.7180],[139.7820,35.7180],[139.7820,35.7100],[139.7700,35.7100]]]
                            },
                            ""properties"": { ""name"": ""Ueno Park"" }
                        }
                    ]
                }")
            });

            // Stations source (points)
            _map.AddSource("stations", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7671,35.6812] },
                            ""properties"": { ""name"": ""Tokyo"" }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7016,35.6580] },
                            ""properties"": { ""name"": ""Shibuya"" }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7003,35.6896] },
                            ""properties"": { ""name"": ""Shinjuku"" }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7771,35.7141] },
                            ""properties"": { ""name"": ""Ueno"" }
                        }
                    ]
                }")
            });

            // Add layers
            _map.AddLayer(new LayerDefinition
            {
                Id = "parks-fill",
                Type = LayerType.Fill,
                Source = "parks",
                SourceLayer = "parks",
                Paint = new Dictionary<string, object>
                {
                    { "fill-color", "#4CAF50" },
                    { "fill-opacity", 0.5 }
                }
            }, triggerRefresh: false);

            _map.AddLayer(new LayerDefinition
            {
                Id = "parks-outline",
                Type = LayerType.Line,
                Source = "parks",
                SourceLayer = "parks",
                Paint = new Dictionary<string, object>
                {
                    { "line-color", "#2E7D32" },
                    { "line-width", 2 },
                    { "line-opacity", 0.9 }
                }
            }, triggerRefresh: false);

            _map.AddLayer(new LayerDefinition
            {
                Id = "stations-circle",
                Type = LayerType.Circle,
                Source = "stations",
                SourceLayer = "stations",
                Paint = new Dictionary<string, object>
                {
                    { "circle-radius", 8 },
                    { "circle-color", "#E91E63" },
                    { "circle-stroke-width", 2 },
                    { "circle-stroke-color", "#ffffff" }
                }
            }, triggerRefresh: false);

            _map.AddLayer(new LayerDefinition
            {
                Id = "stations-label",
                Type = LayerType.Symbol,
                Source = "stations",
                SourceLayer = "stations",
                Layout = new Dictionary<string, object>
                {
                    { "text-field", "{name}" },
                    { "text-size", 14 }
                },
                Paint = new Dictionary<string, object>
                {
                    { "text-color", "#1A237E" },
                    { "text-halo-color", "#ffffff" },
                    { "text-halo-width", 1.5 }
                }
            }, triggerRefresh: false);

            _map.RefreshTiles();
        }

        private void BuildUI()
        {
            var go = new GameObject("PropertyDemoUI");
            go.transform.SetParent(transform);

            var uiDoc = go.AddComponent<UIDocument>();
            if (_panelSettings != null)
                uiDoc.panelSettings = _panelSettings;
            uiDoc.sortingOrder = 200;

            StartCoroutine(BuildAfterFrame(uiDoc));
        }

        private IEnumerator BuildAfterFrame(UIDocument uiDoc)
        {
            yield return null;

            var root = uiDoc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;

            // Panel container -- viewport-relative width, capped at the
            // original 480px so it fits comfortably on a 1920×1080 monitor
            // without crowding smaller displays.
            var panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = new Length(50, LengthUnit.Percent),
                    translate = new Translate(0, new Length(-50, LengthUnit.Percent)),
                    left = 14,
                    width = new Length(36, LengthUnit.Percent),
                    maxWidth = 480,
                    minWidth = 280,
                    maxHeight = new Length(80, LengthUnit.Percent),
                    backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.88f),
                    borderTopLeftRadius = 10,
                    borderTopRightRadius = 10,
                    borderBottomLeftRadius = 10,
                    borderBottomRightRadius = 10,
                    paddingTop = 14,
                    paddingBottom = 14,
                    paddingLeft = 14,
                    paddingRight = 14,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.12f),
                    borderBottomColor = new Color(1, 1, 1, 0.12f),
                    borderLeftColor = new Color(1, 1, 1, 0.12f),
                    borderRightColor = new Color(1, 1, 1, 0.12f)
                }
            };
            root.Add(panel);

            // Title
            panel.Add(MakeLabel("setPaintProperty / setLayoutProperty", 14, Color.white, FontStyle.Bold));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;
            SampleScrollViewStyle.Apply(scroll);
            panel.Add(scroll);

            // --- setPaintProperty section ---
            scroll.Add(MakeSectionLabel("setPaintProperty"));
            scroll.Add(MakeButton("fill-color (cycle)", new Color(0.22f, 0.40f, 0.22f), OnCycleFillColor));
            scroll.Add(MakeButton("fill-opacity = 0.8", new Color(0.22f, 0.40f, 0.22f), OnSetFillOpacity));
            scroll.Add(MakeButton("line-width (cycle)", new Color(0.22f, 0.40f, 0.22f), OnCycleLineWidth));
            scroll.Add(MakeButton("circle-color (cycle)", new Color(0.22f, 0.40f, 0.22f), OnCycleCircleColor));
            scroll.Add(MakeButton("circle-radius = 14", new Color(0.22f, 0.40f, 0.22f), OnSetCircleRadius));
            scroll.Add(MakeButton("text-color = red", new Color(0.22f, 0.40f, 0.22f), OnSetTextColorRed));
            scroll.Add(MakeButton("background-color (toggle)", new Color(0.22f, 0.40f, 0.22f), OnToggleBackgroundColor));

            // --- setLayoutProperty section ---
            scroll.Add(MakeSectionLabel("setLayoutProperty"));
            scroll.Add(MakeButton("visibility: parks-fill (toggle)", new Color(0.22f, 0.28f, 0.52f), OnToggleParksVisibility));
            scroll.Add(MakeButton("text-size (cycle)", new Color(0.22f, 0.28f, 0.52f), OnCycleTextSize));

            // --- getPaintProperty / getLayoutProperty section ---
            scroll.Add(MakeSectionLabel("getPaintProperty / getLayoutProperty"));
            scroll.Add(MakeButton("getPaintProperty('parks-fill','fill-color')", new Color(0.20f, 0.39f, 0.31f), OnGetFillColor));
            scroll.Add(MakeButton("getLayoutProperty('parks-fill','visibility')", new Color(0.20f, 0.39f, 0.31f), OnGetVisibility));

            // Log area
            _logLabel = new Label();
            _logLabel.style.fontSize = 10;
            _logLabel.style.color = new Color(0.7f, 1f, 0.7f, 0.9f);
            _logLabel.style.backgroundColor = new Color(0, 0, 0, 0.4f);
            _logLabel.style.borderTopLeftRadius = 4;
            _logLabel.style.borderTopRightRadius = 4;
            _logLabel.style.borderBottomLeftRadius = 4;
            _logLabel.style.borderBottomRightRadius = 4;
            _logLabel.style.paddingTop = 6;
            _logLabel.style.paddingBottom = 6;
            _logLabel.style.paddingLeft = 6;
            _logLabel.style.paddingRight = 6;
            _logLabel.style.marginTop = 6;
            _logLabel.style.minHeight = 60;
            _logLabel.style.whiteSpace = WhiteSpace.Normal;
            _logLabel.style.unityFontDefinition = StyleKeyword.None;
            _logLabel.style.unityFont = new StyleFont(_font);
            _logLabel.pickingMode = PickingMode.Ignore;
            panel.Add(_logLabel);

            Log("Ready. Layers: parks-fill, parks-outline, stations-circle, stations-label");
        }

        // --- setPaintProperty handlers ---

        private void OnCycleFillColor()
        {
            _fillColorIndex = (_fillColorIndex + 1) % FillColors.Length;
            var color = FillColors[_fillColorIndex];
            _map.SetPaintProperty("parks-fill", "fill-color", color);
            Log($"setPaintProperty('parks-fill','fill-color','{color}')");
        }

        private void OnSetFillOpacity()
        {
            _map.SetPaintProperty("parks-fill", "fill-opacity", 0.8);
            Log("setPaintProperty('parks-fill','fill-opacity', 0.8)");
        }

        private void OnCycleLineWidth()
        {
            _outlineWidthIndex = (_outlineWidthIndex + 1) % OutlineWidths.Length;
            var width = OutlineWidths[_outlineWidthIndex];
            _map.SetPaintProperty("parks-outline", "line-width", width);
            Log($"setPaintProperty('parks-outline','line-width', {width})");
        }

        private void OnCycleCircleColor()
        {
            _circleColorIndex = (_circleColorIndex + 1) % CircleColors.Length;
            var color = CircleColors[_circleColorIndex];
            _map.SetPaintProperty("stations-circle", "circle-color", color);
            Log($"setPaintProperty('stations-circle','circle-color','{color}')");
        }

        private void OnSetCircleRadius()
        {
            _map.SetPaintProperty("stations-circle", "circle-radius", 14);
            Log("setPaintProperty('stations-circle','circle-radius', 14)");
        }

        private void OnSetTextColorRed()
        {
            _map.SetPaintProperty("stations-label", "text-color", "#F44336");
            Log("setPaintProperty('stations-label','text-color','#F44336')");
        }

        private bool _bgToggle;
        private void OnToggleBackgroundColor()
        {
            _bgToggle = !_bgToggle;
            var color = _bgToggle ? "#263238" : "#f0f0f0";
            _map.SetPaintProperty("background", "background-color", color);
            Log($"setPaintProperty('background','background-color','{color}')");
        }

        // --- setLayoutProperty handlers ---

        private void OnToggleParksVisibility()
        {
            _parksVisible = !_parksVisible;
            var vis = _parksVisible ? "visible" : "none";
            _map.SetLayoutProperty("parks-fill", "visibility", vis);
            Log($"setLayoutProperty('parks-fill','visibility','{vis}')");
        }

        private void OnCycleTextSize()
        {
            _textSizeIndex = (_textSizeIndex + 1) % TextSizes.Length;
            var size = TextSizes[_textSizeIndex];
            _map.SetLayoutProperty("stations-label", "text-size", size);
            Log($"setLayoutProperty('stations-label','text-size', {size})");
        }

        // --- get handlers ---

        private void OnGetFillColor()
        {
            var val = _map.GetPaintProperty("parks-fill", "fill-color");
            Log($"getPaintProperty('parks-fill','fill-color') = {val ?? "null"}");
        }

        private void OnGetVisibility()
        {
            var val = _map.GetLayoutProperty("parks-fill", "visibility");
            Log($"getLayoutProperty('parks-fill','visibility') = {val ?? "null"}");
        }

        // --- UI helpers ---

        private Label MakeLabel(string text, int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            var label = new Label(text);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = fontStyle;
            label.style.marginBottom = 6;
            label.style.unityFontDefinition = StyleKeyword.None;
            label.style.unityFont = new StyleFont(_font);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private Label MakeSectionLabel(string text)
        {
            var label = new Label(text)
            {
                style =
                {
                    fontSize = 11,
                    color = new Color(1, 1, 1, 0.5f),
                    unityFontStyleAndWeight = FontStyle.Italic,
                    marginTop = 5,
                    marginBottom = 3,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font)
                },
                pickingMode = PickingMode.Ignore
            };
            return label;
        }

        private Button MakeButton(string text, Color bgColor, Action onClick)
        {
            var btn = new Button();
            btn.text = text;
            btn.style.height = 24;
            btn.style.marginTop = 1;
            btn.style.marginBottom = 1;
            btn.style.borderTopLeftRadius = 4;
            btn.style.borderTopRightRadius = 4;
            btn.style.borderBottomLeftRadius = 4;
            btn.style.borderBottomRightRadius = 4;
            btn.style.fontSize = 11;
            btn.style.color = Color.white;
            btn.style.backgroundColor = bgColor;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.unityFontDefinition = StyleKeyword.None;
            btn.style.unityFont = new StyleFont(_font);
            btn.clicked += onClick;
            return btn;
        }

        private void Log(string message)
        {
            Debug.Log($"[PropertyDemo] {message}");
            _logLines.Add(message);
            while (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);

            if (_logLabel != null)
                _logLabel.text = string.Join("\n", _logLines);
        }
    }
}
