using MapLibre.Unity.CameraControl;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MapLibre.Unity
{
    /// <summary>
    /// Per-frame input dispatch (mouse, touch, resize) and the box-zoom callbacks
    /// hooked to <see cref="MapLibre.Unity.CameraControl.MapInputHandler"/>.
    /// All firing routes through the Fire* helpers in MapLibreMap.Events.cs.
    /// </summary>
    public partial class MapLibreMap
    {
        /// <summary>
        /// Returns true if the given screen position is over any UI Toolkit
        /// overlay (marker/popup, debug HUD, sample panel, etc.). Used to
        /// suppress map drag/zoom/click when the pointer is over a UI element.
        /// Picks across every <see cref="UIDocument"/> in the scene so that
        /// independently-authored overlays (e.g. demo panels) automatically
        /// block input without needing to register with MapLibreMap.
        /// </summary>
        public bool IsPointerOverOverlayUI(Vector2 screenPos)
        {
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            if (docs == null || docs.Length == 0) return false;

            // ScreenToPanel expects OS screen coordinates (origin top-left)
            var flipped = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (var doc in docs)
            {
                var root = doc != null ? doc.rootVisualElement : null;
                if (root?.panel == null) continue;
                var panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, flipped);
                var picked = root.panel.Pick(panelPos);
                if (picked != null && picked != root) return true;
            }
            return false;
        }

        /// <summary>
        /// Detect viewport size changes and fire Resize events. Matches map.on('resize') in MapLibre GL JS.
        /// </summary>
        private void HandleResize()
        {
            int w = Screen.width;
            int h = Screen.height;
            if (_lastScreenWidth == 0 && _lastScreenHeight == 0)
            {
                _lastScreenWidth = w;
                _lastScreenHeight = h;
                return;
            }
            if (w != _lastScreenWidth || h != _lastScreenHeight)
            {
                _lastScreenWidth = w;
                _lastScreenHeight = h;
                FireEvent(new MapEvent(MapEventType.Resize, this) { Size = new Vector2(w, h) });
            }
        }

        /// <summary>
        /// Fire start/continuous/end events based on state changes this frame.
        /// Matches MapLibre GL JS movestart → move → moveend lifecycle.
        /// </summary>
        private void ProcessEvents()
        {
            if (_stateChangedThisFrame)
            {
                _idleFrameCount = 0;

                // Move (any state change)
                if (!_isMoving)
                {
                    _isMoving = true;
                    FireEvent(MapEventType.MoveStart);
                }
                FireEvent(MapEventType.Move);

                // Zoom
                if ((_frameChangeFlags & StateChangeFlags.Zoom) != 0)
                {
                    if (!_isZooming)
                    {
                        _isZooming = true;
                        FireEvent(MapEventType.ZoomStart);
                    }
                    FireEvent(MapEventType.Zoom);
                }

                // Rotate (bearing)
                if ((_frameChangeFlags & StateChangeFlags.Bearing) != 0)
                {
                    if (!_isRotating)
                    {
                        _isRotating = true;
                        FireEvent(MapEventType.RotateStart);
                    }
                    FireEvent(MapEventType.Rotate);
                }

                // Pitch
                if ((_frameChangeFlags & StateChangeFlags.Pitch) != 0)
                {
                    if (!_isPitching)
                    {
                        _isPitching = true;
                        FireEvent(MapEventType.PitchStart);
                    }
                    FireEvent(MapEventType.Pitch);
                }

                _stateChangedThisFrame = false;
                _frameChangeFlags = StateChangeFlags.None;
            }
            else if (_isMoving)
            {
                // Wait a couple of frames with no changes before firing end events,
                // to avoid spurious ends during smooth zoom interpolation.
                _idleFrameCount++;
                if (_idleFrameCount >= IdleFrameThreshold)
                {
                    if (_isPitching)
                    {
                        _isPitching = false;
                        FireEvent(MapEventType.PitchEnd);
                    }
                    if (_isRotating)
                    {
                        _isRotating = false;
                        FireEvent(MapEventType.RotateEnd);
                    }
                    if (_isZooming)
                    {
                        _isZooming = false;
                        FireEvent(MapEventType.ZoomEnd);
                    }
                    _isMoving = false;
                    FireEvent(MapEventType.MoveEnd);
                    FireEvent(MapEventType.Idle);
                }
            }
        }

        /// <summary>
        /// Handle all mouse/pointer events: mousedown, mouseup, mousemove,
        /// click, dblclick, contextmenu, mouseenter, mouseleave.
        /// </summary>
        private void HandlePointerEvents()
        {
            var mouse = Mouse.current;
            if (mouse == null || _mapCamera == null) return;

            Vector2 pos = mouse.position.ReadValue();
            bool overUI = IsPointerOverOverlayUI(pos);

            // --- mouseenter / mouseleave ---
            bool insideScreen = pos.x >= 0 && pos.x <= Screen.width &&
                                pos.y >= 0 && pos.y <= Screen.height;
            if (insideScreen && !_isMouseOverMap)
            {
                _isMouseOverMap = true;
                FireEvent(MakePointerEvent(MapEventType.MouseEnter, pos));
            }
            else if (!insideScreen && _isMouseOverMap)
            {
                _isMouseOverMap = false;
                FireEvent(MakePointerEvent(MapEventType.MouseLeave, pos));
            }

            // --- mousemove (every frame the mouse moves) ---
            if (mouse.delta.ReadValue().sqrMagnitude > 0.01f && _isMouseOverMap)
            {
                FireEvent(MakePointerEvent(MapEventType.MouseMove, pos));
            }

            // --- wheel (raw scroll input, distinct from zoom derived from it) ---
            Vector2 scrollDelta = mouse.scroll.ReadValue();
            if (scrollDelta.sqrMagnitude > 0.01f && _isMouseOverMap && !overUI)
            {
                FireEvent(new MapEvent(MapEventType.Wheel, this)
                {
                    Point = pos,
                    LngLat = Unproject(pos),
                    WheelDelta = scrollDelta,
                });
            }

            // --- drag tracking: dragstart once the pointer moves past threshold while held ---
            if (_mouseDownTracking && !_isDragging &&
                Vector2.Distance(_mouseDownPos, pos) > DragStartThreshold)
            {
                _isDragging = true;
                if (!overUI)
                    FireEvent(MakePointerEvent(MapEventType.DragStart, _mouseDownPos));
            }
            if (_isDragging && mouse.delta.ReadValue().sqrMagnitude > 0.01f)
            {
                FireEvent(MakePointerEvent(MapEventType.Drag, pos));
            }

            // --- mousedown ---
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _mouseDownPos = pos;
                _mouseDownTracking = true;
                if (!overUI)
                    FireEvent(MakePointerEvent(MapEventType.MouseDown, pos));
            }
            if (mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
            {
                if (!overUI)
                    FireEvent(MakePointerEvent(MapEventType.MouseDown, pos));
            }

            // --- mouseup ---
            if (mouse.leftButton.wasReleasedThisFrame ||
                mouse.rightButton.wasReleasedThisFrame ||
                mouse.middleButton.wasReleasedThisFrame)
            {
                if (!overUI)
                    FireEvent(MakePointerEvent(MapEventType.MouseUp, pos));
            }

            // --- dragend on left button release after dragstart fired ---
            if (mouse.leftButton.wasReleasedThisFrame && _isDragging)
            {
                FireEvent(MakePointerEvent(MapEventType.DragEnd, pos));
                _isDragging = false;
            }

            // --- click / dblclick (left button release, not a drag) ---
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                Vector2 releasePos = pos;

                if (_mouseDownTracking && Vector2.Distance(_mouseDownPos, releasePos) <= ClickMaxDistance)
                {
                    Vector2 clickPos = _mouseDownPos;
                    if (!overUI && !PrefabConsumedClickThisFrame)
                    {
                        FireEvent(MakePointerEvent(MapEventType.Click, clickPos));

                        // Double-click detection
                        float now = Time.unscaledTime;
                        if (now - _lastClickTime < DblClickMaxInterval &&
                            Vector2.Distance(clickPos, _lastClickPos) <= ClickMaxDistance)
                        {
                            FireEvent(MakePointerEvent(MapEventType.DblClick, clickPos));
                            _lastClickTime = 0f; // reset to avoid triple-click = 2x dblclick
                        }
                        else
                        {
                            _lastClickTime = now;
                            _lastClickPos = clickPos;
                        }
                    }
                }

                _mouseDownTracking = false;
            }

            // --- contextmenu (right-click release, not a drag) ---
            if (mouse.rightButton.wasReleasedThisFrame && !overUI)
            {
                FireEvent(MakePointerEvent(MapEventType.ContextMenu, pos));
            }
        }

        /// <summary>
        /// Handle touch events: touchstart, touchend, touchmove, touchcancel.
        /// </summary>
        private void HandleTouchEvents()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null || _mapCamera == null) return;

            foreach (var touch in touchscreen.touches)
            {
                var phase = touch.phase.ReadValue();
                int id = touch.touchId.ReadValue();
                Vector2 pos = touch.position.ReadValue();

                switch (phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        _activeTouches[id] = pos;
                        FireEvent(new MapEvent(MapEventType.TouchStart, this)
                        {
                            Point = pos,
                            LngLat = Unproject(pos),
                            TouchIndex = id,
                        });
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        if (_activeTouches.ContainsKey(id) &&
                            phase == UnityEngine.InputSystem.TouchPhase.Moved)
                        {
                            _activeTouches[id] = pos;
                            FireEvent(new MapEvent(MapEventType.TouchMove, this)
                            {
                                Point = pos,
                                LngLat = Unproject(pos),
                                TouchIndex = id,
                            });
                        }
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Ended:
                        _activeTouches.Remove(id);
                        FireEvent(new MapEvent(MapEventType.TouchEnd, this)
                        {
                            Point = pos,
                            LngLat = Unproject(pos),
                            TouchIndex = id,
                        });
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        _activeTouches.Remove(id);
                        FireEvent(new MapEvent(MapEventType.TouchCancel, this)
                        {
                            Point = pos,
                            LngLat = Unproject(pos),
                            TouchIndex = id,
                        });
                        break;
                }
            }
        }

        // === Box-zoom handlers ===

        private void OnBoxZoomStart(Vector2 startPoint)
        {
            _controlsOverlay?.ShowBoxZoom(startPoint, startPoint);
            FireEvent(MakePointerEvent(MapEventType.BoxZoomStart, startPoint));
        }

        private void OnBoxZoomDrag(Vector2 startPoint, Vector2 currentPoint)
        {
            _controlsOverlay?.ShowBoxZoom(startPoint, currentPoint);
        }

        private void OnBoxZoomEnd(Vector2 startPoint, Vector2 endPoint, bool committed)
        {
            _controlsOverlay?.HideBoxZoom();

            if (!committed)
            {
                FireEvent(MakePointerEvent(MapEventType.BoxZoomCancel, endPoint));
                return;
            }

            var c0 = Unproject(startPoint);
            var c1 = Unproject(endPoint);
            if (!c0.HasValue || !c1.HasValue)
            {
                FireEvent(MakePointerEvent(MapEventType.BoxZoomCancel, endPoint));
                return;
            }

            FireEvent(MakePointerEvent(MapEventType.BoxZoomEnd, endPoint));
            FitBounds(LngLatBounds.FromCorners(c0.Value, c1.Value),
                new FitBoundsOptions { Duration = 500f });
        }
    }
}
