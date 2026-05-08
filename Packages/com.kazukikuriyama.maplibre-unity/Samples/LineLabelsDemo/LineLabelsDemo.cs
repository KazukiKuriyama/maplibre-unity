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
    /// Demonstrates symbol-placement: "line" -- text labels that follow line geometry.
    /// Features:
    ///   - "line" vs "point" placement toggle to compare behavior
    ///   - symbol-spacing slider for interactive label density control
    ///   - Sharp-curve road data to show text-max-angle label rejection
    ///   - text-size slider for zoom-independent size adjustment
    /// Attach this MonoBehaviour to the same GameObject as MapLibreMap.
    /// </summary>
    public class LineLabelsDemo : MonoBehaviour
    {
        [Header("Timing")]
        [SerializeField] private float _initialDelay = 0.3f;

        [SerializeField] private PanelSettings _panelSettings;

        private MapLibreMap _map;
        private Label _logLabel;
        private Label _spacingValueLabel;
        private Label _textSizeValueLabel;
        private Font _font;
        private readonly List<string> _logLines = new();
        private const int MaxLogLines = 3;

        private bool _isLinePlacement = true;
        private int _symbolSpacing = 80;
        private int _textSize = 14;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[LineLabelsDemo] MapLibreMap component not found on this GameObject");
                yield break;
            }

            while (!_map.IsInitialized)
                yield return null;

            yield return new WaitForSeconds(_initialDelay);

            SetupDemoData();
            _font = SystemFontFallback.Resolve("Arial", 16);
            BuildUI();
        }

        private void SetupDemoData()
        {
            // Add GeoJSON source with road-like line features around Tokyo
            _map.AddSource("tokyo-roads", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(RoadsGeoJson)
            });

            // Line layer for road rendering
            _map.AddLayer(new LayerDefinition
            {
                Id = "road-lines",
                Type = LayerType.Line,
                Source = "tokyo-roads",
                SourceLayer = "tokyo-roads",
                Paint = new Dictionary<string, object>
                {
                    { "line-color", "#7799bb" },
                    { "line-width", 4 },
                    { "line-opacity", 0.6 }
                }
            }, triggerRefresh: false);

            // Symbol layer with symbol-placement: "line"
            _map.AddLayer(new LayerDefinition
            {
                Id = "road-labels",
                Type = LayerType.Symbol,
                Source = "tokyo-roads",
                SourceLayer = "tokyo-roads",
                Layout = new Dictionary<string, object>
                {
                    { "text-field", "{name}" },
                    { "text-size", _textSize },
                    { "symbol-placement", "line" },
                    { "symbol-spacing", _symbolSpacing }
                },
                Paint = new Dictionary<string, object>
                {
                    { "text-color", "#222222" },
                    { "text-halo-color", "rgba(255, 255, 255, 0.95)" },
                    { "text-halo-width", 2 }
                }
            }, triggerRefresh: false);

            _map.RefreshTiles();
            Debug.Log("[LineLabelsDemo] Road lines and line-placed labels added");
        }

        // ────────────────────────────────────────────
        // UI
        // ────────────────────────────────────────────

        private void BuildUI()
        {
            var go = new GameObject("LineLabelsUI");
            go.transform.SetParent(transform);

            var uiDoc = go.AddComponent<UIDocument>();
            if (_panelSettings != null)
            {
                uiDoc.panelSettings = _panelSettings;
            }
            else
            {
                // Fallback: find any PanelSettings in the project (including MapLibreMap's)
                var found = Resources.FindObjectsOfTypeAll<PanelSettings>();
                if (found.Length > 0)
                    uiDoc.panelSettings = found[0];
            }
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

            // Panel container -- bottom-left, viewport-relative width.
            var panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = new Length(50, LengthUnit.Percent),
                    translate = new Translate(0, new Length(-50, LengthUnit.Percent)),
                    left = 14,
                    width = new Length(28, LengthUnit.Percent),
                    maxWidth = 360,
                    minWidth = 240,
                    backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.88f),
                    borderTopLeftRadius = 8,
                    borderTopRightRadius = 8,
                    borderBottomLeftRadius = 8,
                    borderBottomRightRadius = 8,
                    paddingTop = 14,
                    paddingBottom = 14,
                    paddingLeft = 16,
                    paddingRight = 16,
                }
            };
            root.Add(panel);

            // Title
            panel.Add(MakeLabel("symbol-placement: \"line\"", 18, Color.white, FontStyle.Bold));

            // ── placement toggle ──
            var placementRow = MakeRow();
            placementRow.style.marginTop = 10;
            placementRow.Add(MakeButton("line", new Color(0.15f, 0.35f, 0.55f), OnSetLinePlacement));
            placementRow.Add(MakeButton("point", new Color(0.30f, 0.30f, 0.30f), OnSetPointPlacement));
            panel.Add(placementRow);

            // ── symbol-spacing ──
            _spacingValueLabel = MakeLabel($"spacing: {_symbolSpacing}px", 15, new Color(0.6f, 0.85f, 1f));

            var spacingRow = MakeRow();
            spacingRow.style.marginTop = 8;
            spacingRow.Add(MakeButton("-", new Color(0.35f, 0.25f, 0.15f), OnDecreaseSpacing));
            spacingRow.Add(_spacingValueLabel);
            spacingRow.Add(MakeButton("+", new Color(0.35f, 0.25f, 0.15f), OnIncreaseSpacing));
            panel.Add(spacingRow);

            // ── text-size ──
            _textSizeValueLabel = MakeLabel($"size: {_textSize}px", 15, new Color(0.6f, 0.85f, 1f));

            var textSizeRow = MakeRow();
            textSizeRow.style.marginTop = 8;
            textSizeRow.Add(MakeButton("-", new Color(0.25f, 0.35f, 0.20f), OnDecreaseTextSize));
            textSizeRow.Add(_textSizeValueLabel);
            textSizeRow.Add(MakeButton("+", new Color(0.25f, 0.35f, 0.20f), OnIncreaseTextSize));
            panel.Add(textSizeRow);

            // Log area
            _logLabel = new Label();
            _logLabel.style.fontSize = 13;
            _logLabel.style.color = new Color(0.7f, 1f, 0.7f, 0.8f);
            _logLabel.style.marginTop = 10;
            _logLabel.style.whiteSpace = WhiteSpace.Normal;
            _logLabel.style.unityFontDefinition = StyleKeyword.None;
            _logLabel.style.unityFont = new StyleFont(_font);
            _logLabel.pickingMode = PickingMode.Ignore;
            panel.Add(_logLabel);

            Log("Ready -- symbol-placement: \"line\"");
        }

        // ────────────────────────────────────────────
        // Event handlers
        // ────────────────────────────────────────────

        private void OnSetLinePlacement()
        {
            if (_isLinePlacement) return;
            _isLinePlacement = true;
            _map.SetLayoutProperty("road-labels", "symbol-placement", "line");
            Log("setLayoutProperty('road-labels', 'symbol-placement', 'line')");
        }

        private void OnSetPointPlacement()
        {
            if (!_isLinePlacement) return;
            _isLinePlacement = false;
            _map.SetLayoutProperty("road-labels", "symbol-placement", "point");
            Log("setLayoutProperty('road-labels', 'symbol-placement', 'point')");
        }

        private void OnDecreaseSpacing()
        {
            _symbolSpacing = Mathf.Max(20, _symbolSpacing - 20);
            _spacingValueLabel.text = $"spacing: {_symbolSpacing}px";
            _map.SetLayoutProperty("road-labels", "symbol-spacing", _symbolSpacing);
            Log($"setLayoutProperty('road-labels', 'symbol-spacing', {_symbolSpacing})");
        }

        private void OnIncreaseSpacing()
        {
            _symbolSpacing = Mathf.Min(400, _symbolSpacing + 20);
            _spacingValueLabel.text = $"spacing: {_symbolSpacing}px";
            _map.SetLayoutProperty("road-labels", "symbol-spacing", _symbolSpacing);
            Log($"setLayoutProperty('road-labels', 'symbol-spacing', {_symbolSpacing})");
        }

        private void OnDecreaseTextSize()
        {
            _textSize = Mathf.Max(8, _textSize - 2);
            _textSizeValueLabel.text = $"size: {_textSize}px";
            _map.SetLayoutProperty("road-labels", "text-size", _textSize);
            Log($"setLayoutProperty('road-labels', 'text-size', {_textSize})");
        }

        private void OnIncreaseTextSize()
        {
            _textSize = Mathf.Min(28, _textSize + 2);
            _textSizeValueLabel.text = $"size: {_textSize}px";
            _map.SetLayoutProperty("road-labels", "text-size", _textSize);
            Log($"setLayoutProperty('road-labels', 'text-size', {_textSize})");
        }

        // ────────────────────────────────────────────
        // UI helpers
        // ────────────────────────────────────────────

        private Label MakeLabel(string text, int size, Color color,
            FontStyle fontStyle = FontStyle.Normal)
        {
            var lbl = new Label(text);
            lbl.style.fontSize = size;
            lbl.style.color = color;
            lbl.style.unityFontStyleAndWeight = fontStyle;
            lbl.style.unityFontDefinition = StyleKeyword.None;
            lbl.style.unityFont = new StyleFont(_font);
            lbl.pickingMode = PickingMode.Ignore;
            return lbl;
        }

        private Label MakeSectionLabel(string text)
        {
            var lbl = new Label(text);
            lbl.style.fontSize = 13;
            lbl.style.color = new Color(0.55f, 0.75f, 1f);
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.marginTop = 8;
            lbl.style.marginBottom = 2;
            lbl.style.unityFontDefinition = StyleKeyword.None;
            lbl.style.unityFont = new StyleFont(_font);
            lbl.pickingMode = PickingMode.Ignore;
            return lbl;
        }

        private Label MakeDescription(string text)
        {
            var lbl = new Label(text);
            lbl.style.fontSize = 11;
            lbl.style.color = new Color(0.6f, 0.6f, 0.6f);
            lbl.style.whiteSpace = WhiteSpace.Normal;
            lbl.style.marginTop = 2;
            lbl.style.marginBottom = 4;
            lbl.style.unityFontDefinition = StyleKeyword.None;
            lbl.style.unityFont = new StyleFont(_font);
            lbl.pickingMode = PickingMode.Ignore;
            return lbl;
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        private Button MakeButton(string text, Color bgColor, System.Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            btn.style.fontSize = 15;
            btn.style.color = Color.white;
            btn.style.backgroundColor = bgColor;
            btn.style.borderTopLeftRadius = 6;
            btn.style.borderTopRightRadius = 6;
            btn.style.borderBottomLeftRadius = 6;
            btn.style.borderBottomRightRadius = 6;
            btn.style.marginRight = 6;
            btn.style.paddingTop = 6;
            btn.style.paddingBottom = 6;
            btn.style.paddingLeft = 14;
            btn.style.paddingRight = 14;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.unityFontDefinition = StyleKeyword.None;
            btn.style.unityFont = new StyleFont(_font);
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            return btn;
        }

        private void Log(string message)
        {
            _logLines.Add(message);
            while (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);
            if (_logLabel != null)
                _logLabel.text = string.Join("\n", _logLines);
            Debug.Log($"[LineLabelsDemo] {message}");
        }

        // ────────────────────────────────────────────
        // GeoJSON data
        // ────────────────────────────────────────────

        // Curved road lines around central Tokyo.
        // Includes a "Hairpin Curve" road with a sharp turn to demonstrate text-max-angle rejection.
        private const string RoadsGeoJson = @"{
            ""type"": ""FeatureCollection"",
            ""features"": [
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7454, 35.6939],
                            [139.7530, 35.6920],
                            [139.7600, 35.6890],
                            [139.7671, 35.6812],
                            [139.7720, 35.6770],
                            [139.7790, 35.6730]
                        ]
                    },
                    ""properties"": { ""name"": ""Chuo-dori"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7003, 35.6896],
                            [139.7100, 35.6930],
                            [139.7200, 35.6940],
                            [139.7310, 35.6920],
                            [139.7400, 35.6870],
                            [139.7500, 35.6850],
                            [139.7600, 35.6830],
                            [139.7671, 35.6812]
                        ]
                    },
                    ""properties"": { ""name"": ""Yasukuni-dori"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7003, 35.6896],
                            [139.7050, 35.6830],
                            [139.7016, 35.6740],
                            [139.7016, 35.6580],
                            [139.7080, 35.6510],
                            [139.7200, 35.6470]
                        ]
                    },
                    ""properties"": { ""name"": ""Meiji-dori"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7016, 35.6580],
                            [139.7120, 35.6600],
                            [139.7250, 35.6620],
                            [139.7370, 35.6650],
                            [139.7500, 35.6690],
                            [139.7600, 35.6720],
                            [139.7671, 35.6812]
                        ]
                    },
                    ""properties"": { ""name"": ""Roppongi-dori"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7101, 35.7296],
                            [139.7150, 35.7200],
                            [139.7200, 35.7100],
                            [139.7250, 35.7000],
                            [139.7300, 35.6920],
                            [139.7400, 35.6870]
                        ]
                    },
                    ""properties"": { ""name"": ""Kasuga-dori"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7771, 35.7141],
                            [139.7700, 35.7050],
                            [139.7650, 35.6950],
                            [139.7620, 35.6870],
                            [139.7650, 35.6812],
                            [139.7671, 35.6750]
                        ]
                    },
                    ""properties"": { ""name"": ""Showa-dori"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7250, 35.6620],
                            [139.7280, 35.6700],
                            [139.7320, 35.6800],
                            [139.7350, 35.6870],
                            [139.7400, 35.6940],
                            [139.7454, 35.6990],
                            [139.7500, 35.7060],
                            [139.7550, 35.7141]
                        ]
                    },
                    ""properties"": { ""name"": ""Sotobori-dori"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7584, 35.6753],
                            [139.7530, 35.6700],
                            [139.7460, 35.6650],
                            [139.7380, 35.6610],
                            [139.7290, 35.6580],
                            [139.7200, 35.6560],
                            [139.7100, 35.6550]
                        ]
                    },
                    ""properties"": { ""name"": ""Uchibori-dori"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7350, 35.6950],
                            [139.7380, 35.6900],
                            [139.7420, 35.6870],
                            [139.7480, 35.6900],
                            [139.7450, 35.6940],
                            [139.7380, 35.6960],
                            [139.7350, 35.6950]
                        ]
                    },
                    ""properties"": { ""name"": ""Hairpin Curve"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.6700, 35.6900],
                            [139.6800, 35.6880],
                            [139.6920, 35.6860],
                            [139.7003, 35.6850],
                            [139.7100, 35.6840],
                            [139.7200, 35.6830],
                            [139.7300, 35.6810],
                            [139.7400, 35.6800],
                            [139.7500, 35.6790],
                            [139.7600, 35.6780],
                            [139.7671, 35.6770],
                            [139.7750, 35.6760],
                            [139.7850, 35.6740],
                            [139.7950, 35.6720],
                            [139.8050, 35.6700]
                        ]
                    },
                    ""properties"": { ""name"": ""Koshu-kaido"" }
                },
                {
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7200, 35.7300],
                            [139.7250, 35.7200],
                            [139.7300, 35.7100],
                            [139.7350, 35.7000],
                            [139.7380, 35.6900],
                            [139.7400, 35.6800],
                            [139.7420, 35.6700],
                            [139.7450, 35.6600],
                            [139.7480, 35.6500],
                            [139.7500, 35.6400],
                            [139.7520, 35.6300],
                            [139.7550, 35.6200]
                        ]
                    },
                    ""properties"": { ""name"": ""Meguro-dori"" }
                }
            ]
        }";
    }
}
