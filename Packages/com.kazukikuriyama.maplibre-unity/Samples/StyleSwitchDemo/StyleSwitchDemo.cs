using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates runtime layer-visibility switching across multiple raster
    /// sources. The scene's initial style JSON declares three raster layers
    /// (OSM Standard / CartoDB Voyager / OpenTopoMap) all anchored to Tokyo,
    /// with only the first visible. The demo's buttons flip
    /// <c>layout.visibility</c> on those layers via SetLayoutProperty so the
    /// user sees three visually distinct basemaps without paying for a full
    /// SetStyle teardown.
    ///
    /// Why visibility instead of SetStyle: ApplyStyleJsonInternalAsync runs
    /// synchronously when the style is inline raster (no awaits actually
    /// pause), so the dispose-and-rebuild path can stall the main thread long
    /// enough to trigger macOS' unresponsive-app watchdog. Visibility flips
    /// reuse the existing renderer / tile pool.
    ///
    /// Attach this MonoBehaviour to the same GameObject as MapLibreMap.
    /// Press number keys 1–3 or click the on-screen buttons to switch.
    /// </summary>
    public class StyleSwitchDemo : MonoBehaviour
    {
        [Header("Layer ids to flip between")]
        [Tooltip("Layer ids declared in the scene's initial style JSON. The " +
                 "selected layer's visibility is set to 'visible' and all " +
                 "others to 'none'. This is much lighter than SetStyle() -- " +
                 "no teardown of sources / renderers -- and avoids the editor " +
                 "freeze that a full SetStyle path can cause when multiple " +
                 "raster sources are swapped on the main thread.")]
        [SerializeField] private string[] _layerIds = new[]
        {
            "osm-layer",
            "voyager-layer",
            "topo-layer",
        };

        [Header("Style Labels")]
        [Tooltip("Display labels for each layer. Must match _layerIds length.")]
        [SerializeField] private string[] _styleLabels = new[]
        {
            "OSM Standard",
            "CartoDB Voyager",
            "OpenTopoMap",
        };

        [Header("View")]
        [Tooltip("Camera target after the initial style finishes loading. " +
                 "All three layers cover the world at the same Mercator " +
                 "projection, so a single view target works for every option.")]
        [SerializeField] private double _viewLongitude = 139.767;
        [SerializeField] private double _viewLatitude = 35.6814;
        [SerializeField] private float _viewZoom = 9f;

        private MapLibreMap _map;
        private int _currentStyleIndex;
        private int? _pendingSwitchIndex;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[StyleSwitchDemo] MapLibreMap component not found on this GameObject");
                yield break;
            }

            // Wait for map initialization
            while (!_map.IsInitialized)
                yield return null;

            // The style JSON's center/zoom may not match the demo's intent --
            // snap to the demo target so the visual differences between
            // basemaps show at the same Tokyo view.
            ApplyTargetView();

            Debug.Log("[StyleSwitchDemo] Ready. Press 1/2/3 to switch basemaps.");
        }

        private void ApplyTargetView()
        {
            _map.SetCenter(new LngLat(_viewLongitude, _viewLatitude));
            _map.SetZoom(_viewZoom);
        }

        private void Update()
        {
            if (_map == null || _map.State == null) return;

            // Process a deferred switch first. SwitchToStyle queues here from
            // OnGUI / keyboard so the actual SetLayoutProperty calls don't
            // happen mid-render -- even though visibility flipping is light,
            // running renderer mutations during OnGUI is risky.
            if (_pendingSwitchIndex.HasValue)
            {
                int idx = _pendingSwitchIndex.Value;
                _pendingSwitchIndex = null;
                DoSwitch(idx);
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Number keys 1-9 switch to corresponding layer index
            Key[] numberKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                                 Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < _layerIds.Length && i < numberKeys.Length; i++)
            {
                if (keyboard[numberKeys[i]].wasPressedThisFrame)
                {
                    SwitchToStyle(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Schedule a basemap switch. The actual visibility flip runs on the
        /// next Update tick to avoid mutating the renderer during OnGUI /
        /// rendering.
        /// </summary>
        public void SwitchToStyle(int index)
        {
            if (index < 0 || index >= _layerIds.Length) return;
            if (index == _currentStyleIndex) return;
            _pendingSwitchIndex = index;
        }

        private void DoSwitch(int index)
        {
            if (index < 0 || index >= _layerIds.Length) return;
            if (index == _currentStyleIndex) return;

            string label = index < _styleLabels.Length ? _styleLabels[index] : $"Layer {index + 1}";
            Debug.Log($"[StyleSwitchDemo] Switching to: {label}");

            // Set visibility="visible" on the selected layer and "none" on all
            // others. SetLayoutProperty triggers RefreshTiles internally; doing
            // it N times in a single frame coalesces to a single tile re-fetch
            // for the new visible layer, so the cost is fine.
            for (int i = 0; i < _layerIds.Length; i++)
            {
                _map.SetLayoutProperty(_layerIds[i], "visibility",
                    i == index ? "visible" : "none");
            }

            _currentStyleIndex = index;
        }

        private GUIStyle _buttonStyle;

        private void OnGUI()
        {
            if (_map == null || _map.State == null) return;

            // Default IMGUI font is ~12pt -- readable on a 1080p display but
            // unusable on Retina / high-DPI screens. Build a one-off style
            // sized to the screen height so the labels stay legible.
            int fontSize = Mathf.Max(20, Screen.height / 40);
            if (_buttonStyle == null || _buttonStyle.fontSize != fontSize)
            {
                _buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = fontSize,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            // Width scales with font size so the longest label
            // ("[CartoDB Voyager]" -- 17 chars including the active brackets)
            // fits without truncation on Retina / 4K displays. A fixed 240px
            // worked at 1080p but clipped the text once Screen.height/40
            // pushed font size past ~24.
            float buttonWidth = fontSize * 11f;
            float buttonHeight = fontSize * 2.2f;
            float padding = 8f;
            float startX = 16f;
            float startY = Screen.height - buttonHeight - 16f;

            for (int i = 0; i < _layerIds.Length; i++)
            {
                string label = i < _styleLabels.Length ? _styleLabels[i] : $"Layer {i + 1}";
                float x = startX + i * (buttonWidth + padding);

                bool isActive = i == _currentStyleIndex;
                GUI.enabled = !isActive;

                if (isActive)
                    label = $"[{label}]";

                if (GUI.Button(new Rect(x, startY, buttonWidth, buttonHeight), label, _buttonStyle))
                {
                    SwitchToStyle(i);
                }
            }
            GUI.enabled = true;
        }
    }
}
