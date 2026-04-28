using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.UI
{
    /// <summary>
    /// Manages markers and popups on the map.
    /// Handles the UITK overlay layer and per-frame position updates.
    /// Created automatically by MapLibreMap when the first marker or popup is added.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MarkerManager : MonoBehaviour
    {
        private MapLibreMap _map;
        private VisualElement _root;
        private VisualElement _markerLayer;
        private VisualElement _popupLayer;
        private readonly List<Marker> _markers = new();
        private readonly List<Popup> _standalonePopups = new();
        private bool _uiReady;

        public void Initialize(MapLibreMap map, PanelSettings panelSettings)
        {
            _map = map;

            var uiDoc = GetComponent<UIDocument>();
            uiDoc.panelSettings = panelSettings;
            uiDoc.sortingOrder = 200; // Above map controls overlay (100)
        }

        /// <summary>
        /// Returns true if the given screen position is over a marker or popup element.
        /// Uses UITK panel Pick to detect hit before UITK processes its own events.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPos)
        {
            if (!_uiReady || _root?.panel == null) return false;

            // ScreenToPanel expects OS screen coordinates (origin top-left)
            var panelPos = RuntimePanelUtils.ScreenToPanel(
                _root.panel,
                new Vector2(screenPos.x, Screen.height - screenPos.y));
            var picked = _root.panel.Pick(panelPos);
            return picked != null && picked != _root
                   && picked != _markerLayer && picked != _popupLayer;
        }

        /// <summary>
        /// Add a marker to the map.
        /// </summary>
        public void AddMarker(Marker marker)
        {
            _markers.Add(marker);

            // Ensure popup has map reference for coordinate projection
            var popup = marker.GetPopup();
            popup?.AddTo(_map);

            if (_uiReady)
                BuildMarker(marker);
        }

        /// <summary>
        /// Remove a marker from the map.
        /// </summary>
        public void RemoveMarker(Marker marker)
        {
            marker.Remove();
            _markers.Remove(marker);
        }

        /// <summary>
        /// Add a standalone popup (not attached to a marker) to the map.
        /// </summary>
        public void AddPopup(Popup popup, bool startOpen = true)
        {
            _standalonePopups.Add(popup);

            if (_uiReady)
                popup.Build(_popupLayer, startOpen);
        }

        /// <summary>
        /// Remove a standalone popup from the map.
        /// </summary>
        public void RemovePopup(Popup popup)
        {
            popup.Remove();
            _standalonePopups.Remove(popup);
        }

        private void BuildMarker(Marker marker)
        {
            marker.Build(_markerLayer);
            var popup = marker.GetPopup();
            if (popup != null)
            {
                popup.SetMarker(marker);
                popup.Build(_popupLayer);
            }
        }

        private static VisualElement CreateLayer(string name)
        {
            var layer = new VisualElement { name = name };
            layer.pickingMode = PickingMode.Ignore;
            layer.style.position = Position.Absolute;
            layer.style.left = 0;
            layer.style.top = 0;
            layer.style.right = 0;
            layer.style.bottom = 0;
            return layer;
        }

        private void Update()
        {
            if (!_uiReady)
            {
                var uiDoc = GetComponent<UIDocument>();
                if (uiDoc == null || uiDoc.rootVisualElement == null) return;

                _root = uiDoc.rootVisualElement;
                _root.pickingMode = PickingMode.Ignore;
                _root.style.position = Position.Absolute;
                _root.style.left = 0;
                _root.style.top = 0;
                _root.style.right = 0;
                _root.style.bottom = 0;

                // Markers below, popups always on top
                _markerLayer = CreateLayer("marker-layer");
                _popupLayer = CreateLayer("popup-layer");
                _root.Add(_markerLayer);
                _root.Add(_popupLayer);

                _uiReady = true;

                // Build any markers/popups that were added before UI was ready
                foreach (var marker in _markers)
                    BuildMarker(marker);

                foreach (var popup in _standalonePopups)
                    popup.Build(_popupLayer, popup.IsOpen);
            }

            // Update positions every frame
            foreach (var marker in _markers)
                marker.UpdatePosition();

            foreach (var popup in _standalonePopups)
                popup.UpdatePosition();
        }
    }
}
