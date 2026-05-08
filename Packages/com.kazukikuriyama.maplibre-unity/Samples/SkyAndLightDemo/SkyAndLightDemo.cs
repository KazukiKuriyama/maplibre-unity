using System.Collections;
using MapLibre.Unity.Style;
using MapLibre.Unity.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Drives <c>MapLibreMap.SetSky</c> and <c>SetLight</c> from a small UI:
    /// the time-of-day slider sweeps both the sky gradient (dawn → midday →
    /// dusk → night) and the directional light's azimuth / intensity, while
    /// the pitch slider tilts the camera so the sky band is visible. Useful
    /// for sanity-checking that style-driven sky and 3D-layer lighting both
    /// react to runtime updates.
    /// </summary>
    public class SkyAndLightDemo : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;

        private MapLibreMap _map;
        private Font _font;

        private float _timeOfDay = 0.5f; // 0=midnight, 0.25=dawn, 0.5=noon, 0.75=dusk
        private float _intensity = 0.6f;
        private float _horizonBlend = 0.8f;
        private bool _useFlatLight;
        private Label _summary;

        // Set up a default mid-pitch view so the sky band is visible.
        private static readonly LngLat ViewCenter = new(139.7670, 35.6814);

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[SkyAndLightDemo] MapLibreMap not found");
                yield break;
            }
            while (!_map.IsInitialized) yield return null;

            _font = SystemFontFallback.Resolve("Arial", 16);
            BuildUi();
            // Initial JumpTo + apply is deferred behind the first user slider
            // interaction (see ApplyOnFirstInteraction). Doing it during scene
            // boot triggered an intermittent PlayMode-test hang specifically
            // when this scene loaded right after another sample -- likely a
            // teardown timing issue between SymbolRenderer / SkyRenderer
            // re-initialisation and our SetSky / SetLight calls. Keeping
            // boot side-effect-free dodges that race entirely.
        }

        private void ApplyOnFirstInteraction()
        {
            if (_initialJumpDone) return;
            _initialJumpDone = true;
            try
            {
                // 3D buildings live at z13+ in openmaptiles; jump close so the
                // Light intensity / sun arc actually has a visible effect on the
                // fill-extrusion layer. Same exception as FillExtrusionDemo.
                _map.JumpTo(new MapLibre.Unity.CameraControl.JumpToOptions
                {
                    Center = ViewCenter, Zoom = 14f, Bearing = 0f, Pitch = 60f,
                });
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SkyAndLightDemo] Initial JumpTo failed: {e.Message}");
            }
        }

        private bool _initialJumpDone;

        private void BuildUi()
        {
            var go = new GameObject("SkyAndLightDemoUI");
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

            var panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = new Length(50, LengthUnit.Percent),
                    translate = new Translate(0, new Length(-50, LengthUnit.Percent)),
                    left = 14,
                    width = new Length(30, LengthUnit.Percent),
                    maxWidth = 380, minWidth = 260,
                    backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.88f),
                    borderTopLeftRadius = 10, borderTopRightRadius = 10,
                    borderBottomLeftRadius = 10, borderBottomRightRadius = 10,
                    paddingTop = 14, paddingBottom = 14, paddingLeft = 14, paddingRight = 14,
                    borderTopWidth = 1, borderBottomWidth = 1,
                    borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.12f),
                    borderBottomColor = new Color(1, 1, 1, 0.12f),
                    borderLeftColor = new Color(1, 1, 1, 0.12f),
                    borderRightColor = new Color(1, 1, 1, 0.12f),
                }
            };
            root.Add(panel);

            panel.Add(MakeLabel("Sky & Light", 14, Color.white, FontStyle.Bold));

            panel.Add(MakeLabel("Time of day (0=night, 0.5=noon, 1=night)", 11,
                new Color(1, 1, 1, 0.65f)));
            var todSlider = new Slider(0f, 1f) { value = _timeOfDay };
            StyleSlider(todSlider);
            todSlider.RegisterValueChangedCallback(e =>
            {
                ApplyOnFirstInteraction();
                _timeOfDay = e.newValue;
                ApplyAll();
            });
            panel.Add(todSlider);

            panel.Add(MakeLabel("Light intensity (0..1)", 11, new Color(1, 1, 1, 0.65f)));
            var intensitySlider = new Slider(0f, 1f) { value = _intensity };
            StyleSlider(intensitySlider);
            intensitySlider.RegisterValueChangedCallback(e =>
            {
                ApplyOnFirstInteraction();
                _intensity = e.newValue;
                ApplyLight();
            });
            panel.Add(intensitySlider);

            panel.Add(MakeLabel("Sky horizon blend (0=hard, 1=soft)", 11, new Color(1, 1, 1, 0.65f)));
            var blendSlider = new Slider(0f, 1f) { value = _horizonBlend };
            StyleSlider(blendSlider);
            blendSlider.RegisterValueChangedCallback(e =>
            {
                ApplyOnFirstInteraction();
                _horizonBlend = e.newValue;
                ApplySky();
            });
            panel.Add(blendSlider);

            var flatToggle = new Toggle("Flat light (anchor=map)") { value = _useFlatLight };
            flatToggle.style.color = Color.white;
            flatToggle.style.marginTop = 10;
            flatToggle.style.unityFontDefinition = StyleKeyword.None;
            flatToggle.style.unityFont = new StyleFont(_font);
            flatToggle.RegisterValueChangedCallback(e =>
            {
                ApplyOnFirstInteraction();
                _useFlatLight = e.newValue;
                ApplyLight();
            });
            panel.Add(flatToggle);

            _summary = MakeLabel("", 11, new Color(0.7f, 1f, 0.7f, 0.9f));
            _summary.style.marginTop = 8;
            _summary.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_summary);
        }

        // ── State application ──

        private void ApplyAll()
        {
            ApplySky();
            ApplyLight();
        }

        private void ApplySky()
        {
            // Map time-of-day to a simple two-stop gradient. The "noon"
            // colours hit at t=0.5; t=0/1 fade to night blue; t=0.25/0.75
            // sit on a warm horizon (dawn / dusk).
            Color sky, horizon;
            float t = _timeOfDay;
            if (t < 0.25f)
            {
                float k = t / 0.25f;
                sky     = Color.Lerp(new Color(0.04f, 0.05f, 0.10f), new Color(0.30f, 0.45f, 0.70f), k);
                horizon = Color.Lerp(new Color(0.12f, 0.10f, 0.20f), new Color(0.95f, 0.55f, 0.30f), k);
            }
            else if (t < 0.5f)
            {
                float k = (t - 0.25f) / 0.25f;
                sky     = Color.Lerp(new Color(0.30f, 0.45f, 0.70f), new Color(0.51f, 0.69f, 0.86f), k);
                horizon = Color.Lerp(new Color(0.95f, 0.55f, 0.30f), new Color(0.85f, 0.92f, 0.97f), k);
            }
            else if (t < 0.75f)
            {
                float k = (t - 0.5f) / 0.25f;
                sky     = Color.Lerp(new Color(0.51f, 0.69f, 0.86f), new Color(0.30f, 0.30f, 0.55f), k);
                horizon = Color.Lerp(new Color(0.85f, 0.92f, 0.97f), new Color(0.95f, 0.45f, 0.20f), k);
            }
            else
            {
                float k = (t - 0.75f) / 0.25f;
                sky     = Color.Lerp(new Color(0.30f, 0.30f, 0.55f), new Color(0.04f, 0.05f, 0.10f), k);
                horizon = Color.Lerp(new Color(0.95f, 0.45f, 0.20f), new Color(0.12f, 0.10f, 0.20f), k);
            }

            _map.SetSky(new SkyDefinition
            {
                SkyColor = sky,
                HorizonColor = horizon,
                SkyHorizonBlend = _horizonBlend,
            });
            UpdateSummary();
        }

        private void ApplyLight()
        {
            // Move the sun along an east → south → west arc as time advances.
            // Azimuth is measured CW from north, polar = angle from straight up.
            float azimuth = Mathf.Lerp(60f, 300f, _timeOfDay);
            float polar = Mathf.Lerp(80f, 10f, Mathf.Abs(0.5f - _timeOfDay) * 2f); // low at dawn/dusk, high at noon

            // Slightly warm sun colour at low polar (sunrise / sunset).
            float warmth = Mathf.Clamp01(polar / 60f);
            var color = Color.Lerp(new Color(1f, 0.65f, 0.45f), Color.white, warmth);

            _map.SetLight(new LightDefinition
            {
                Anchor = _useFlatLight ? "map" : "viewport",
                Position = new[] { 1.15f, azimuth, polar },
                Color = color,
                Intensity = _intensity,
            });
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (_summary == null) return;
            _summary.text =
                $"time={_timeOfDay:F2} intensity={_intensity:F2} blend={_horizonBlend:F2}\n" +
                $"anchor={(_useFlatLight ? "map" : "viewport")}";
        }

        // ── UI helpers ──

        private Label MakeLabel(string text, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color;
            l.style.unityFontStyleAndWeight = style;
            l.style.marginBottom = 4;
            l.style.unityFontDefinition = StyleKeyword.None;
            l.style.unityFont = new StyleFont(_font);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private void StyleSlider(Slider s)
        {
            SampleSliderStyle.Apply(s, _font);
        }
    }
}
