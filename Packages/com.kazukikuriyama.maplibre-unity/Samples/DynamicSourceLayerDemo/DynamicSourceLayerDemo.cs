using System;
using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using MapLibre.Unity.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Interactive demo for the dynamic source/layer API.
    /// Uses UI Toolkit buttons (built in C#) to call each API step-by-step.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class DynamicSourceLayerDemo : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;

        private MapLibreMap _map;
        private Label _logLabel;
        private Font _font;
        private readonly List<string> _logLines = new();
        private const int MaxLogLines = 6;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[DynamicSourceLayerDemo] MapLibreMap not found");
                yield break;
            }

            while (!_map.IsInitialized)
                yield return null;

            // Wait an extra frame so MapLibreMap finishes its own UI setup
            yield return null;

            _font = SystemFontFallback.Resolve("Arial", 16);
            BuildUI();
        }

        private void BuildUI()
        {
            var go = new GameObject("DemoUI");
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

            // Panel container. Viewport-relative width clamped between
            // 240 and the original 360px target.
            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.top = new Length(50, LengthUnit.Percent);
            panel.style.translate = new Translate(0, new Length(-50, LengthUnit.Percent));
            panel.style.left = 14;
            panel.style.width = new Length(28, LengthUnit.Percent);
            panel.style.maxWidth = 360;
            panel.style.minWidth = 240;
            panel.style.maxHeight = new Length(80, LengthUnit.Percent);
            panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.88f);
            panel.style.borderTopLeftRadius = 10;
            panel.style.borderTopRightRadius = 10;
            panel.style.borderBottomLeftRadius = 10;
            panel.style.borderBottomRightRadius = 10;
            panel.style.paddingTop = 16;
            panel.style.paddingBottom = 16;
            panel.style.paddingLeft = 16;
            panel.style.paddingRight = 16;
            panel.style.borderTopWidth = 1;
            panel.style.borderBottomWidth = 1;
            panel.style.borderLeftWidth = 1;
            panel.style.borderRightWidth = 1;
            panel.style.borderTopColor = new Color(1, 1, 1, 0.12f);
            panel.style.borderBottomColor = new Color(1, 1, 1, 0.12f);
            panel.style.borderLeftColor = new Color(1, 1, 1, 0.12f);
            panel.style.borderRightColor = new Color(1, 1, 1, 0.12f);
            root.Add(panel);

            // Title
            panel.Add(MakeLabel("Dynamic Source / Layer API", 14, Color.white, FontStyle.Bold));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;
            SampleScrollViewStyle.Apply(scroll);
            panel.Add(scroll);

            // Source section
            scroll.Add(MakeSectionLabel("Source"));
            scroll.Add(MakeButton("addSource(\"parks\")", new Color(0.22f, 0.28f, 0.52f), OnAddParksSource));
            scroll.Add(MakeButton("addSource(\"stations\")", new Color(0.22f, 0.28f, 0.52f), OnAddStationsSource));
            scroll.Add(MakeButton("getSource(\"parks\")", new Color(0.20f, 0.39f, 0.31f), OnGetParksSource));
            scroll.Add(MakeButton("getSource(\"stations\")", new Color(0.20f, 0.39f, 0.31f), OnGetStationsSource));
            scroll.Add(MakeButton("removeSource(\"parks\")", new Color(0.55f, 0.16f, 0.16f), OnRemoveParksSource));
            scroll.Add(MakeButton("removeSource(\"stations\")", new Color(0.55f, 0.16f, 0.16f), OnRemoveStationsSource));

            // Layer section
            scroll.Add(MakeSectionLabel("Layer"));
            scroll.Add(MakeButton("addLayer (parks-fill + outline)", new Color(0.22f, 0.28f, 0.52f), OnAddParksLayers));
            scroll.Add(MakeButton("addLayer (stations-circle + label)", new Color(0.22f, 0.28f, 0.52f), OnAddStationLayers));
            scroll.Add(MakeButton("getLayer(\"parks-fill\")", new Color(0.20f, 0.39f, 0.31f), OnGetParksLayer));
            scroll.Add(MakeButton("getLayer(\"stations-circle\")", new Color(0.20f, 0.39f, 0.31f), OnGetStationsLayer));
            scroll.Add(MakeButton("removeLayer(\"parks-outline\")", new Color(0.55f, 0.16f, 0.16f), OnRemoveParksOutline));
            scroll.Add(MakeButton("removeLayer(\"parks-fill\")", new Color(0.55f, 0.16f, 0.16f), OnRemoveParksFill));
            scroll.Add(MakeButton("removeLayer (stations)", new Color(0.55f, 0.16f, 0.16f), OnRemoveStationLayers));

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

            Log("Ready. Press buttons to call API.");
        }

        private Label MakeLabel(string text, int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            var label = new Label(text);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = fontStyle;
            label.style.marginBottom = 4;
            label.style.unityFontDefinition = StyleKeyword.None;
            label.style.unityFont = new StyleFont(_font);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private Label MakeSectionLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 11;
            label.style.color = new Color(1, 1, 1, 0.5f);
            label.style.unityFontStyleAndWeight = FontStyle.Italic;
            label.style.marginTop = 5;
            label.style.marginBottom = 3;
            label.style.unityFontDefinition = StyleKeyword.None;
            label.style.unityFont = new StyleFont(_font);
            label.pickingMode = PickingMode.Ignore;
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

        // --- Source operations ---

        private void OnAddParksSource()
        {
            if (_map.GetSource("parks") != null)
            {
                Log("addSource: 'parks' already exists");
                return;
            }

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
            Log("addSource('parks') OK");
        }

        private void OnAddStationsSource()
        {
            if (_map.GetSource("stations") != null)
            {
                Log("addSource: 'stations' already exists");
                return;
            }

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
            Log("addSource('stations') OK");
        }

        private void OnGetParksSource() => GetSourceAndLog("parks");
        private void OnGetStationsSource() => GetSourceAndLog("stations");

        private void GetSourceAndLog(string id)
        {
            var src = _map.GetSource(id);
            if (src != null)
                Log($"getSource('{id}'): type={src.Definition.Type}");
            else
                Log($"getSource('{id}'): null");
        }

        private void OnRemoveParksSource() => RemoveSourceAndLog("parks");
        private void OnRemoveStationsSource() => RemoveSourceAndLog("stations");

        private void RemoveSourceAndLog(string id)
        {
            if (_map.GetSource(id) == null)
            {
                Log($"removeSource: '{id}' not found");
                return;
            }

            _map.RemoveSource(id);
            if (_map.GetSource(id) != null)
                Log($"removeSource('{id}'): blocked -- remove layers first");
            else
                Log($"removeSource('{id}') OK");
        }

        // --- Layer operations ---

        private void OnAddParksLayers()
        {
            if (_map.GetSource("parks") == null)
            {
                Log("addLayer: source 'parks' not found -- add it first");
                return;
            }

            bool added = false;

            if (_map.GetLayer("parks-fill") == null)
            {
                _map.AddLayer(new LayerDefinition
                {
                    Id = "parks-fill",
                    Type = LayerType.Fill,
                    Source = "parks",
                    SourceLayer = "parks",
                    Paint = new Dictionary<string, object>
                    {
                        { "fill-color", "#4CAF50" },
                        { "fill-opacity", 0.4 }
                    }
                }, triggerRefresh: false);
                added = true;
            }

            if (_map.GetLayer("parks-outline") == null)
            {
                _map.AddLayer(new LayerDefinition
                {
                    Id = "parks-outline",
                    Type = LayerType.Line,
                    Source = "parks",
                    SourceLayer = "parks",
                    Paint = new Dictionary<string, object>
                    {
                        { "line-color", "#2E7D32" },
                        { "line-width", 3 },
                        { "line-opacity", 0.9 }
                    }
                }, triggerRefresh: false);
                added = true;
            }

            if (added)
            {
                _map.RefreshTiles();
                Log("addLayer('parks-fill','parks-outline') OK");
            }
            else
            {
                Log("addLayer: parks layers already exist");
            }
        }

        private void OnAddStationLayers()
        {
            if (_map.GetSource("stations") == null)
            {
                Log("addLayer: source 'stations' not found -- add it first");
                return;
            }

            bool added = false;

            if (_map.GetLayer("stations-circle") == null)
            {
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
                added = true;
            }

            if (_map.GetLayer("stations-label") == null)
            {
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
                added = true;
            }

            if (added)
            {
                _map.RefreshTiles();
                Log("addLayer('stations-circle','stations-label') OK");
            }
            else
            {
                Log("addLayer: stations layers already exist");
            }
        }

        private void OnGetParksLayer() => GetLayerAndLog("parks-fill");
        private void OnGetStationsLayer() => GetLayerAndLog("stations-circle");

        private void GetLayerAndLog(string id)
        {
            var layer = _map.GetLayer(id);
            if (layer != null)
                Log($"getLayer('{id}'): type={layer.Type}, src={layer.Source}");
            else
                Log($"getLayer('{id}'): null");
        }

        private void OnRemoveParksOutline()
        {
            if (_map.GetLayer("parks-outline") == null)
            {
                Log("removeLayer: 'parks-outline' not found");
                return;
            }
            _map.RemoveLayer("parks-outline");
            Log("removeLayer('parks-outline') OK");
        }

        private void OnRemoveParksFill()
        {
            if (_map.GetLayer("parks-fill") == null)
            {
                Log("removeLayer: 'parks-fill' not found");
                return;
            }
            _map.RemoveLayer("parks-fill");
            Log("removeLayer('parks-fill') OK");
        }

        private void OnRemoveStationLayers()
        {
            bool removed = false;
            if (_map.GetLayer("stations-label") != null)
            {
                _map.RemoveLayer("stations-label");
                removed = true;
            }
            if (_map.GetLayer("stations-circle") != null)
            {
                _map.RemoveLayer("stations-circle");
                removed = true;
            }

            if (removed)
                Log("removeLayer (stations) OK");
            else
                Log("removeLayer: no station layers to remove");
        }

        private void Log(string message)
        {
            Debug.Log($"[DynamicSourceLayerDemo] {message}");
            _logLines.Add(message);
            while (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);

            if (_logLabel != null)
                _logLabel.text = string.Join("\n", _logLines);
        }
    }
}
