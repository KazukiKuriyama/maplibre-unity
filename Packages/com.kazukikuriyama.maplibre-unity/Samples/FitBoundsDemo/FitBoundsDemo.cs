using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.CameraControl;
using MapLibre.Unity.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates the fitBounds() API matching MapLibre GL JS map.fitBounds().
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class FitBoundsDemo : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;

        private MapLibreMap _map;
        private Label _logLabel;
        private Font _font;
        private readonly List<string> _logLines = new();
        private const int MaxLogLines = 6;

        // Predefined bounds for demo
        private static readonly LngLatBounds JapanBounds = new(
            new LngLat(129.5, 31.0),
            new LngLat(145.8, 45.5));

        private static readonly LngLatBounds TokyoBounds = new(
            new LngLat(139.60, 35.60),
            new LngLat(139.85, 35.78));

        private static readonly LngLatBounds KansaiBounds = new(
            new LngLat(135.10, 34.55),
            new LngLat(135.85, 35.10));

        private static readonly LngLatBounds HokkaidoBounds = new(
            new LngLat(139.3, 41.3),
            new LngLat(145.8, 45.5));

        private static readonly LngLatBounds OkinawaBounds = new(
            new LngLat(127.5, 26.0),
            new LngLat(128.3, 26.9));

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[FitBoundsDemo] MapLibreMap not found");
                yield break;
            }

            while (!_map.IsInitialized)
                yield return null;

            _font = SystemFontFallback.Resolve("Arial", 16);
            BuildUI();
        }

        private void BuildUI()
        {
            var go = new GameObject("FitBoundsDemoUI");
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

            var panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = new Length(50, LengthUnit.Percent),
                    translate = new Translate(0, new Length(-50, LengthUnit.Percent)),
                    left = 14,
                    width = new Length(30, LengthUnit.Percent),
                    maxWidth = 380,
                    minWidth = 240,
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

            panel.Add(MakeLabel("fitBounds()", 14, Color.white, FontStyle.Bold));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;
            SampleScrollViewStyle.Apply(scroll);
            panel.Add(scroll);

            // --- fitBounds region buttons ---
            scroll.Add(MakeSectionLabel("Regions"));
            scroll.Add(MakeButton("Japan (all)", new Color(0.13f, 0.35f, 0.55f), OnFitJapan));
            scroll.Add(MakeButton("Tokyo", new Color(0.13f, 0.35f, 0.55f), OnFitTokyo));
            scroll.Add(MakeButton("Kansai (Osaka/Kyoto)", new Color(0.13f, 0.35f, 0.55f), OnFitKansai));
            scroll.Add(MakeButton("Hokkaido", new Color(0.13f, 0.35f, 0.55f), OnFitHokkaido));
            scroll.Add(MakeButton("Okinawa", new Color(0.13f, 0.35f, 0.55f), OnFitOkinawa));

            // --- fitBounds with options ---
            scroll.Add(MakeSectionLabel("Options"));
            scroll.Add(MakeButton("Tokyo + padding(100px)", new Color(0.35f, 0.22f, 0.45f), OnFitTokyoPadded));
            scroll.Add(MakeButton("Tokyo + maxZoom(10)", new Color(0.35f, 0.22f, 0.45f), OnFitTokyoMaxZoom));
            scroll.Add(MakeButton("Kansai + bearing(45)", new Color(0.35f, 0.22f, 0.45f), OnFitKansaiBearing));
            scroll.Add(MakeButton("Kansai + bearing(0)", new Color(0.35f, 0.22f, 0.45f), OnFitKansaiBearing0));
            scroll.Add(MakeButton("fitBounds(points)", new Color(0.35f, 0.22f, 0.45f), OnFitPoints));

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
            _logLabel.style.minHeight = 40;
            _logLabel.style.whiteSpace = WhiteSpace.Normal;
            _logLabel.style.unityFontDefinition = StyleKeyword.None;
            _logLabel.style.unityFont = new StyleFont(_font);
            _logLabel.pickingMode = PickingMode.Ignore;
            panel.Add(_logLabel);

            Log("Ready. Click a button to fitBounds().");
        }

        // --- Region handlers ---

        private void OnFitJapan()
        {
            _map.FitBounds(JapanBounds, new FitBoundsOptions { Duration = 2000f });
            Log("fitBounds(Japan) -- SW(129.5,31.0) NE(145.8,45.5)");
        }

        private void OnFitTokyo()
        {
            _map.FitBounds(TokyoBounds);
            Log("fitBounds(Tokyo) -- SW(139.60,35.60) NE(139.85,35.78)");
        }

        private void OnFitKansai()
        {
            _map.FitBounds(KansaiBounds);
            Log("fitBounds(Kansai) -- SW(135.10,34.55) NE(135.85,35.10)");
        }

        private void OnFitHokkaido()
        {
            _map.FitBounds(HokkaidoBounds, new FitBoundsOptions { Duration = 2000f });
            Log("fitBounds(Hokkaido) -- SW(139.3,41.3) NE(145.8,45.5)");
        }

        private void OnFitOkinawa()
        {
            _map.FitBounds(OkinawaBounds, new FitBoundsOptions { Duration = 2000f });
            Log("fitBounds(Okinawa) -- SW(127.5,26.0) NE(128.3,26.9)");
        }

        // --- Options handlers ---

        private void OnFitTokyoPadded()
        {
            _map.FitBounds(TokyoBounds, new FitBoundsOptions
            {
                Padding = new PaddingOptions(100f)
            });
            Log("fitBounds(Tokyo, { padding: 100 })");
        }

        private void OnFitTokyoMaxZoom()
        {
            _map.FitBounds(TokyoBounds, new FitBoundsOptions
            {
                MaxZoom = 10f
            });
            Log("fitBounds(Tokyo, { maxZoom: 10 })");
        }

        private void OnFitKansaiBearing()
        {
            _map.FitBounds(KansaiBounds, new FitBoundsOptions
            {
                Bearing = 45f,
                Duration = 1500f,
            });
            Log("fitBounds(Kansai, { bearing: 45 })");
        }

        private void OnFitKansaiBearing0()
        {
            _map.FitBounds(KansaiBounds, new FitBoundsOptions
            {
                Bearing = 0f,
                Duration = 1500f,
            });
            Log("fitBounds(Kansai, { bearing: 0 })");
        }

        private void OnFitPoints()
        {
            var points = new[]
            {
                new LngLat(139.7671, 35.6812), // Tokyo Station
                new LngLat(135.7681, 35.0116), // Kyoto Station
                new LngLat(130.4017, 33.5902), // Hakata Station
            };
            _map.FitBounds(points, new FitBoundsOptions
            {
                Padding = new PaddingOptions(50f),
                Duration = 2000f
            });
            Log("fitBounds([Tokyo, Kyoto, Hakata], { padding: 50 })");
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
            return new Label(text)
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
        }

        private Button MakeButton(string text, Color bgColor, System.Action onClick)
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
            Debug.Log($"[FitBoundsDemo] {message}");
            _logLines.Add(message);
            while (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);

            if (_logLabel != null)
                _logLabel.text = string.Join("\n", _logLines);
        }
    }
}
