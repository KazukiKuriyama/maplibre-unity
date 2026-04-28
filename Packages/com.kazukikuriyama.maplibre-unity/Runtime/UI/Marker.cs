using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MapLibre.Unity.UI
{
    /// <summary>
    /// A marker displayed on the map at a geographic coordinate.
    /// Matches the Marker class in MapLibre GL JS.
    /// </summary>
    public class Marker
    {
        private LngLat _lngLat;
        private MapLibreMap _map;
        private VisualElement _root;
        private VisualElement _element;
        private Popup _popup;
        private bool _isDraggable;
        private bool _isDragging;
        private Vector2 _lastPointerPos;
        private Color _color = new(0.25f, 0.55f, 0.96f, 1f);
        private Vector2 _offset;

        /// <summary>
        /// Fires when the marker's LngLat changes (e.g. via drag).
        /// </summary>
        public event Action<LngLat> OnDragEnd;

        /// <summary>
        /// The geographic position of this marker.
        /// </summary>
        public LngLat LngLat => _lngLat;

        /// <summary>
        /// The root VisualElement containing the marker.
        /// </summary>
        public VisualElement Element => _root;

        /// <summary>
        /// Create a marker with default pin appearance.
        /// </summary>
        public Marker(MarkerOptions options = default)
        {
            if (options.Color.HasValue) _color = options.Color.Value;
            _isDraggable = options.Draggable;
            _offset = options.Offset;
            _element = options.Element;
        }

        /// <summary>
        /// Set the marker's geographic position. Matches marker.setLngLat() in MapLibre GL JS.
        /// </summary>
        public Marker SetLngLat(LngLat lngLat)
        {
            _lngLat = lngLat;
            UpdatePosition();
            return this;
        }

        /// <summary>
        /// Get the marker's geographic position. Matches marker.getLngLat() in MapLibre GL JS.
        /// </summary>
        public LngLat GetLngLat() => _lngLat;

        /// <summary>
        /// Associate a popup with this marker. The popup will toggle on marker click.
        /// Matches marker.setPopup() in MapLibre GL JS.
        /// </summary>
        public Marker SetPopup(Popup popup)
        {
            _popup = popup;
            return this;
        }

        /// <summary>
        /// Get the popup associated with this marker.
        /// </summary>
        public Popup GetPopup() => _popup;

        /// <summary>
        /// Add this marker to the map. Matches marker.addTo(map) in MapLibre GL JS.
        /// </summary>
        public Marker AddTo(MapLibreMap map)
        {
            _map = map;
            return this;
        }

        /// <summary>
        /// Remove the marker from the map. Matches marker.remove() in MapLibre GL JS.
        /// </summary>
        public void Remove()
        {
            _root?.RemoveFromHierarchy();
            _popup?.Remove();
            _map = null;
        }

        /// <summary>
        /// Called by the marker manager to build the UI and attach to the UITK tree.
        /// </summary>
        internal void Build(VisualElement parent)
        {
            _root = new VisualElement { name = "marker" };
            _root.style.position = Position.Absolute;
            _root.pickingMode = PickingMode.Position;

            if (_element != null)
            {
                _root.Add(_element);
            }
            else
            {
                BuildDefaultPin();
            }

            if (_isDraggable)
            {
                _root.RegisterCallback<PointerDownEvent>(OnPointerDown);
                _root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                _root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            }

            _root.RegisterCallback<ClickEvent>(OnClick);

            parent.Add(_root);
        }

        private void BuildDefaultPin()
        {
            // Outer pin shape: a circle with a triangle pointer at the bottom
            var pin = new VisualElement { name = "marker-pin" };
            pin.pickingMode = PickingMode.Ignore;
            pin.style.width = 27;
            pin.style.height = 41;
            pin.style.alignItems = Align.Center;

            // Circle
            var circle = new VisualElement { name = "marker-circle" };
            circle.pickingMode = PickingMode.Ignore;
            circle.style.width = 27;
            circle.style.height = 27;
            circle.style.borderTopLeftRadius = 14;
            circle.style.borderTopRightRadius = 14;
            circle.style.borderBottomLeftRadius = 14;
            circle.style.borderBottomRightRadius = 14;
            circle.style.backgroundColor = _color;
            circle.style.borderTopWidth = 2;
            circle.style.borderBottomWidth = 2;
            circle.style.borderLeftWidth = 2;
            circle.style.borderRightWidth = 2;
            circle.style.borderTopColor = Color.white;
            circle.style.borderBottomColor = Color.white;
            circle.style.borderLeftColor = Color.white;
            circle.style.borderRightColor = Color.white;
            circle.style.alignItems = Align.Center;
            circle.style.justifyContent = Justify.Center;

            // Inner dot
            var dot = new VisualElement { name = "marker-dot" };
            dot.pickingMode = PickingMode.Ignore;
            dot.style.width = 9;
            dot.style.height = 9;
            dot.style.borderTopLeftRadius = 5;
            dot.style.borderTopRightRadius = 5;
            dot.style.borderBottomLeftRadius = 5;
            dot.style.borderBottomRightRadius = 5;
            dot.style.backgroundColor = Color.white;
            circle.Add(dot);

            // Triangle pointer
            var pointer = new VisualElement { name = "marker-pointer" };
            pointer.pickingMode = PickingMode.Ignore;
            pointer.style.width = 0;
            pointer.style.height = 0;
            pointer.style.borderLeftWidth = 8;
            pointer.style.borderRightWidth = 8;
            pointer.style.borderTopWidth = 14;
            pointer.style.borderLeftColor = Color.clear;
            pointer.style.borderRightColor = Color.clear;
            pointer.style.borderTopColor = _color;
            pointer.style.marginTop = -2;

            pin.Add(circle);
            pin.Add(pointer);
            _root.Add(pin);

            // Anchor at bottom-center of the pin
            _root.style.translate = new StyleTranslate(
                new Translate(new Length(-50, LengthUnit.Percent), new Length(-100, LengthUnit.Percent)));
        }

        /// <summary>
        /// Update the marker's screen position from its LngLat.
        /// Called each frame by the marker manager.
        /// </summary>
        internal void UpdatePosition()
        {
            if (_map == null || _root == null) return;
            if (_isDragging) return;

            var panelPos = ProjectToPanel(_lngLat);
            if (panelPos.HasValue)
            {
                _root.style.left = panelPos.Value.x + _offset.x;
                _root.style.top = panelPos.Value.y + _offset.y;
                _root.style.display = DisplayStyle.Flex;
            }
            else
            {
                _root.style.display = DisplayStyle.None;
            }

            _popup?.UpdatePosition();
        }

        /// <summary>
        /// Convert LngLat directly to UITK panel coordinates via world space.
        /// Avoids screen coordinate ambiguity by using CameraTransformWorldToPanel.
        /// </summary>
        private Vector2? ProjectToPanel(LngLat lngLat)
        {
            if (_root?.panel == null || _map?.MapCamera == null) return null;

            var worldPos = _map.LngLatToWorld(lngLat);
            if (!worldPos.HasValue) return null;

            return RuntimePanelUtils.CameraTransformWorldToPanel(
                _root.panel, worldPos.Value, _map.MapCamera);
        }

        private void OnClick(ClickEvent evt)
        {
            if (_isDragging) return;

            if (_popup != null)
                _popup.TogglePopup();

            evt.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!_isDraggable) return;
            _isDragging = true;
            _root.CapturePointer(evt.pointerId);
            _lastPointerPos = evt.position;
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_isDragging) return;

            Vector2 currentPos = evt.position;
            Vector2 delta = currentPos - _lastPointerPos;
            _lastPointerPos = currentPos;

            _root.style.left = _root.resolvedStyle.left + delta.x;
            _root.style.top = _root.resolvedStyle.top + delta.y;

            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_isDragging) return;
            _isDragging = false;
            _root.ReleasePointer(evt.pointerId);

            // Convert final position back to LngLat using Mouse screen coordinates
            if (_map != null)
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    var screenPos = mouse.position.ReadValue();
                    var lngLat = _map.Unproject(screenPos);
                    if (lngLat.HasValue)
                    {
                        _lngLat = lngLat.Value;
                        OnDragEnd?.Invoke(_lngLat);
                    }
                }
            }

            evt.StopPropagation();
        }
    }

    /// <summary>
    /// Options for creating a Marker.
    /// </summary>
    public struct MarkerOptions
    {
        /// <summary>
        /// Custom color for the default pin marker.
        /// </summary>
        public Color? Color;

        /// <summary>
        /// Whether the marker is draggable.
        /// </summary>
        public bool Draggable;

        /// <summary>
        /// Pixel offset [x, y] from the marker's LngLat.
        /// </summary>
        public Vector2 Offset;

        /// <summary>
        /// Custom VisualElement to use instead of the default pin.
        /// </summary>
        public VisualElement Element;
    }
}
