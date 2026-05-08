using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.CameraControl;
using MapLibre.Unity.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Side-by-side comparison of the four camera-animation entry points:
    /// <c>EaseTo</c>, <c>FlyTo</c>, <c>ZoomTo</c>, <c>RotateTo</c>, <c>PanBy</c>,
    /// and <c>JumpTo</c>. Each button drives the same camera target
    /// through a different MapAnimator transition so the visual difference
    /// (linear vs. parabolic, animated vs. instant) is obvious.
    /// </summary>
    public class CameraAnimationDemo : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;

        private MapLibreMap _map;
        private Label _logLabel;
        private Font _font;
        private readonly List<string> _logLines = new();
        private const int MaxLogLines = 6;

        // Three reference targets so the buttons can chain calls and the
        // motion difference between transitions is easier to see.
        private static readonly LngLat Tokyo  = new(139.7670, 35.6814);
        private static readonly LngLat Kyoto  = new(135.7681, 35.0116);
        private static readonly LngLat Sapporo = new(141.3469, 43.0642);

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[CameraAnimationDemo] MapLibreMap not found");
                yield break;
            }
            while (!_map.IsInitialized)
                yield return null;

            _font = SystemFontFallback.Resolve("Arial", 16);
            BuildUi();
        }

        private void BuildUi()
        {
            var go = new GameObject("CameraAnimationDemoUI");
            go.transform.SetParent(transform);
            var uiDoc = go.AddComponent<UIDocument>();
            if (_panelSettings != null) uiDoc.panelSettings = _panelSettings;
            uiDoc.sortingOrder = 200;
            StartCoroutine(BuildAfterFrame(uiDoc));
        }

        private IEnumerator BuildAfterFrame(UIDocument uiDoc)
        {
            yield return null;

            var root = uiDoc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0;
            root.style.right = 0; root.style.bottom = 0;

            var panel = MakePanel();
            root.Add(panel);

            panel.Add(MakeLabel("Camera animation", 14, Color.white, FontStyle.Bold));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;
            SampleScrollViewStyle.Apply(scroll);
            panel.Add(scroll);

            scroll.Add(MakeSection("EaseTo (linear)"));
            scroll.Add(MakeButton("EaseTo Tokyo (1000ms)", new Color(0.13f, 0.35f, 0.55f),
                () => EaseTo(Tokyo, 12f, 1000f)));
            scroll.Add(MakeButton("EaseTo Kyoto (2000ms, bearing 30)", new Color(0.13f, 0.35f, 0.55f),
                () => EaseTo(Kyoto, 11f, 2000f, bearing: 30f)));

            scroll.Add(MakeSection("FlyTo (parabolic)"));
            scroll.Add(MakeButton("FlyTo Tokyo → Sapporo", new Color(0.20f, 0.45f, 0.30f),
                () => FlyTo(Sapporo, 10f, 3000f)));
            scroll.Add(MakeButton("FlyTo Sapporo → Kyoto", new Color(0.20f, 0.45f, 0.30f),
                () => FlyTo(Kyoto, 11f, 3000f)));

            scroll.Add(MakeSection("ZoomTo / RotateTo"));
            scroll.Add(MakeButton("ZoomTo 14 (1000ms)", new Color(0.55f, 0.35f, 0.13f),
                () => { _map.ZoomTo(14f, 1000f); Log("ZoomTo(14, 1000ms)"); }));
            scroll.Add(MakeButton("ZoomTo 4 (1500ms)", new Color(0.55f, 0.35f, 0.13f),
                () => { _map.ZoomTo(4f, 1500f); Log("ZoomTo(4, 1500ms)"); }));
            scroll.Add(MakeButton("RotateTo 0", new Color(0.55f, 0.35f, 0.13f),
                () => { _map.RotateTo(0f, 1000f); Log("RotateTo(0)"); }));
            scroll.Add(MakeButton("RotateTo 90", new Color(0.55f, 0.35f, 0.13f),
                () => { _map.RotateTo(90f, 1000f); Log("RotateTo(90)"); }));

            scroll.Add(MakeSection("PanBy / JumpTo"));
            scroll.Add(MakeButton("PanBy (+200, 0) px", new Color(0.45f, 0.30f, 0.55f),
                () => { _map.PanBy(new Vector2(200, 0), 600f); Log("PanBy((200,0), 600ms)"); }));
            scroll.Add(MakeButton("PanBy (0, -200) px", new Color(0.45f, 0.30f, 0.55f),
                () => { _map.PanBy(new Vector2(0, -200), 600f); Log("PanBy((0,-200), 600ms)"); }));
            scroll.Add(MakeButton("JumpTo Tokyo (instant)", new Color(0.65f, 0.20f, 0.20f),
                () =>
                {
                    _map.JumpTo(new JumpToOptions { Center = Tokyo, Zoom = 12f, Bearing = 0f, Pitch = 0f });
                    Log("JumpTo(Tokyo, z=12) -- no animation");
                }));

            _logLabel = MakeLogLabel();
            panel.Add(_logLabel);
            Log("Ready. Click a button to drive the camera.");
        }

        private void EaseTo(LngLat center, float zoom, float durationMs, float bearing = 0f, float pitch = 0f)
        {
            _map.EaseTo(new EaseToOptions
            {
                Center = center,
                Zoom = zoom,
                Bearing = bearing,
                Pitch = pitch,
                Duration = durationMs,
            });
            Log($"EaseTo({center.Longitude:F2},{center.Latitude:F2}, z={zoom}, b={bearing}, {durationMs}ms)");
        }

        private void FlyTo(LngLat center, float zoom, float durationMs)
        {
            _map.FlyTo(new FlyToOptions
            {
                Center = center,
                Zoom = zoom,
                Duration = durationMs,
            });
            Log($"FlyTo({center.Longitude:F2},{center.Latitude:F2}, z={zoom}, {durationMs}ms)");
        }

        // ── UI helpers (same conventions as other demos) ──

        private VisualElement MakePanel()
        {
            return new VisualElement
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
                    paddingTop = 14, paddingBottom = 14,
                    paddingLeft = 14, paddingRight = 14,
                    borderTopWidth = 1, borderBottomWidth = 1,
                    borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.12f),
                    borderBottomColor = new Color(1, 1, 1, 0.12f),
                    borderLeftColor = new Color(1, 1, 1, 0.12f),
                    borderRightColor = new Color(1, 1, 1, 0.12f),
                }
            };
        }

        private Label MakeLabel(string text, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color;
            l.style.unityFontStyleAndWeight = style;
            l.style.marginBottom = 6;
            l.style.unityFontDefinition = StyleKeyword.None;
            l.style.unityFont = new StyleFont(_font);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private Label MakeSection(string text) => new(text)
        {
            style =
            {
                fontSize = 11,
                color = new Color(1, 1, 1, 0.5f),
                unityFontStyleAndWeight = FontStyle.Italic,
                marginTop = 5, marginBottom = 3,
                unityFontDefinition = StyleKeyword.None,
                unityFont = new StyleFont(_font),
            },
            pickingMode = PickingMode.Ignore,
        };

        private Button MakeButton(string text, Color bg, System.Action onClick)
        {
            var b = new Button { text = text };
            b.style.height = 24;
            b.style.marginTop = 1; b.style.marginBottom = 1;
            b.style.borderTopLeftRadius = 4; b.style.borderTopRightRadius = 4;
            b.style.borderBottomLeftRadius = 4; b.style.borderBottomRightRadius = 4;
            b.style.fontSize = 11;
            b.style.color = Color.white;
            b.style.backgroundColor = bg;
            b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0;
            b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.unityFontDefinition = StyleKeyword.None;
            b.style.unityFont = new StyleFont(_font);
            b.clicked += onClick;
            return b;
        }

        private Label MakeLogLabel()
        {
            var l = new Label();
            l.style.fontSize = 10;
            l.style.color = new Color(0.7f, 1f, 0.7f, 0.9f);
            l.style.backgroundColor = new Color(0, 0, 0, 0.4f);
            l.style.borderTopLeftRadius = 4; l.style.borderTopRightRadius = 4;
            l.style.borderBottomLeftRadius = 4; l.style.borderBottomRightRadius = 4;
            l.style.paddingTop = 6; l.style.paddingBottom = 6;
            l.style.paddingLeft = 6; l.style.paddingRight = 6;
            l.style.marginTop = 6; l.style.minHeight = 40;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.unityFontDefinition = StyleKeyword.None;
            l.style.unityFont = new StyleFont(_font);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private void Log(string msg)
        {
            Debug.Log($"[CameraAnimationDemo] {msg}");
            _logLines.Add(msg);
            while (_logLines.Count > MaxLogLines) _logLines.RemoveAt(0);
            if (_logLabel != null) _logLabel.text = string.Join("\n", _logLines);
        }
    }
}
