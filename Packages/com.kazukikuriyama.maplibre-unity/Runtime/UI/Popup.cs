using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.UI
{
    /// <summary>
    /// A popup displayed on the map at a geographic coordinate or attached to a marker.
    /// Matches the Popup class in MapLibre GL JS.
    /// </summary>
    public class Popup
    {
        private LngLat? _lngLat;
        private MapLibreMap _map;
        private Marker _marker;
        private VisualElement _root;
        private VisualElement _contentContainer;
        private Label _textLabel;
        private bool _isOpen;
        private bool _closeButton = true;
        private bool _deleteButton;
        private string _className;
        private Vector2 _offset;
        private int _maxWidth = 240;
        private string _pendingText;

        /// <summary>
        /// Fires when the popup is opened.
        /// </summary>
        public event Action OnOpen;

        /// <summary>
        /// Fires when the popup is closed.
        /// </summary>
        public event Action OnClose;

        /// <summary>
        /// Fires when the delete button is clicked.
        /// </summary>
        public event Action OnDelete;

        /// <summary>
        /// Whether the popup is currently visible.
        /// </summary>
        public bool IsOpen => _isOpen;

        /// <summary>
        /// The root VisualElement of the popup.
        /// </summary>
        public VisualElement Element => _root;

        /// <summary>
        /// Create a popup.
        /// </summary>
        public Popup(PopupOptions options = default)
        {
            _closeButton = options.CloseButton ?? true;
            _deleteButton = options.DeleteButton;
            _offset = options.Offset;
            _maxWidth = options.MaxWidth > 0 ? options.MaxWidth : 240;
            _className = options.ClassName;
        }

        /// <summary>
        /// Set the popup's geographic position. Matches popup.setLngLat() in MapLibre GL JS.
        /// </summary>
        public Popup SetLngLat(LngLat lngLat)
        {
            _lngLat = lngLat;
            UpdatePosition();
            return this;
        }

        /// <summary>
        /// Set popup text content. Matches popup.setText() in MapLibre GL JS.
        /// </summary>
        public Popup SetText(string text)
        {
            _pendingText = text;
            if (_textLabel != null)
                _textLabel.text = text;
            return this;
        }

        /// <summary>
        /// Set custom VisualElement as popup content. Matches popup.setDOMContent() in MapLibre GL JS.
        /// </summary>
        public Popup SetDOMContent(VisualElement content)
        {
            if (_contentContainer == null) return this;
            _contentContainer.Clear();
            _textLabel = null;
            _pendingText = null;
            _contentContainer.Add(content);
            return this;
        }

        /// <summary>
        /// Add this popup to the map. Matches popup.addTo(map) in MapLibre GL JS.
        /// </summary>
        public Popup AddTo(MapLibreMap map)
        {
            _map = map;
            return this;
        }

        /// <summary>
        /// Remove the popup from the map. Matches popup.remove() in MapLibre GL JS.
        /// </summary>
        public void Remove()
        {
            _isOpen = false;
            _root?.RemoveFromHierarchy();
            _map = null;
        }

        /// <summary>
        /// Open the popup (make it visible).
        /// </summary>
        public void Open()
        {
            if (_root == null) return;
            _isOpen = true;
            _root.style.display = DisplayStyle.Flex;
            UpdatePosition();
            OnOpen?.Invoke();
        }

        /// <summary>
        /// Close the popup (hide it).
        /// </summary>
        public void Close()
        {
            if (_root == null) return;
            _isOpen = false;
            _root.style.display = DisplayStyle.None;
            OnClose?.Invoke();
        }

        /// <summary>
        /// Toggle the popup open/closed.
        /// </summary>
        public void TogglePopup()
        {
            if (_isOpen) Close();
            else Open();
        }

        /// <summary>
        /// Associate this popup with a marker. The popup position will follow the marker.
        /// </summary>
        internal void SetMarker(Marker marker)
        {
            _marker = marker;
        }

        /// <summary>
        /// Called by the marker/popup manager to build the UI and attach to the UITK tree.
        /// </summary>
        internal void Build(VisualElement parent, bool startOpen = false)
        {
            _root = new VisualElement { name = "popup" };
            _root.style.position = Position.Absolute;
            _root.pickingMode = PickingMode.Position;

            if (!string.IsNullOrEmpty(_className))
                _root.AddToClassList(_className);

            // Anchor at bottom-center, positioned above the marker
            _root.style.translate = new StyleTranslate(
                new Translate(new Length(-50, LengthUnit.Percent), new Length(-100, LengthUnit.Percent)));

            // Popup container (white card)
            var card = new VisualElement { name = "popup-card" };
            card.style.backgroundColor = new Color(1f, 1f, 1f, 0.95f);
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = new Color(0, 0, 0, 0.15f);
            card.style.borderBottomColor = new Color(0, 0, 0, 0.15f);
            card.style.borderLeftColor = new Color(0, 0, 0, 0.15f);
            card.style.borderRightColor = new Color(0, 0, 0, 0.15f);
            card.style.maxWidth = _maxWidth;
            card.style.minWidth = 100;

            // Explicitly set a font so text renders even without a PanelSettings theme
            var font = SystemFontFallback.Resolve("Arial", 12);

            // Header with optional close/delete buttons
            if (_closeButton || _deleteButton)
            {
                var header = new VisualElement { name = "popup-header" };
                header.style.flexDirection = FlexDirection.RowReverse;
                header.style.alignItems = Align.Center;
                header.style.paddingRight = 4;
                header.style.paddingTop = 2;

                if (_closeButton)
                {
                    var closeBtn = CreateHeaderButton(font, "\u00D7", new Color(0.4f, 0.4f, 0.4f));
                    closeBtn.name = "popup-close";
                    closeBtn.clicked += Close;
                    header.Add(closeBtn);
                }

                if (_deleteButton)
                {
                    var deleteBtn = CreateHeaderButton(font, "Delete", new Color(0.8f, 0.2f, 0.2f));
                    deleteBtn.name = "popup-delete";
                    deleteBtn.style.fontSize = 11;
                    deleteBtn.style.width = StyleKeyword.Auto;
                    deleteBtn.style.height = 20;
                    deleteBtn.style.paddingLeft = 4;
                    deleteBtn.style.paddingRight = 4;
                    deleteBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                    deleteBtn.clicked += () => OnDelete?.Invoke();
                    header.Add(deleteBtn);
                }

                card.Add(header);
            }

            // Content area
            _contentContainer = new VisualElement { name = "popup-content" };
            _contentContainer.style.paddingTop = _closeButton ? 0 : 8;
            _contentContainer.style.paddingBottom = 8;
            _contentContainer.style.paddingLeft = 10;
            _contentContainer.style.paddingRight = 10;

            _textLabel = new Label { name = "popup-text", text = _pendingText ?? "" };
            _textLabel.style.fontSize = 12;
            _textLabel.style.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            _textLabel.style.whiteSpace = WhiteSpace.Normal;
            _textLabel.style.unityFontDefinition = StyleKeyword.None;
            _textLabel.style.unityFont = new StyleFont(font);
            _contentContainer.Add(_textLabel);

            card.Add(_contentContainer);

            // Tip triangle pointing down
            var tip = new VisualElement { name = "popup-tip" };
            tip.pickingMode = PickingMode.Ignore;
            tip.style.alignSelf = Align.Center;
            tip.style.width = 0;
            tip.style.height = 0;
            tip.style.borderLeftWidth = 8;
            tip.style.borderRightWidth = 8;
            tip.style.borderTopWidth = 8;
            tip.style.borderLeftColor = Color.clear;
            tip.style.borderRightColor = Color.clear;
            tip.style.borderTopColor = new Color(1f, 1f, 1f, 0.95f);

            _root.Add(card);
            _root.Add(tip);

            _root.style.display = startOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _isOpen = startOpen;

            parent.Add(_root);
        }

        private static Button CreateHeaderButton(Font font, string text, Color color)
        {
            var btn = new Button { text = text };
            btn.style.backgroundColor = Color.clear;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.fontSize = 16;
            btn.style.color = color;
            btn.style.width = 20;
            btn.style.height = 20;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.marginTop = 0;
            btn.style.marginBottom = 0;
            btn.style.marginLeft = 0;
            btn.style.marginRight = 0;
            btn.style.unityFontDefinition = StyleKeyword.None;
            btn.style.unityFont = new StyleFont(font);
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            return btn;
        }

        /// <summary>
        /// Update the popup's screen position from its LngLat or marker position.
        /// Called each frame by the marker/popup manager.
        /// </summary>
        internal void UpdatePosition()
        {
            if (_root == null || _map == null) return;
            if (!_isOpen) return;

            LngLat? lngLat = _lngLat;
            if (_marker != null) lngLat = _marker.LngLat;
            if (!lngLat.HasValue) return;

            var panelPos = ProjectToPanel(lngLat.Value);
            if (panelPos.HasValue)
            {
                // Position above the marker pin (offset upward by the pin height)
                float markerOffset = _marker != null ? -45f : 0f;
                _root.style.left = panelPos.Value.x + _offset.x;
                _root.style.top = panelPos.Value.y + _offset.y + markerOffset;
                _root.style.display = DisplayStyle.Flex;
            }
            else
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// Convert LngLat directly to UITK panel coordinates via world space.
        /// </summary>
        private Vector2? ProjectToPanel(LngLat lngLat)
        {
            if (_root?.panel == null || _map?.MapCamera == null) return null;

            var worldPos = _map.LngLatToWorld(lngLat);
            if (!worldPos.HasValue) return null;

            return RuntimePanelUtils.CameraTransformWorldToPanel(
                _root.panel, worldPos.Value, _map.MapCamera);
        }
    }

    /// <summary>
    /// Options for creating a Popup.
    /// </summary>
    public struct PopupOptions
    {
        /// <summary>
        /// Whether to show a close button. Default is true.
        /// </summary>
        public bool? CloseButton;

        /// <summary>
        /// Whether to show a delete button next to the close button.
        /// </summary>
        public bool DeleteButton;

        /// <summary>
        /// Pixel offset [x, y] from the popup's anchor point.
        /// </summary>
        public Vector2 Offset;

        /// <summary>
        /// Maximum width in pixels. Default is 240.
        /// </summary>
        public int MaxWidth;

        /// <summary>
        /// Optional USS class name to add to the popup root element.
        /// </summary>
        public string ClassName;
    }
}
