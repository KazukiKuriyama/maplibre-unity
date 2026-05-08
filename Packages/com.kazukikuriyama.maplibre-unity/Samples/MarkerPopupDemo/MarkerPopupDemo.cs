using UnityEngine;
using MapLibre.Unity.UI;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates Marker and Popup functionality.
    /// Attach to the same GameObject as MapLibreMap.
    ///
    /// - Pre-placed markers at notable Tokyo locations with popups
    /// - Click the map to add a new marker with popup showing coordinates
    /// - Draggable marker example
    /// </summary>
    public class MarkerPopupDemo : MonoBehaviour
    {
        private MapLibreMap _map;

        private void Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[MarkerPopupDemo] MapLibreMap component not found");
                return;
            }

            _map.On(MapEventType.Load, OnMapLoaded);
        }

        private void OnMapLoaded(MapEvent e)
        {
            // 1. Tokyo Tower marker (red)
            var tokyoTowerMarker = new Marker(new MarkerOptions { Color = new Color(0.9f, 0.2f, 0.2f) });
            var tokyoTowerPopup = new Popup();
            tokyoTowerPopup.SetText("Tokyo Tower\n333m, opened 1958");
            tokyoTowerMarker.SetLngLat(new LngLat(139.7454, 35.6586)).SetPopup(tokyoTowerPopup);
            _map.AddMarker(tokyoTowerMarker);

            // 2. Shibuya Crossing marker (blue, default)
            var shibuyaMarker = new Marker();
            var shibuyaPopup = new Popup();
            shibuyaPopup.SetText("Shibuya Crossing\nWorld's busiest pedestrian crossing");
            shibuyaMarker.SetLngLat(new LngLat(139.7005, 35.6594)).SetPopup(shibuyaPopup);
            _map.AddMarker(shibuyaMarker);

            // 3. Senso-ji marker (green)
            var sensojiMarker = new Marker(new MarkerOptions { Color = new Color(0.2f, 0.7f, 0.3f) });
            var sensojiPopup = new Popup();
            sensojiPopup.SetText("Senso-ji\nTokyo's oldest temple, founded 645 AD");
            sensojiMarker.SetLngLat(new LngLat(139.7966, 35.7148)).SetPopup(sensojiPopup);
            _map.AddMarker(sensojiMarker);

            // 4. Draggable marker (orange) -- starts at Tokyo Station
            var draggableMarker = new Marker(new MarkerOptions
            {
                Color = new Color(1f, 0.6f, 0f),
                Draggable = true,
            });
            var dragPopup = new Popup();
            dragPopup.SetText("Drag me!\nTokyo Station");
            draggableMarker.SetLngLat(new LngLat(139.7671, 35.6812)).SetPopup(dragPopup);
            draggableMarker.OnDragEnd += lngLat =>
            {
                dragPopup.SetText($"Dropped at\n{lngLat.Longitude:F4}, {lngLat.Latitude:F4}");
            };
            _map.AddMarker(draggableMarker);

            // 5. Click map to add marker
            _map.On(MapEventType.Click, OnMapClick);

            Debug.Log("[MarkerPopupDemo] Loaded -- click the map to add markers, click markers to toggle popups");
        }

        private void OnMapClick(MapEvent e)
        {
            if (!e.LngLat.HasValue) return;

            var lngLat = e.LngLat.Value;
            var marker = new Marker(new MarkerOptions { Color = new Color(0.6f, 0.3f, 0.8f) });
            var popup = new Popup(new PopupOptions { DeleteButton = true });
            popup.SetText($"{lngLat.Longitude:F4}, {lngLat.Latitude:F4}");
            popup.OnDelete += () => _map.RemoveMarker(marker);
            marker.SetLngLat(lngLat).SetPopup(popup);
            _map.AddMarker(marker);
        }

        private void OnDestroy()
        {
            if (_map != null)
            {
                _map.Off(MapEventType.Load, OnMapLoaded);
                _map.Off(MapEventType.Click, OnMapClick);
            }
        }
    }
}
