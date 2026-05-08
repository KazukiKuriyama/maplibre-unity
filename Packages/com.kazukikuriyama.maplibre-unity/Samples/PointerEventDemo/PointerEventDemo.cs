using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates mouse/touch events matching MapLibre GL JS.
    /// Shows a real-time event log on screen using UI Toolkit.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class PointerEventDemo : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;
        [SerializeField] private int _maxLogLines = 80;

        private MapLibreMap _map;
        private Label _logLabel;
        private Label _coordLabel;
        private Label _lastEventLabel;
        private ScrollView _scrollView;
        private Font _font;
        private readonly List<string> _logLines = new();

        // Event type color mapping for visual clarity
        private static readonly Dictionary<MapEventType, string> EventColors = new()
        {
            { MapEventType.Click,        "#4FC3F7" },
            { MapEventType.DblClick,     "#29B6F6" },
            { MapEventType.MouseDown,    "#FFA726" },
            { MapEventType.MouseUp,      "#FFB74D" },
            { MapEventType.MouseMove,    "#A5D6A7" },
            { MapEventType.MouseEnter,   "#81C784" },
            { MapEventType.MouseLeave,   "#E57373" },
            { MapEventType.ContextMenu,  "#CE93D8" },
            { MapEventType.TouchStart,   "#4DD0E1" },
            { MapEventType.TouchEnd,     "#4DB6AC" },
            { MapEventType.TouchMove,    "#AED581" },
            { MapEventType.TouchCancel,  "#FF8A65" },
        };

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[PointerEventDemo] MapLibreMap not found");
                yield break;
            }

            while (!_map.IsInitialized)
                yield return null;

            _font = SystemFontFallback.Resolve("Arial", 16);
            BuildUI();
            RegisterEvents();
        }

        private void RegisterEvents()
        {
            _map.On(MapEventType.Click, OnPointerEvent);
            _map.On(MapEventType.DblClick, OnPointerEvent);
            _map.On(MapEventType.MouseDown, OnPointerEvent);
            _map.On(MapEventType.MouseUp, OnPointerEvent);
            _map.On(MapEventType.MouseMove, OnMouseMove);
            _map.On(MapEventType.MouseEnter, OnPointerEvent);
            _map.On(MapEventType.MouseLeave, OnPointerEvent);
            _map.On(MapEventType.ContextMenu, OnPointerEvent);

            _map.On(MapEventType.TouchStart, OnPointerEvent);
            _map.On(MapEventType.TouchEnd, OnPointerEvent);
            _map.On(MapEventType.TouchMove, OnTouchMove);
            _map.On(MapEventType.TouchCancel, OnPointerEvent);
        }

        private void OnPointerEvent(MapEvent e)
        {
            string color = EventColors.TryGetValue(e.Type, out var c) ? c : "#FFFFFF";
            string lngLatStr = e.LngLat.HasValue
                ? $"({e.LngLat.Value.Longitude:F4}, {e.LngLat.Value.Latitude:F4})"
                : "N/A";
            string pointStr = e.Point.HasValue
                ? $"({e.Point.Value.x:F0}, {e.Point.Value.y:F0})"
                : "N/A";
            string touchStr = e.TouchIndex >= 0 ? $"  touch:{e.TouchIndex}" : "";

            string line = $"<color={color}>{e.Type}</color>  screen:{pointStr}  lngLat:{lngLatStr}{touchStr}";
            AddLog(line);

            if (_lastEventLabel != null)
                _lastEventLabel.text = $"Last: {e.Type}";
        }

        // MouseMove fires every frame -- throttle log to avoid flooding
        private int _moveLogCounter;
        private void OnMouseMove(MapEvent e)
        {
            // Update coordinate display every frame
            if (_coordLabel != null && e.LngLat.HasValue)
            {
                _coordLabel.text = $"Lng: {e.LngLat.Value.Longitude:F5}  Lat: {e.LngLat.Value.Latitude:F5}  Screen: ({e.Point?.x:F0}, {e.Point?.y:F0})";
            }

            // Log every 30th move to keep log readable
            _moveLogCounter++;
            if (_moveLogCounter % 30 == 0)
                OnPointerEvent(e);
        }

        private int _touchMoveLogCounter;
        private void OnTouchMove(MapEvent e)
        {
            if (_coordLabel != null && e.LngLat.HasValue)
            {
                _coordLabel.text = $"Touch {e.TouchIndex}  Lng: {e.LngLat.Value.Longitude:F5}  Lat: {e.LngLat.Value.Latitude:F5}";
            }

            _touchMoveLogCounter++;
            if (_touchMoveLogCounter % 15 == 0)
                OnPointerEvent(e);
        }

        private void AddLog(string line)
        {
            _logLines.Add(line);
            while (_logLines.Count > _maxLogLines)
                _logLines.RemoveAt(0);

            if (_logLabel != null)
            {
                _logLabel.text = string.Join("\n", _logLines);

                // Auto-scroll to bottom
                _scrollView?.schedule.Execute(() =>
                    _scrollView.scrollOffset = new Vector2(0, float.MaxValue));
            }
        }

        private void BuildUI()
        {
            var go = new GameObject("PointerEventDemoUI");
            go.transform.SetParent(transform);

            var uiDoc = go.AddComponent<UIDocument>();
            if (_panelSettings != null)
            {
                uiDoc.panelSettings = _panelSettings;
            }
            else
            {
                // Fallback: find any PanelSettings in the project (including MapLibreMap's)
                var found = Resources.FindObjectsOfTypeAll<PanelSettings>();
                if (found.Length > 0)
                    uiDoc.panelSettings = found[0];
            }
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

            // Panel (right side)
            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.top = new Length(50, LengthUnit.Percent);
            panel.style.translate = new Translate(0, new Length(-50, LengthUnit.Percent));
            panel.style.right = 14;
            // Width: 32% of the viewport, capped at the original 420px on
            // wide displays and floored at 260px on phones / portrait windows.
            panel.style.width = new Length(32, LengthUnit.Percent);
            panel.style.maxWidth = 420;
            panel.style.minWidth = 260;
            panel.style.maxHeight = new Length(85, LengthUnit.Percent);
            panel.style.backgroundColor = new Color(0.06f, 0.06f, 0.10f, 0.92f);
            SetBorderRadius(panel, 10);
            panel.style.paddingTop = 14;
            panel.style.paddingBottom = 14;
            panel.style.paddingLeft = 14;
            panel.style.paddingRight = 14;
            SetBorderWidth(panel, 1);
            SetBorderColor(panel, new Color(1, 1, 1, 0.12f));
            panel.style.overflow = Overflow.Hidden;
            root.Add(panel);

            // Title
            panel.Add(MakeLabel("Pointer / Touch Event Demo", 18, Color.white, FontStyle.Bold));
            panel.Add(MakeLabel("Mouse & touch events matching MapLibre GL JS", 12, new Color(1, 1, 1, 0.5f)));

            // Last event indicator
            _lastEventLabel = MakeLabel("Last: (none)", 14, new Color(1f, 0.85f, 0.4f), FontStyle.Bold);
            _lastEventLabel.style.marginTop = 6;
            panel.Add(_lastEventLabel);

            // Coordinate display
            _coordLabel = MakeLabel("Move mouse over the map...", 13, new Color(0.6f, 0.85f, 1f));
            _coordLabel.style.marginTop = 4;
            panel.Add(_coordLabel);

            // Legend
            panel.Add(MakeSectionLabel("Event Types"));
            var legendContainer = new VisualElement();
            legendContainer.style.flexDirection = FlexDirection.Row;
            legendContainer.style.flexWrap = Wrap.Wrap;
            legendContainer.style.marginBottom = 4;
            foreach (var kvp in EventColors)
            {
                var chip = new Label(kvp.Key.ToString());
                chip.style.fontSize = 10;
                chip.style.color = Color.white;
                if (ColorUtility.TryParseHtmlString(kvp.Value, out var chipColor))
                    chip.style.backgroundColor = new Color(chipColor.r, chipColor.g, chipColor.b, 0.6f);
                SetBorderRadius(chip, 4);
                chip.style.paddingLeft = 5;
                chip.style.paddingRight = 5;
                chip.style.paddingTop = 2;
                chip.style.paddingBottom = 2;
                chip.style.marginRight = 3;
                chip.style.marginBottom = 3;
                chip.style.unityFontDefinition = StyleKeyword.None;
                chip.style.unityFont = new StyleFont(_font);
                chip.pickingMode = PickingMode.Ignore;
                legendContainer.Add(chip);
            }
            panel.Add(legendContainer);

            // Clear button
            var clearBtn = new Button();
            clearBtn.text = "Clear Log";
            clearBtn.style.height = 28;
            clearBtn.style.marginTop = 4;
            clearBtn.style.marginBottom = 4;
            SetBorderRadius(clearBtn, 6);
            clearBtn.style.fontSize = 13;
            clearBtn.style.color = Color.white;
            clearBtn.style.backgroundColor = new Color(0.3f, 0.15f, 0.15f);
            SetBorderWidth(clearBtn, 0);
            clearBtn.style.unityFontDefinition = StyleKeyword.None;
            clearBtn.style.unityFont = new StyleFont(_font);
            clearBtn.clicked += () =>
            {
                _logLines.Clear();
                if (_logLabel != null) _logLabel.text = "";
            };
            panel.Add(clearBtn);

            // Event log section
            panel.Add(MakeSectionLabel("Event Log"));
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            SampleScrollViewStyle.Apply(_scrollView);
            _scrollView.style.flexGrow = 1;
            _scrollView.style.flexShrink = 1;
            _scrollView.style.maxHeight = 400;
            _scrollView.style.backgroundColor = new Color(0, 0, 0, 0.35f);
            SetBorderRadius(_scrollView, 6);
            _scrollView.style.paddingTop = 6;
            _scrollView.style.paddingBottom = 6;
            _scrollView.style.paddingLeft = 8;
            _scrollView.style.paddingRight = 8;
            panel.Add(_scrollView);

            _logLabel = new Label("");
            _logLabel.style.fontSize = 11;
            _logLabel.style.color = new Color(0.9f, 0.95f, 1f, 0.9f);
            _logLabel.style.whiteSpace = WhiteSpace.Normal;
            _logLabel.style.overflow = Overflow.Hidden;
            _logLabel.style.maxWidth = new Length(100, LengthUnit.Percent);
            _logLabel.style.unityFontDefinition = StyleKeyword.None;
            _logLabel.style.unityFont = new StyleFont(_font);
            _logLabel.enableRichText = true;
            _logLabel.pickingMode = PickingMode.Ignore;
            _scrollView.Add(_logLabel);

            AddLog("<color=#888>Event log ready. Interact with the map...</color>");
        }

        private Label MakeLabel(string text, int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            var label = new Label(text);
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = fontStyle;
            label.style.marginBottom = 2;
            label.style.unityFontDefinition = StyleKeyword.None;
            label.style.unityFont = new StyleFont(_font);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private Label MakeSectionLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 12;
            label.style.color = new Color(1, 1, 1, 0.45f);
            label.style.unityFontStyleAndWeight = FontStyle.Italic;
            label.style.marginTop = 6;
            label.style.marginBottom = 3;
            label.style.unityFontDefinition = StyleKeyword.None;
            label.style.unityFont = new StyleFont(_font);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private static void SetBorderRadius(VisualElement el, float r)
        {
            el.style.borderTopLeftRadius = r;
            el.style.borderTopRightRadius = r;
            el.style.borderBottomLeftRadius = r;
            el.style.borderBottomRightRadius = r;
        }

        private static void SetBorderWidth(VisualElement el, float w)
        {
            el.style.borderTopWidth = w;
            el.style.borderBottomWidth = w;
            el.style.borderLeftWidth = w;
            el.style.borderRightWidth = w;
        }

        private static void SetBorderColor(VisualElement el, Color c)
        {
            el.style.borderTopColor = c;
            el.style.borderBottomColor = c;
            el.style.borderLeftColor = c;
            el.style.borderRightColor = c;
        }

        private void OnDestroy()
        {
            if (_map == null) return;
            _map.Off(MapEventType.Click, OnPointerEvent);
            _map.Off(MapEventType.DblClick, OnPointerEvent);
            _map.Off(MapEventType.MouseDown, OnPointerEvent);
            _map.Off(MapEventType.MouseUp, OnPointerEvent);
            _map.Off(MapEventType.MouseMove, OnMouseMove);
            _map.Off(MapEventType.MouseEnter, OnPointerEvent);
            _map.Off(MapEventType.MouseLeave, OnPointerEvent);
            _map.Off(MapEventType.ContextMenu, OnPointerEvent);
            _map.Off(MapEventType.TouchStart, OnPointerEvent);
            _map.Off(MapEventType.TouchEnd, OnPointerEvent);
            _map.Off(MapEventType.TouchMove, OnTouchMove);
            _map.Off(MapEventType.TouchCancel, OnPointerEvent);
        }
    }
}
