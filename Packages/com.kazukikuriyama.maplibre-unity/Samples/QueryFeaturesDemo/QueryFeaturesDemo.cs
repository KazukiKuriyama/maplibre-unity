using System.Collections;
using System.Collections.Generic;
using System.Text;
using MapLibre.Unity.Style;
using MapLibre.Unity.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Interactive demo for queryRenderedFeatures() and querySourceFeatures().
    /// Click on the map to query features at that point. Results are shown in a UITK panel.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class QueryFeaturesDemo : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;

        private MapLibreMap _map;
        private Label _resultLabel;
        private Label _coordLabel;
        private Label _featureCountLabel;
        private Font _font;
        private VisualElement _panel;
        private ScrollView _scrollView;
        private bool _queryAllLayers = true;
        private string _selectedLayerFilter;
        private readonly List<(Button Button, string LayerId)> _filterButtons = new();
        private static readonly Color FilterActiveBg = new(0.22f, 0.28f, 0.52f);
        private static readonly Color FilterInactiveBg = new(0.3f, 0.3f, 0.3f);

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[QueryFeaturesDemo] MapLibreMap not found");
                yield break;
            }

            while (!_map.IsInitialized)
                yield return null;

            // Yield a couple of frames so the basemap has time to start fetching
            // before the demo wires its overlays / event handlers on top.
            yield return null;
            yield return null;

            // Add GeoJSON data sources for demo
            AddDemoSources();
            yield return null;
            yield return null;
            AddDemoLayers();

            _font = SystemFontFallback.Resolve("Arial", 16);
            BuildUI();
        }

        private void AddDemoSources()
        {
            _map.AddSource("demo-parks", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        {
                            ""type"": ""Feature"",
                            ""id"": 1,
                            ""geometry"": {
                                ""type"": ""Polygon"",
                                ""coordinates"": [[[139.7594,35.6762],[139.7594,35.6832],[139.7708,35.6832],[139.7708,35.6762],[139.7594,35.6762]]]
                            },
                            ""properties"": { ""name"": ""Imperial Palace"", ""area_ha"": 115, ""type"": ""park"" }
                        },
                        {
                            ""type"": ""Feature"",
                            ""id"": 2,
                            ""geometry"": {
                                ""type"": ""Polygon"",
                                ""coordinates"": [[[139.6950,35.6715],[139.6950,35.6780],[139.7045,35.6780],[139.7045,35.6715],[139.6950,35.6715]]]
                            },
                            ""properties"": { ""name"": ""Yoyogi Park"", ""area_ha"": 54, ""type"": ""park"" }
                        },
                        {
                            ""type"": ""Feature"",
                            ""id"": 3,
                            ""geometry"": {
                                ""type"": ""Polygon"",
                                ""coordinates"": [[[139.7700,35.7100],[139.7700,35.7180],[139.7820,35.7180],[139.7820,35.7100],[139.7700,35.7100]]]
                            },
                            ""properties"": { ""name"": ""Ueno Park"", ""area_ha"": 53, ""type"": ""park"" }
                        }
                    ]
                }")
            });

            _map.AddSource("demo-stations", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        {
                            ""type"": ""Feature"",
                            ""id"": 10,
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7671, 35.6812] },
                            ""properties"": { ""name"": ""Tokyo"", ""line"": ""Yamanote"", ""passengers"": 462000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""id"": 11,
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7016, 35.6580] },
                            ""properties"": { ""name"": ""Shibuya"", ""line"": ""Yamanote"", ""passengers"": 366000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""id"": 12,
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7003, 35.6896] },
                            ""properties"": { ""name"": ""Shinjuku"", ""line"": ""Yamanote"", ""passengers"": 775000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""id"": 13,
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7101, 35.7296] },
                            ""properties"": { ""name"": ""Ikebukuro"", ""line"": ""Yamanote"", ""passengers"": 558000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""id"": 14,
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7771, 35.7141] },
                            ""properties"": { ""name"": ""Ueno"", ""line"": ""Yamanote"", ""passengers"": 187000 }
                        }
                    ]
                }")
            });

            _map.AddSource("demo-route", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""Feature"",
                    ""id"": 100,
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7671, 35.6812],
                            [139.7584, 35.6753],
                            [139.7456, 35.6586],
                            [139.7016, 35.6580],
                            [139.7003, 35.6896],
                            [139.7101, 35.7296],
                            [139.7771, 35.7141],
                            [139.7671, 35.6812]
                        ]
                    },
                    ""properties"": { ""name"": ""Yamanote Line"", ""type"": ""railway"" }
                }")
            });
        }

        private void AddDemoLayers()
        {
            // Fill layer for parks
            _map.AddLayer(new LayerDefinition
            {
                Id = "parks-fill",
                Type = LayerType.Fill,
                Source = "demo-parks",
                SourceLayer = "demo-parks",
                Paint = new Dictionary<string, object>
                {
                    { "fill-color", "#4CAF50" },
                    { "fill-opacity", 0.4 }
                }
            }, triggerRefresh: false);

            // Outline for parks
            _map.AddLayer(new LayerDefinition
            {
                Id = "parks-outline",
                Type = LayerType.Line,
                Source = "demo-parks",
                SourceLayer = "demo-parks",
                Paint = new Dictionary<string, object>
                {
                    { "line-color", "#2E7D32" },
                    { "line-width", 3 },
                    { "line-opacity", 0.9 }
                }
            }, triggerRefresh: false);

            // Route line
            _map.AddLayer(new LayerDefinition
            {
                Id = "route-line",
                Type = LayerType.Line,
                Source = "demo-route",
                SourceLayer = "demo-route",
                Paint = new Dictionary<string, object>
                {
                    { "line-color", "#FF9800" },
                    { "line-width", 5 },
                    { "line-opacity", 0.8 }
                }
            }, triggerRefresh: false);

            // Station circles
            _map.AddLayer(new LayerDefinition
            {
                Id = "station-circles",
                Type = LayerType.Circle,
                Source = "demo-stations",
                SourceLayer = "demo-stations",
                Paint = new Dictionary<string, object>
                {
                    { "circle-radius", 10 },
                    { "circle-color", "#E91E63" },
                    { "circle-opacity", 0.9 },
                    { "circle-stroke-width", 2 },
                    { "circle-stroke-color", "#ffffff" }
                }
            }, triggerRefresh: false);

            // Station labels
            _map.AddLayer(new LayerDefinition
            {
                Id = "station-labels",
                Type = LayerType.Symbol,
                Source = "demo-stations",
                SourceLayer = "demo-stations",
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
            var go = new GameObject("QueryFeaturesUI");
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

            // Main panel (right side). Width is viewport-relative so the
            // panel doesn't dominate the screen on small / portrait devices,
            // capped at the original 380px so it stays readable on 4K.
            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.top = new Length(50, LengthUnit.Percent);
            _panel.style.translate = new Translate(0, new Length(-50, LengthUnit.Percent));
            _panel.style.right = 14;
            _panel.style.width = new Length(30, LengthUnit.Percent);
            _panel.style.maxWidth = 380;
            _panel.style.minWidth = 240;
            _panel.style.maxHeight = new Length(80, LengthUnit.Percent);
            _panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.92f);
            _panel.style.borderTopLeftRadius = 10;
            _panel.style.borderTopRightRadius = 10;
            _panel.style.borderBottomLeftRadius = 10;
            _panel.style.borderBottomRightRadius = 10;
            _panel.style.paddingTop = 14;
            _panel.style.paddingBottom = 14;
            _panel.style.paddingLeft = 14;
            _panel.style.paddingRight = 14;
            _panel.style.borderTopWidth = 1;
            _panel.style.borderBottomWidth = 1;
            _panel.style.borderLeftWidth = 1;
            _panel.style.borderRightWidth = 1;
            _panel.style.borderTopColor = new Color(1, 1, 1, 0.12f);
            _panel.style.borderBottomColor = new Color(1, 1, 1, 0.12f);
            _panel.style.borderLeftColor = new Color(1, 1, 1, 0.12f);
            _panel.style.borderRightColor = new Color(1, 1, 1, 0.12f);
            _panel.style.overflow = Overflow.Hidden;
            root.Add(_panel);

            // Title
            _panel.Add(MakeLabel("queryRenderedFeatures Demo", 14, Color.white, FontStyle.Bold));
            _panel.Add(MakeLabel("Click on the map to query features", 11, new Color(1, 1, 1, 0.6f)));

            // Coordinate display
            _coordLabel = MakeLabel("", 11, new Color(0.6f, 0.8f, 1f));
            _coordLabel.style.marginTop = 6;
            _panel.Add(_coordLabel);

            // Feature count
            _featureCountLabel = MakeLabel("", 12, new Color(1f, 0.85f, 0.4f), FontStyle.Bold);
            _featureCountLabel.style.marginTop = 2;
            _panel.Add(_featureCountLabel);

            // Layer filter buttons
            _panel.Add(MakeSectionLabel("Layer Filter"));
            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.flexWrap = Wrap.Wrap;
            filterRow.style.flexShrink = 0;
            filterRow.Add(MakeFilterButton("All", null));
            filterRow.Add(MakeFilterButton("Parks", "parks-fill"));
            filterRow.Add(MakeFilterButton("Route", "route-line"));
            filterRow.Add(MakeFilterButton("Stations", "station-circles"));
            _panel.Add(filterRow);

            // querySourceFeatures button
            _panel.Add(MakeSectionLabel("querySourceFeatures"));
            var srcRow = new VisualElement();
            srcRow.style.flexDirection = FlexDirection.Row;
            srcRow.style.flexWrap = Wrap.Wrap;
            srcRow.style.flexShrink = 0;
            srcRow.Add(MakeActionButton("demo-parks", new Color(0.20f, 0.39f, 0.31f), () => OnQuerySource("demo-parks")));
            srcRow.Add(MakeActionButton("demo-stations", new Color(0.20f, 0.39f, 0.31f), () => OnQuerySource("demo-stations")));
            srcRow.Add(MakeActionButton("demo-route", new Color(0.20f, 0.39f, 0.31f), () => OnQuerySource("demo-route")));
            _panel.Add(srcRow);

            // Results scroll view. flexGrow=1 so it claims remaining vertical
            // space inside the panel; minHeight=0 lets it shrink instead of
            // pushing the panel past its maxHeight (which previously caused
            // the result rows to render on top of the buttons above).
            _panel.Add(MakeSectionLabel("Results"));
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            SampleScrollViewStyle.Apply(_scrollView);
            _scrollView.style.flexGrow = 1;
            _scrollView.style.flexShrink = 1;
            _scrollView.style.minHeight = 0;
            _scrollView.style.maxHeight = 180;
            _scrollView.style.backgroundColor = new Color(0, 0, 0, 0.3f);
            _scrollView.style.borderTopLeftRadius = 6;
            _scrollView.style.borderTopRightRadius = 6;
            _scrollView.style.borderBottomLeftRadius = 6;
            _scrollView.style.borderBottomRightRadius = 6;
            _scrollView.style.paddingTop = 6;
            _scrollView.style.paddingBottom = 6;
            _scrollView.style.paddingLeft = 8;
            _scrollView.style.paddingRight = 8;
            _panel.Add(_scrollView);

            _resultLabel = new Label("Click on the map...");
            _resultLabel.style.fontSize = 10;
            _resultLabel.style.color = new Color(0.8f, 1f, 0.8f, 0.9f);
            _resultLabel.style.whiteSpace = WhiteSpace.Normal;
            _resultLabel.style.overflow = Overflow.Hidden;
            _resultLabel.style.maxWidth = new Length(100, LengthUnit.Percent);
            _resultLabel.style.unityFontDefinition = StyleKeyword.None;
            _resultLabel.style.unityFont = new StyleFont(_font);
            _resultLabel.pickingMode = PickingMode.Ignore;
            _scrollView.Add(_resultLabel);

            // Register click handler
            _map.On(MapEventType.Click, OnMapClick);
        }

        private void OnMapClick(MapEvent e)
        {
            if (!e.Point.HasValue || !e.LngLat.HasValue) return;

            var screenPoint = e.Point.Value;
            var lngLat = e.LngLat.Value;

            // Build query options
            QueryOptions options = null;
            if (!_queryAllLayers && !string.IsNullOrEmpty(_selectedLayerFilter))
            {
                options = new QueryOptions { Layers = new[] { _selectedLayerFilter } };
            }

            var features = _map.QueryRenderedFeatures(screenPoint, options);

            // Update coordinate display
            _coordLabel.text = $"LngLat: ({lngLat.Longitude:F5}, {lngLat.Latitude:F5})  Screen: ({screenPoint.x:F0}, {screenPoint.y:F0})";

            // Update feature count
            _featureCountLabel.text = $"Features found: {features.Count}";

            // Build results text
            if (features.Count == 0)
            {
                _resultLabel.text = "No features at this location.";
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < features.Count; i++)
            {
                var f = features[i];
                sb.AppendLine($"--- Feature #{i + 1} ---");
                sb.AppendLine($"  Layer: {f.Layer?.Id ?? "(null)"}");
                sb.AppendLine($"  Source: {f.Source}");
                sb.AppendLine($"  SourceLayer: {f.SourceLayer}");
                sb.AppendLine($"  Geometry: {f.GeometryType}");
                sb.AppendLine($"  ID: {f.Id}");
                sb.AppendLine("  Properties:");
                if (f.Properties != null)
                {
                    foreach (var kvp in f.Properties)
                    {
                        sb.AppendLine($"    {kvp.Key}: {kvp.Value}");
                    }
                }
                sb.AppendLine();
            }

            _resultLabel.text = sb.ToString();
            Debug.Log($"[QueryFeaturesDemo] Click at {lngLat} -- {features.Count} features found");
        }

        private void OnQuerySource(string sourceId)
        {
            var features = _map.QuerySourceFeatures(sourceId, new QuerySourceOptions
            {
                SourceLayer = sourceId
            });

            _coordLabel.text = $"querySourceFeatures(\"{sourceId}\")";
            _featureCountLabel.text = $"Features found: {features.Count}";

            if (features.Count == 0)
            {
                _resultLabel.text = "No features in source cache.";
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < features.Count; i++)
            {
                var f = features[i];
                sb.AppendLine($"--- Feature #{i + 1} ---");
                sb.AppendLine($"  Geometry: {f.GeometryType}");
                sb.AppendLine($"  ID: {f.Id}");
                sb.AppendLine("  Properties:");
                if (f.Properties != null)
                {
                    foreach (var kvp in f.Properties)
                    {
                        sb.AppendLine($"    {kvp.Key}: {kvp.Value}");
                    }
                }
                sb.AppendLine();
            }

            _resultLabel.text = sb.ToString();
            Debug.Log($"[QueryFeaturesDemo] querySourceFeatures(\"{sourceId}\") -- {features.Count} features");
        }

        private Label MakeLabel(string text, int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            var label = new Label(text);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = fontStyle;
            label.style.marginBottom = 2;
            label.style.unityFontDefinition = StyleKeyword.None;
            label.style.unityFont = new StyleFont(_font);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private Label MakeSectionLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.color = new Color(1, 1, 1, 0.5f);
            label.style.unityFontStyleAndWeight = FontStyle.Italic;
            label.style.marginTop = 6;
            label.style.marginBottom = 2;
            label.style.unityFontDefinition = StyleKeyword.None;
            label.style.unityFont = new StyleFont(_font);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private Button MakeFilterButton(string text, string layerId)
        {
            var btn = new Button();
            btn.text = text;
            btn.style.height = 22;
            btn.style.marginTop = 2;
            btn.style.marginBottom = 2;
            btn.style.marginRight = 3;
            btn.style.paddingLeft = 8;
            btn.style.paddingRight = 8;
            btn.style.borderTopLeftRadius = 4;
            btn.style.borderTopRightRadius = 4;
            btn.style.borderBottomLeftRadius = 4;
            btn.style.borderBottomRightRadius = 4;
            btn.style.fontSize = 11;
            btn.style.color = Color.white;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.unityFontDefinition = StyleKeyword.None;
            btn.style.unityFont = new StyleFont(_font);
            // Initial colour follows the active state -- set after registration
            // so UpdateFilterButtonColors picks the right one.
            _filterButtons.Add((btn, layerId));
            btn.clicked += () =>
            {
                _queryAllLayers = layerId == null;
                _selectedLayerFilter = layerId;
                UpdateFilterButtonColors();
            };
            UpdateFilterButtonColors();
            return btn;
        }

        /// <summary>
        /// Re-paint every filter button so the currently selected one is
        /// highlighted. The button background was set once at creation time
        /// before this fix, so clicks updated state but never the visuals.
        /// </summary>
        private void UpdateFilterButtonColors()
        {
            foreach (var (btn, layerId) in _filterButtons)
            {
                bool isSelected = layerId == null
                    ? _queryAllLayers
                    : (!_queryAllLayers && layerId == _selectedLayerFilter);
                btn.style.backgroundColor = isSelected ? FilterActiveBg : FilterInactiveBg;
            }
        }

        private Button MakeActionButton(string text, Color bgColor, System.Action onClick)
        {
            var btn = new Button();
            btn.text = text;
            btn.style.height = 22;
            btn.style.marginTop = 2;
            btn.style.marginBottom = 2;
            btn.style.marginRight = 3;
            btn.style.paddingLeft = 8;
            btn.style.paddingRight = 8;
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
            btn.style.unityFontDefinition = StyleKeyword.None;
            btn.style.unityFont = new StyleFont(_font);
            btn.clicked += onClick;
            return btn;
        }

        private void OnDestroy()
        {
            if (_map != null)
                _map.Off(MapEventType.Click, OnMapClick);
        }
    }
}
