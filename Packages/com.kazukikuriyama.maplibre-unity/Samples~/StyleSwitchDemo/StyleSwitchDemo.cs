using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates the SetStyle() API — switching map styles at runtime.
    /// Attach this MonoBehaviour to the same GameObject as MapLibreMap.
    ///
    /// Usage: Press the number keys (1–3) to switch between predefined styles,
    /// or assign custom style URLs in the Inspector.
    /// </summary>
    public class StyleSwitchDemo : MonoBehaviour
    {
        [Header("Style URLs")]
        [Tooltip("Style URLs to cycle through. Press 1/2/3... or click the UI buttons to switch.")]
        [SerializeField] private string[] _styleUrls = new[]
        {
            "https://demotiles.maplibre.org/style.json",
            "https://tile.openstreetmap.jp/styles/osm-bright-ja/style.json",
            "https://tile.openstreetmap.jp/styles/maptiler-basic-ja/style.json",
        };

        [Header("Style Labels")]
        [Tooltip("Display labels for each style. Must match _styleUrls length.")]
        [SerializeField] private string[] _styleLabels = new[]
        {
            "MapLibre Demo",
            "OSM Bright",
            "MapTiler Basic",
        };

        private MapLibreMap _map;
        private int _currentStyleIndex;
        private bool _isSwitching;

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

            Debug.Log("[StyleSwitchDemo] Ready. Press 1/2/3 to switch styles.");
        }

        private void Update()
        {
            if (_map == null || _map.State == null || _isSwitching) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Number keys 1-9 switch to corresponding style index
            Key[] numberKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                                 Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < _styleUrls.Length && i < numberKeys.Length; i++)
            {
                if (keyboard[numberKeys[i]].wasPressedThisFrame)
                {
                    SwitchToStyle(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Switch to a style by index. Can be called from UI buttons.
        /// </summary>
        public void SwitchToStyle(int index)
        {
            if (index < 0 || index >= _styleUrls.Length) return;
            if (_isSwitching) return;
            if (index == _currentStyleIndex) return;

            _currentStyleIndex = index;
            _isSwitching = true;

            string label = index < _styleLabels.Length ? _styleLabels[index] : $"Style {index + 1}";
            Debug.Log($"[StyleSwitchDemo] Switching to: {label}");

            _map.SetStyle(_styleUrls[index]);

            // Listen for load event to know when switch is complete
            _map.Once(MapEventType.Load, _ =>
            {
                _isSwitching = false;
                Debug.Log($"[StyleSwitchDemo] Style loaded: {label}");
            });
        }

        private void OnGUI()
        {
            if (_map == null || _map.State == null) return;

            float buttonWidth = 140f;
            float buttonHeight = 30f;
            float padding = 5f;
            float startX = 10f;
            float startY = Screen.height - buttonHeight - 10f;

            for (int i = 0; i < _styleUrls.Length; i++)
            {
                string label = i < _styleLabels.Length ? _styleLabels[i] : $"Style {i + 1}";
                float x = startX + i * (buttonWidth + padding);

                bool isActive = i == _currentStyleIndex;
                GUI.enabled = !_isSwitching && !isActive;

                if (isActive)
                    label = $"[{label}]";

                if (GUI.Button(new Rect(x, startY, buttonWidth, buttonHeight), label))
                {
                    SwitchToStyle(i);
                }
            }
            GUI.enabled = true;
        }
    }
}
