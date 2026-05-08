using System.Collections;
using MapLibre.Unity.CameraControl;
using MapLibre.Unity.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Drives the map's <see cref="UnityEngine.Camera"/> from outside the
    /// built-in <c>MapInputHandler</c> rig using
    /// <c>MapLibreMap.SetFreeCameraOptions</c>. The script orbits the camera
    /// around a fixed LngLat target using a yaw / pitch / radius parameter
    /// triplet, the way a Cinemachine virtual cam or a third-person rig
    /// would. Built-in pan / zoom is disabled while the orbit is active so
    /// the two systems don't fight for camera ownership.
    /// </summary>
    public class FreeCameraDemo : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;

        private MapLibreMap _map;
        private Font _font;
        private Label _summary;

        private bool _orbitEnabled;
        private float _yawDeg;
        private float _pitchDeg = 35f;
        private float _radiusWorld = 600f;
        private float _orbitSpeed = 30f;

        // Pin orbit centre to Tokyo Station for consistency with other demos.
        private static readonly LngLat OrbitCenter = new(139.7670, 35.6814);

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[FreeCameraDemo] MapLibreMap not found");
                yield break;
            }
            while (!_map.IsInitialized) yield return null;

            _font = SystemFontFallback.Resolve("Arial", 16);
            _map.JumpTo(new JumpToOptions { Center = OrbitCenter, Zoom = 9f });
            BuildUi();
        }

        private void Update()
        {
            if (!_orbitEnabled || _map == null) return;
            _yawDeg = (_yawDeg + _orbitSpeed * Time.deltaTime) % 360f;
            ApplyOrbit();
        }

        private void ApplyOrbit()
        {
            // Orbit centre as a Unity world-space anchor. The map keeps its
            // centre LngLat at the Unity origin so the map root is the right
            // pivot.
            var pivot = _map.transform.position;

            // Spherical → cartesian. Yaw rotates around Y, pitch from horizon.
            float yawRad = _yawDeg * Mathf.Deg2Rad;
            float pitchRad = _pitchDeg * Mathf.Deg2Rad;
            float horiz = Mathf.Cos(pitchRad) * _radiusWorld;
            var offset = new Vector3(
                Mathf.Sin(yawRad) * horiz,
                Mathf.Sin(pitchRad) * _radiusWorld,
                Mathf.Cos(yawRad) * horiz);

            _map.SetFreeCameraOptions(FreeCameraOptions.LookAt(pivot + offset, pivot));

            if (_summary != null)
                _summary.text =
                    $"yaw={_yawDeg:F0}° pitch={_pitchDeg:F0}° radius={_radiusWorld:F0}u\n" +
                    $"speed={_orbitSpeed:F0}°/s -- built-in input disabled while orbiting";
        }

        // ── UI ──

        private void BuildUi()
        {
            var go = new GameObject("FreeCameraDemoUI");
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

            panel.Add(MakeLabel("Free camera (external rig)", 14, Color.white, FontStyle.Bold));

            var orbitToggle = new Toggle("Enable orbit") { value = _orbitEnabled };
            orbitToggle.style.color = Color.white;
            orbitToggle.style.marginTop = 6;
            orbitToggle.style.unityFontDefinition = StyleKeyword.None;
            orbitToggle.style.unityFont = new StyleFont(_font);
            orbitToggle.RegisterValueChangedCallback(e =>
            {
                _orbitEnabled = e.newValue;
                // Disable the built-in input handler while we own the camera so
                // mouse drag doesn't fight the orbit. The handler lives on the
                // camera GameObject (added by MapLibreMap.Initialize), not on
                // _map -- querying _map silently returned null and left the
                // handler running.
                var input = _map.MapCamera != null
                    ? _map.MapCamera.GetComponent<MapInputHandler>()
                    : null;
                if (input != null) input.enabled = !_orbitEnabled;
                if (_orbitEnabled) ApplyOrbit();
                else _map.ResumeMapDrivenCamera();
            });
            panel.Add(orbitToggle);

            panel.Add(SliderRow("Pitch (°)", 5f, 80f, _pitchDeg, v => { _pitchDeg = v; ApplyOrbit(); }));
            panel.Add(SliderRow("Radius (world units)", 100f, 2000f, _radiusWorld, v => { _radiusWorld = v; ApplyOrbit(); }));
            panel.Add(SliderRow("Speed (°/s)", 0f, 90f, _orbitSpeed, v => _orbitSpeed = v));

            _summary = MakeLabel("Toggle orbit to drive the map camera externally.", 11,
                new Color(0.7f, 1f, 0.7f, 0.9f));
            _summary.style.marginTop = 8;
            _summary.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_summary);
        }

        private VisualElement SliderRow(string label, float min, float max, float initial,
            System.Action<float> onChange)
        {
            var row = new VisualElement();
            row.style.marginTop = 5;
            row.Add(MakeLabel(label, 11, new Color(1, 1, 1, 0.65f)));
            var slider = new Slider(min, max) { value = initial };
            SampleSliderStyle.Apply(slider, _font);
            slider.RegisterValueChangedCallback(e => onChange(e.newValue));
            row.Add(slider);
            return row;
        }

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
    }
}
