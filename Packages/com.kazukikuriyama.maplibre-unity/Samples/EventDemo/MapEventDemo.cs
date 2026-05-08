using System.Collections.Generic;
using MapLibre.Unity.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates the map event system matching MapLibre GL JS on/off/once API.
    /// Events are shown in a floating panel on screen as well as logged to the console.
    /// Attach this MonoBehaviour to the same GameObject as MapLibreMap.
    /// </summary>
    public class MapEventDemo : MonoBehaviour
    {
        private const int MaxEntries = 60;
        private const float PanelWidth = 340f;
        private const float EntryHeight = 32f;
        private const float EntryMargin = 4f;
        private const float HeaderHeight = 34f;
        private const float TopLineHeight = 16f;
        private const float TimeWidth = 70f;
        private const float TypeWidth = 82f;
        // Panel(340) - borders(2) - scroll padding(12) - scrollbar(14) - details indent(70) = 242
        private const float DetailsWidth = 242f;

        private MapLibreMap _map;
        private UIDocument _uiDoc;
        private ScrollView _scroll;
        private Font _font;
        private readonly List<VisualElement> _entries = new();

        private void Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[MapEventDemo] MapLibreMap component not found on this GameObject");
                return;
            }

            _font = SystemFontFallback.Resolve("Arial", 12);
            BuildOverlay();

            _map.On(MapEventType.Load, OnLoad);
            _map.Once(MapEventType.Idle, e => AddEntry("idle", "map ready", IdleColor));
            _map.On(MapEventType.Click, OnClick);

            _map.On(MapEventType.MoveStart, e => AddEntry("movestart", null, MoveColor));
            _map.On(MapEventType.MoveEnd, e => AddEntry("moveend",
                $"center: {_map.State.Center}  zoom: {_map.State.Zoom:F2}", MoveColor));
            _map.On(MapEventType.ZoomStart, e => AddEntry("zoomstart", null, ZoomColor));
            _map.On(MapEventType.ZoomEnd, e => AddEntry("zoomend",
                $"zoom: {_map.State.Zoom:F2}", ZoomColor));
            _map.On(MapEventType.RotateStart, e => AddEntry("rotatestart", null, RotateColor));
            _map.On(MapEventType.RotateEnd, e => AddEntry("rotateend",
                $"bearing: {_map.State.Bearing:F1}", RotateColor));
            _map.On(MapEventType.PitchStart, e => AddEntry("pitchstart", null, PitchColor));
            _map.On(MapEventType.PitchEnd, e => AddEntry("pitchend",
                $"pitch: {_map.State.Pitch:F1}", PitchColor));
            _map.On(MapEventType.SourceAdd, e => AddEntry("sourceadd", e.Id, DataColor));
            _map.On(MapEventType.LayerAdd, e => AddEntry("layeradd", e.Id, DataColor));
            _map.On(MapEventType.LayerRemove, e => AddEntry("layerremove", e.Id, DataColor));
        }

        private void OnLoad(MapEvent e)
        {
            AddEntry("load", $"center: {_map.State.Center}  zoom: {_map.State.Zoom:F2}", LoadColor);
        }

        private void OnClick(MapEvent e)
        {
            AddEntry("click", $"lngLat: {e.LngLat}  screen: {e.Point}", ClickColor);
        }

        private void OnDestroy()
        {
            if (_map == null) return;
            _map.Off(MapEventType.Load, OnLoad);
            _map.Off(MapEventType.Click, OnClick);
        }

        private static readonly Color LoadColor = new(0.4f, 0.85f, 0.5f, 1f);
        private static readonly Color IdleColor = new(0.7f, 0.7f, 0.7f, 1f);
        private static readonly Color ClickColor = new(1f, 0.8f, 0.3f, 1f);
        private static readonly Color MoveColor = new(0.55f, 0.78f, 1f, 1f);
        private static readonly Color ZoomColor = new(0.55f, 0.78f, 1f, 1f);
        private static readonly Color RotateColor = new(0.85f, 0.55f, 1f, 1f);
        private static readonly Color PitchColor = new(0.85f, 0.55f, 1f, 1f);
        private static readonly Color DataColor = new(1f, 0.65f, 0.4f, 1f);

        private void BuildOverlay()
        {
            var overlayGo = new GameObject("__EventLogOverlay");
            overlayGo.transform.SetParent(null, false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(overlayGo, gameObject.scene);

            _uiDoc = overlayGo.AddComponent<UIDocument>();
            var fallback = Resources.FindObjectsOfTypeAll<PanelSettings>();
            if (fallback.Length > 0) _uiDoc.panelSettings = fallback[0];
            _uiDoc.sortingOrder = 400;

            var root = _uiDoc.rootVisualElement;
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;

            // Panel width: 28% of the viewport, clamped to keep the body
            // legible on 4K monitors (PanelWidth = original 340px) and
            // usable on portrait phones (~220px floor).
            var panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = new Length(50, LengthUnit.Percent),
                    translate = new Translate(0, new Length(-50, LengthUnit.Percent)),
                    right = 10,
                    width = new Length(28, LengthUnit.Percent),
                    maxWidth = PanelWidth,
                    minWidth = 220,
                    maxHeight = new Length(80, LengthUnit.Percent),
                    backgroundColor = new Color(0.06f, 0.08f, 0.12f, 0.9f),
                    borderTopLeftRadius = 6,
                    borderTopRightRadius = 6,
                    borderBottomLeftRadius = 6,
                    borderBottomRightRadius = 6,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.15f),
                    borderBottomColor = new Color(1, 1, 1, 0.15f),
                    borderLeftColor = new Color(1, 1, 1, 0.15f),
                    borderRightColor = new Color(1, 1, 1, 0.15f),
                }
            };
            root.Add(panel);

            var header = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = HeaderHeight,
                    flexShrink = 0,
                    paddingLeft = 10,
                    paddingRight = 6,
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(1, 1, 1, 0.1f),
                }
            };
            panel.Add(header);

            var headerLabel = new Label("Events")
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    flexGrow = 1,
                    fontSize = 13,
                    minHeight = 20,
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            header.Add(headerLabel);

            var clearBtn = new Button(Clear)
            {
                text = "Clear",
                style =
                {
                    height = 22,
                    width = 56,
                    paddingTop = 0,
                    paddingBottom = 0,
                    paddingLeft = 0,
                    paddingRight = 0,
                    marginTop = 0,
                    marginBottom = 0,
                    marginLeft = 0,
                    marginRight = 0,
                    fontSize = 11,
                    color = Color.white,
                    backgroundColor = new Color(0.22f, 0.5f, 0.85f, 1f),
                    unityTextAlign = TextAnchor.MiddleCenter,
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4,
                    borderTopWidth = 0,
                    borderBottomWidth = 0,
                    borderLeftWidth = 0,
                    borderRightWidth = 0,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            header.Add(clearBtn);

            _scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
                    paddingTop = 4,
                    paddingBottom = 4,
                    paddingLeft = 6,
                    paddingRight = 6,
                }
            };
            panel.Add(_scroll);
            SampleScrollViewStyle.Apply(_scroll);

            _scroll.RegisterCallback<WheelEvent>(evt =>
            {
                _scroll.verticalScroller.value += evt.delta.y * 20f;
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            _scroll.contentContainer.style.flexGrow = 0;
            _scroll.contentContainer.style.flexShrink = 0;
            _scroll.contentContainer.style.width = Length.Percent(100);
            _scroll.style.overflow = Overflow.Hidden;
        }

        private void AddEntry(string type, string details, Color typeColor)
        {
            Debug.Log(string.IsNullOrEmpty(details)
                ? $"[MapEventDemo] {type}"
                : $"[MapEventDemo] {type} -- {details}");

            if (_scroll == null) return;

            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexShrink = 0,
                    width = Length.Percent(100),
                    height = EntryHeight,
                    overflow = Overflow.Hidden,
                    marginBottom = EntryMargin,
                }
            };

            var topLine = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = TopLineHeight,
                    flexShrink = 0,
                }
            };
            row.Add(topLine);

            var time = System.DateTime.Now.ToString("HH:mm:ss.fff");
            var timeLabel = new Label(time)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    width = TimeWidth,
                    flexShrink = 0,
                    fontSize = 10,
                    minHeight = 14,
                    color = new Color(1, 1, 1, 0.45f),
                    whiteSpace = WhiteSpace.NoWrap,
                    overflow = Overflow.Hidden,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            topLine.Add(timeLabel);

            var typeLabel = new Label(type)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    width = TypeWidth,
                    flexShrink = 0,
                    fontSize = 11,
                    minHeight = 16,
                    color = typeColor,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    whiteSpace = WhiteSpace.NoWrap,
                    overflow = Overflow.Hidden,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            topLine.Add(typeLabel);

            var detailsText = string.IsNullOrEmpty(details) ? string.Empty : details;
            var detailsLabel = new Label(detailsText)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    width = DetailsWidth,
                    flexShrink = 0,
                    fontSize = 10,
                    minHeight = 14,
                    marginLeft = TimeWidth,
                    color = new Color(1, 1, 1, 0.8f),
                    whiteSpace = WhiteSpace.NoWrap,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            row.Add(detailsLabel);

            _scroll.Add(row);
            _entries.Add(row);

            while (_entries.Count > MaxEntries)
            {
                var old = _entries[0];
                _entries.RemoveAt(0);
                old.RemoveFromHierarchy();
            }

            UpdateContentHeight();

            // Auto-scroll to the newest entry (at the bottom of the list).
            _scroll.schedule.Execute(() =>
            {
                _scroll.verticalScroller.value = _scroll.verticalScroller.highValue;
            }).StartingIn(0);
        }

        private void Clear()
        {
            foreach (var e in _entries) e.RemoveFromHierarchy();
            _entries.Clear();
            UpdateContentHeight();
        }

        private void UpdateContentHeight()
        {
            if (_scroll == null) return;
            _scroll.contentContainer.style.height = _entries.Count * (EntryHeight + EntryMargin);
        }
    }
}
