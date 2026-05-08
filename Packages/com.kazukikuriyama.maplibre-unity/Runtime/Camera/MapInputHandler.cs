using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MapLibre.Unity.CameraControl
{
    /// <summary>
    /// Handles user input for map navigation -- matches MapLibre GL JS default gestures.
    /// Mouse: left-drag = pan, scroll = zoom,
    ///   right-drag = bearing (horizontal) + pitch (vertical),
    ///   double-click = zoom in, shift+double-click = zoom out.
    /// Touch: 1-finger drag = pan, 2-finger pinch = zoom + rotate,
    ///   2-finger same-direction vertical drag = pitch.
    /// Keyboard: arrows = pan, +/- = zoom, shift+arrows = bearing/pitch.
    /// Each gesture can be disabled individually via the Interactions toggles,
    /// mirroring MapLibre GL JS's per-handler enable/disable API.
    /// </summary>
    public class MapInputHandler : MonoBehaviour
    {
        [Header("Interactions (matches MapLibre GL JS handlers)")]
        [SerializeField] private bool _dragPan = true;
        [SerializeField] private bool _scrollZoom = true;
        [Tooltip("Right-drag rotates (horizontal) and pitches (vertical). MapLibre GL JS default.")]
        [SerializeField] private bool _dragRotate = true;
        [SerializeField] private bool _doubleClickZoom = true;
        [SerializeField] private bool _keyboard = true;
        [SerializeField] private bool _touchZoomRotate = true;
        [SerializeField] private bool _touchPitch = true;
        [Tooltip("Shift + left-drag draws a rectangle and zooms to fit it on release.")]
        [SerializeField] private bool _boxZoom = true;

        [Header("Sensitivity")]
        [SerializeField] private float _zoomSpeed = 3.0f;
        [SerializeField] private float _rotateSpeed = 0.3f;
        [SerializeField] private float _pitchSpeed = 0.3f;
        [SerializeField] private float _zoomSmoothTime = 0.15f;

        [Header("Keyboard steps")]
        [Tooltip("Pixels panned per arrow-key press.")]
        [SerializeField] private float _keyboardPanStepPixels = 100f;
        [Tooltip("Zoom change per +/- key press.")]
        [SerializeField] private float _keyboardZoomStep = 1f;
        [Tooltip("Bearing change per shift+left/right press, in degrees.")]
        [SerializeField] private float _keyboardBearingStep = 15f;
        [Tooltip("Pitch change per shift+up/down press, in degrees.")]
        [SerializeField] private float _keyboardPitchStep = 10f;
        [Tooltip("Animation duration for keyboard / double-click actions, in milliseconds.")]
        [SerializeField] private float _keyboardAnimationDuration = 300f;

        [Header("Legacy")]
        [Tooltip("Middle-mouse drag pitches the camera. Off by default; right-drag already covers pitch.")]
        [SerializeField] private bool _middleButtonPitch = false;

        /// <summary>Fired when the user starts interacting. Used to cancel in-progress animations.</summary>
        public event Action OnUserInteraction;

        // Runtime getters/setters for programmatic control (matches MapLibre GL JS enable()/disable()).
        public bool DragPan { get => _dragPan; set => _dragPan = value; }
        public bool ScrollZoom { get => _scrollZoom; set => _scrollZoom = value; }
        public bool DragRotate { get => _dragRotate; set => _dragRotate = value; }
        public bool DoubleClickZoom { get => _doubleClickZoom; set => _doubleClickZoom = value; }
        public bool Keyboard { get => _keyboard; set => _keyboard = value; }
        public bool TouchZoomRotate { get => _touchZoomRotate; set => _touchZoomRotate = value; }
        public bool TouchPitch { get => _touchPitch; set => _touchPitch = value; }
        public bool BoxZoom { get => _boxZoom; set => _boxZoom = value; }

        /// <summary>
        /// Fired when shift + left mouse button starts a box-zoom gesture.
        /// Argument: starting screen point.
        /// </summary>
        public event Action<Vector2> OnBoxZoomStart;

        /// <summary>
        /// Fired every frame the box is being dragged.
        /// Arguments: starting screen point, current screen point.
        /// </summary>
        public event Action<Vector2, Vector2> OnBoxZoomDrag;

        /// <summary>
        /// Fired when the box-zoom gesture ends. Bool indicates whether the box was
        /// large enough to commit (true) or cancelled (false, e.g. released without drag).
        /// </summary>
        public event Action<Vector2, Vector2, bool> OnBoxZoomEnd;

        private MapState _mapState;
        private UnityEngine.Camera _camera;
        private MapAnimator _animator;
        private bool _animatorResolved;

        private Func<Vector2, bool> _isPointerOverUI;

        // Mouse state
        private bool _isPanning;
        private bool _isRotating;
        private bool _isMiddlePitching;
        private bool _suppressPanUntilRelease;
        private Vector2 _lastMousePosition;

        // Box zoom state
        private bool _boxZoomTracking;
        private bool _boxZoomActive;
        private Vector2 _boxZoomStart;
        private Vector2 _boxZoomCurrent;
        private const float BoxZoomMinDragPixels = 5f;

        // Smooth zoom
        private float _targetZoom;
        private float _zoomVelocity;

        // Double-click detection (independent of MapLibreMap's event-level detection).
        private const float DblClickMaxInterval = 0.3f;
        private const float DblClickMaxDistance = 5f;
        private float _lastMouseDownTime;
        private Vector2 _lastMouseDownPos;
        private bool _firstClickPending;

        // Touch state
        private int _activeTouchCount;
        private Vector2 _touch0Pos;
        private Vector2 _touch1Pos;
        private Vector2 _touch0Start;
        private Vector2 _touch1Start;
        private float _touchInitialDistance;
        private float _touchInitialAngleDeg;
        private float _touchInitialZoom;
        private float _touchInitialBearing;
        private float _touchInitialPitch;
        private MercatorCoordinate _touchInitialAnchorMerc;
        private bool _touchInitialAnchorValid;
        private bool _touchPitchEngaged;

        public void Initialize(MapState mapState, UnityEngine.Camera camera)
        {
            _mapState = mapState;
            _camera = camera;
            _targetZoom = mapState.Zoom;
        }

        /// <summary>
        /// Re-sync the smooth-damped zoom target with the current map state.
        /// Call this when MapState.Zoom is changed by external code
        /// (SetZoom / SetStyle / camera teleport) so ApplySmoothZoom doesn't
        /// quietly damp the new value back toward the previously-cached
        /// target. User input (scroll, pinch, double-click) updates
        /// <c>_targetZoom</c> on its own and never needs this hook.
        /// </summary>
        public void SyncTargetFromMapState()
        {
            if (_mapState == null) return;
            _targetZoom = _mapState.Zoom;
            _zoomVelocity = 0f;
        }

        /// <summary>
        /// Set a callback that returns true when the pointer is over a UI element
        /// (marker, popup, etc.) and map input should be suppressed.
        /// </summary>
        public void SetPointerOverUICheck(Func<Vector2, bool> check)
        {
            _isPointerOverUI = check;
        }

        private void Update()
        {
            if (_mapState == null) return;

            // Lazy-resolve animator (may be added after Initialize)
            if (!_animatorResolved)
            {
                _animator = GetComponent<MapAnimator>();
                if (_animator != null) _animatorResolved = true;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                HandleDoubleClick(mouse);
                HandleBoxZoom(mouse);
                HandlePan(mouse);
                HandleZoom(mouse);
                HandleRotateAndPitch(mouse);
                HandleMiddlePitch(mouse);
            }

            if (_keyboard)
                HandleKeyboard(UnityEngine.InputSystem.Keyboard.current);

            HandleTouch();

            ApplySmoothZoom();
        }

        // -------------------- Mouse --------------------

        private void HandlePan(Mouse mouse)
        {
            if (!_dragPan)
            {
                _isPanning = false;
                return;
            }

            // Box-zoom owns shift+left-drag, so let it suppress pan while active.
            if (_boxZoomTracking || _boxZoomActive)
            {
                _isPanning = false;
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !_isRotating && !_isMiddlePitching
                && !_suppressPanUntilRelease)
            {
                if (_isPointerOverUI != null && _isPointerOverUI(mouse.position.ReadValue()))
                    return;

                _isPanning = true;
                _lastMousePosition = mouse.position.ReadValue();
                OnUserInteraction?.Invoke();
            }

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _isPanning = false;
                _suppressPanUntilRelease = false;
            }

            if (!_isPanning) return;

            Vector2 currentPos = mouse.position.ReadValue();
            Vector2 delta = currentPos - _lastMousePosition;
            _lastMousePosition = currentPos;

            PanByScreenDelta(delta);
        }

        private void HandleZoom(Mouse mouse)
        {
            if (!_scrollZoom) return;

            float scrollDelta = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                if (_isPointerOverUI != null && _isPointerOverUI(mouse.position.ReadValue()))
                    return;

                OnUserInteraction?.Invoke();
                _targetZoom = Mathf.Clamp(
                    _targetZoom + scrollDelta * _zoomSpeed * 0.01f,
                    MapConstants.MinZoom,
                    MapConstants.MaxZoom
                );
            }
        }

        private void HandleRotateAndPitch(Mouse mouse)
        {
            if (!_dragRotate)
            {
                _isRotating = false;
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                _isRotating = true;
                _lastMousePosition = mouse.position.ReadValue();
                OnUserInteraction?.Invoke();
            }
            if (mouse.rightButton.wasReleasedThisFrame)
                _isRotating = false;

            if (!_isRotating) return;

            Vector2 currentPos = mouse.position.ReadValue();
            Vector2 delta = currentPos - _lastMousePosition;
            _lastMousePosition = currentPos;

            // Horizontal -> bearing, vertical -> pitch (MapLibre GL JS default).
            _mapState.Bearing += delta.x * _rotateSpeed;
            _mapState.Pitch -= delta.y * _pitchSpeed;
        }

        private void HandleMiddlePitch(Mouse mouse)
        {
            if (!_middleButtonPitch)
            {
                _isMiddlePitching = false;
                return;
            }

            if (mouse.middleButton.wasPressedThisFrame)
            {
                _isMiddlePitching = true;
                _lastMousePosition = mouse.position.ReadValue();
                OnUserInteraction?.Invoke();
            }
            if (mouse.middleButton.wasReleasedThisFrame)
                _isMiddlePitching = false;

            if (!_isMiddlePitching) return;

            Vector2 currentPos = mouse.position.ReadValue();
            float deltaY = currentPos.y - _lastMousePosition.y;
            _lastMousePosition = currentPos;

            _mapState.Pitch -= deltaY * _pitchSpeed;
        }

        // -------------------- Box zoom (shift + left drag) --------------------

        private void HandleBoxZoom(Mouse mouse)
        {
            if (!_boxZoom)
            {
                if (_boxZoomTracking || _boxZoomActive)
                    EmitBoxZoomEnd(false);
                return;
            }

            // Cancel via Esc while box-zoom is active
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (_boxZoomActive && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                EmitBoxZoomEnd(false);
                _suppressPanUntilRelease = true;
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && IsShiftHeld())
            {
                if (_isPointerOverUI != null && _isPointerOverUI(mouse.position.ReadValue()))
                    return;
                _boxZoomTracking = true;
                _boxZoomActive = false;
                _boxZoomStart = mouse.position.ReadValue();
                _boxZoomCurrent = _boxZoomStart;
                _suppressPanUntilRelease = true;
                OnUserInteraction?.Invoke();
            }

            if (!_boxZoomTracking) return;

            _boxZoomCurrent = mouse.position.ReadValue();

            if (!_boxZoomActive &&
                Vector2.Distance(_boxZoomStart, _boxZoomCurrent) > BoxZoomMinDragPixels)
            {
                _boxZoomActive = true;
                OnBoxZoomStart?.Invoke(_boxZoomStart);
            }

            if (_boxZoomActive)
                OnBoxZoomDrag?.Invoke(_boxZoomStart, _boxZoomCurrent);

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                bool committed = _boxZoomActive;
                EmitBoxZoomEnd(committed);
            }
        }

        private void EmitBoxZoomEnd(bool committed)
        {
            bool wasTracking = _boxZoomTracking || _boxZoomActive;
            _boxZoomTracking = false;
            _boxZoomActive = false;
            if (wasTracking)
                OnBoxZoomEnd?.Invoke(_boxZoomStart, _boxZoomCurrent, committed);
        }

        private void HandleDoubleClick(Mouse mouse)
        {
            if (!_doubleClickZoom) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;

            Vector2 pos = mouse.position.ReadValue();
            if (_isPointerOverUI != null && _isPointerOverUI(pos))
            {
                _firstClickPending = false;
                return;
            }

            float now = Time.unscaledTime;
            if (_firstClickPending &&
                now - _lastMouseDownTime < DblClickMaxInterval &&
                Vector2.Distance(pos, _lastMouseDownPos) <= DblClickMaxDistance)
            {
                bool shift = IsShiftHeld();
                float delta = shift ? -1f : 1f;
                float targetZoom = Mathf.Clamp(
                    _mapState.Zoom + delta, MapConstants.MinZoom, MapConstants.MaxZoom);
                ZoomAround(pos, targetZoom, _keyboardAnimationDuration);

                _firstClickPending = false;
                _isPanning = false;           // abort any pan that started this frame
                _suppressPanUntilRelease = true; // block pan until button released
            }
            else
            {
                _firstClickPending = true;
                _lastMouseDownTime = now;
                _lastMouseDownPos = pos;
            }
        }

        // -------------------- Keyboard --------------------

        private void HandleKeyboard(UnityEngine.InputSystem.Keyboard keyboard)
        {
            if (keyboard == null) return;

            bool shift = keyboard.shiftKey.isPressed;
            Vector2 panPixels = Vector2.zero;
            float bearingDelta = 0f;
            float pitchDelta = 0f;
            float zoomDelta = 0f;

            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                if (shift) bearingDelta -= _keyboardBearingStep;
                else panPixels.x -= _keyboardPanStepPixels;
            }
            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                if (shift) bearingDelta += _keyboardBearingStep;
                else panPixels.x += _keyboardPanStepPixels;
            }
            if (keyboard.upArrowKey.wasPressedThisFrame)
            {
                if (shift) pitchDelta += _keyboardPitchStep;
                else panPixels.y += _keyboardPanStepPixels;
            }
            if (keyboard.downArrowKey.wasPressedThisFrame)
            {
                if (shift) pitchDelta -= _keyboardPitchStep;
                else panPixels.y -= _keyboardPanStepPixels;
            }
            if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
                zoomDelta += _keyboardZoomStep * (shift ? 2f : 1f);
            if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
                zoomDelta -= _keyboardZoomStep * (shift ? 2f : 1f);

            if (panPixels == Vector2.zero && bearingDelta == 0f && pitchDelta == 0f && zoomDelta == 0f)
                return;

            OnUserInteraction?.Invoke();

            LngLat? targetCenter = null;
            if (panPixels != Vector2.zero && _camera != null)
            {
                var screenCenter = new Vector2(_camera.pixelWidth * 0.5f, _camera.pixelHeight * 0.5f);
                if (ScreenToLngLat(screenCenter + panPixels, out var pannedCenter))
                    targetCenter = pannedCenter;
            }

            float targetZoom = Mathf.Clamp(
                _mapState.Zoom + zoomDelta, MapConstants.MinZoom, MapConstants.MaxZoom);
            float targetBearing = _mapState.Bearing + bearingDelta;
            float targetPitch = Mathf.Clamp(_mapState.Pitch + pitchDelta, 0f, MapState.MaxPitchHardLimit);

            if (_animator != null)
            {
                _animator.EaseTo(new EaseToOptions
                {
                    Center = targetCenter,
                    Zoom = zoomDelta != 0f ? targetZoom : null,
                    Bearing = bearingDelta != 0f ? (float?)targetBearing : null,
                    Pitch = pitchDelta != 0f ? (float?)targetPitch : null,
                    Duration = _keyboardAnimationDuration,
                    Easing = MapAnimator.EasingFunction.EaseInOut,
                });
                _targetZoom = targetZoom;
            }
            else
            {
                if (targetCenter.HasValue) _mapState.Center = targetCenter.Value;
                if (zoomDelta != 0f) _mapState.Zoom = targetZoom;
                if (bearingDelta != 0f) _mapState.Bearing = targetBearing;
                if (pitchDelta != 0f) _mapState.Pitch = targetPitch;
                _targetZoom = _mapState.Zoom;
            }
        }

        // -------------------- Touch --------------------

        private void HandleTouch()
        {
            var ts = Touchscreen.current;
            if (ts == null)
            {
                _activeTouchCount = 0;
                return;
            }

            // Collect up to 2 active touches
            Vector2 p0 = default, p1 = default;
            int count = 0;
            foreach (var touch in ts.touches)
            {
                var phase = touch.phase.ReadValue();
                if (phase != UnityEngine.InputSystem.TouchPhase.Began &&
                    phase != UnityEngine.InputSystem.TouchPhase.Moved &&
                    phase != UnityEngine.InputSystem.TouchPhase.Stationary)
                    continue;

                Vector2 pos = touch.position.ReadValue();
                if (count == 0) p0 = pos;
                else if (count == 1) p1 = pos;
                count++;
                if (count >= 2) break;
            }

            if (count == 0)
            {
                _activeTouchCount = 0;
                return;
            }

            if (count == 1)
            {
                HandleSingleTouch(p0);
                return;
            }

            HandleTwoFingerTouch(p0, p1);
        }

        private void HandleSingleTouch(Vector2 pos)
        {
            if (!_dragPan)
            {
                _activeTouchCount = 1;
                _touch0Pos = pos;
                return;
            }

            if (_activeTouchCount != 1)
            {
                // Gesture start
                if (_isPointerOverUI != null && _isPointerOverUI(pos))
                {
                    _activeTouchCount = 1;
                    _touch0Pos = pos;
                    return;
                }
                _touch0Pos = pos;
                _touch0Start = pos;
                _activeTouchCount = 1;
                OnUserInteraction?.Invoke();
                return;
            }

            Vector2 delta = pos - _touch0Pos;
            _touch0Pos = pos;
            PanByScreenDelta(delta);
        }

        private void HandleTwoFingerTouch(Vector2 p0, Vector2 p1)
        {
            if (!_touchZoomRotate && !_touchPitch && !_dragPan)
            {
                _activeTouchCount = 2;
                return;
            }

            if (_activeTouchCount != 2)
            {
                // Gesture start -- snapshot initial state
                _touch0Start = p0;
                _touch1Start = p1;
                _touch0Pos = p0;
                _touch1Pos = p1;
                _touchInitialDistance = Vector2.Distance(p0, p1);
                _touchInitialAngleDeg = Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg;
                _touchInitialZoom = _mapState.Zoom;
                _touchInitialBearing = _mapState.Bearing;
                _touchInitialPitch = _mapState.Pitch;
                _touchPitchEngaged = false;
                _activeTouchCount = 2;

                Vector2 midpoint = (p0 + p1) * 0.5f;
                _touchInitialAnchorValid = ScreenToMercator(midpoint, out _touchInitialAnchorMerc);
                OnUserInteraction?.Invoke();
                return;
            }

            _touch0Pos = p0;
            _touch1Pos = p1;
            Vector2 newMidpoint = (p0 + p1) * 0.5f;

            float newZoom = _mapState.Zoom;
            float newBearing = _mapState.Bearing;
            float newPitch = _mapState.Pitch;

            if (_touchZoomRotate && _touchInitialDistance > 1f)
            {
                float newDist = Vector2.Distance(p0, p1);
                if (newDist > 1f)
                {
                    newZoom = Mathf.Clamp(
                        _touchInitialZoom + Mathf.Log(newDist / _touchInitialDistance, 2f),
                        MapConstants.MinZoom, MapConstants.MaxZoom);
                }
                float newAngle = Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg;
                // Screen Y is up in Unity; bearing increases clockwise on the map.
                // Finger CCW rotation (angleDelta > 0) should rotate map CCW on screen,
                // which requires bearing to decrease.
                newBearing = _touchInitialBearing - (newAngle - _touchInitialAngleDeg);
            }

            if (_touchPitch)
            {
                Vector2 dA = p0 - _touch0Start;
                Vector2 dB = p1 - _touch1Start;
                bool verticalParallel =
                    Mathf.Abs(dA.y) > Mathf.Abs(dA.x) && Mathf.Abs(dB.y) > Mathf.Abs(dB.x) &&
                    Mathf.Sign(dA.y) == Mathf.Sign(dB.y) &&
                    (Mathf.Abs(dA.y) + Mathf.Abs(dB.y)) > 20f;
                if (verticalParallel) _touchPitchEngaged = true;
                if (_touchPitchEngaged)
                {
                    float avgDy = (dA.y + dB.y) * 0.5f;
                    newPitch = Mathf.Clamp(
                        _touchInitialPitch - avgDy * _pitchSpeed, 0f, MapState.MaxPitchHardLimit);
                }
            }

            // Commit zoom/bearing/pitch first so ScreenToMercator uses the new camera state.
            var currentCenter = _mapState.Center;
            _mapState.SetState(currentCenter, newZoom, newBearing, newPitch);
            _targetZoom = newZoom;

            // Anchor: keep the Mercator point that was under the initial midpoint
            // at the current midpoint screen position.
            if ((_dragPan || _touchZoomRotate) && _touchInitialAnchorValid &&
                ScreenToMercator(newMidpoint, out var qMerc))
            {
                var curCenterMerc = CoordinateConversion.LngLatToMercator(currentCenter);
                double offsetX = qMerc.X - curCenterMerc.X;
                double offsetY = qMerc.Y - curCenterMerc.Y;
                var newCenterMerc = new MercatorCoordinate(
                    _touchInitialAnchorMerc.X - offsetX,
                    _touchInitialAnchorMerc.Y - offsetY);
                _mapState.Center = CoordinateConversion.MercatorToLngLat(newCenterMerc).Wrap();
            }
        }

        // -------------------- Shared helpers --------------------

        private void ApplySmoothZoom()
        {
            if (_animator != null && _animator.IsAnimating)
            {
                _targetZoom = _mapState.Zoom;
                _zoomVelocity = 0f;
                return;
            }
            if (Mathf.Abs(_mapState.Zoom - _targetZoom) > 0.001f)
            {
                float newZoom = Mathf.SmoothDamp(_mapState.Zoom, _targetZoom,
                    ref _zoomVelocity, _zoomSmoothTime);
                _mapState.Zoom = newZoom;
            }
        }

        private void PanByScreenDelta(Vector2 delta)
        {
            if (_camera == null || delta.sqrMagnitude < 0.01f) return;

            float camHeight = _camera.transform.position.y;
            float worldPerPixel = 2f * camHeight
                                  * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                                  / _camera.pixelHeight;
            float worldScale = CoordinateConversion.GetWorldScale(_mapState.Zoom);

            double mercDeltaX = -delta.x * worldPerPixel / worldScale;
            double mercDeltaY = delta.y * worldPerPixel / worldScale;

            float bearingRad = _mapState.Bearing * Mathf.Deg2Rad;
            double rotatedDeltaX = mercDeltaX * Math.Cos(bearingRad) - mercDeltaY * Math.Sin(bearingRad);
            double rotatedDeltaY = mercDeltaX * Math.Sin(bearingRad) + mercDeltaY * Math.Cos(bearingRad);

            var centerMerc = CoordinateConversion.LngLatToMercator(_mapState.Center);
            var newMerc = new MercatorCoordinate(
                centerMerc.X + rotatedDeltaX,
                centerMerc.Y + rotatedDeltaY);
            _mapState.Center = CoordinateConversion.MercatorToLngLat(newMerc).Wrap();
        }

        /// <summary>
        /// Zoom to a target level while keeping the LngLat under <paramref name="screenPos"/>
        /// at the same screen position. Matches MapLibre GL JS zoom-around-point behavior.
        /// </summary>
        private void ZoomAround(Vector2 screenPos, float targetZoom, float duration)
        {
            if (!ScreenToLngLat(screenPos, out var anchor))
            {
                _animator?.ZoomTo(targetZoom, duration);
                _targetZoom = targetZoom;
                return;
            }

            var mCenter = CoordinateConversion.LngLatToMercator(_mapState.Center);
            var mAnchor = CoordinateConversion.LngLatToMercator(anchor);
            float zoomDelta = targetZoom - _mapState.Zoom;
            float factor = Mathf.Pow(2f, -zoomDelta);
            double newMX = mAnchor.X + (mCenter.X - mAnchor.X) * factor;
            double newMY = mAnchor.Y + (mCenter.Y - mAnchor.Y) * factor;
            var newCenter = CoordinateConversion.MercatorToLngLat(
                new MercatorCoordinate(newMX, newMY)).Wrap();

            OnUserInteraction?.Invoke();

            if (_animator != null)
            {
                _animator.EaseTo(new EaseToOptions
                {
                    Center = newCenter,
                    Zoom = targetZoom,
                    Duration = duration,
                    Easing = MapAnimator.EasingFunction.EaseInOut,
                });
            }
            else
            {
                _mapState.SetState(newCenter, targetZoom, _mapState.Bearing, _mapState.Pitch);
            }
            _targetZoom = targetZoom;
        }

        private bool ScreenToLngLat(Vector2 screenPos, out LngLat result)
        {
            if (ScreenToMercator(screenPos, out var merc))
            {
                result = CoordinateConversion.MercatorToLngLat(merc);
                return true;
            }
            result = default;
            return false;
        }

        private bool ScreenToMercator(Vector2 screenPos, out MercatorCoordinate result)
        {
            result = default;
            if (_camera == null) return false;

            var ray = _camera.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-6f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;

            var worldPoint = ray.origin + ray.direction * t;
            float worldScale = CoordinateConversion.GetWorldScale(_mapState.Zoom);
            var mapCenter = CoordinateConversion.LngLatToMercator(_mapState.Center);
            result = new MercatorCoordinate(
                mapCenter.X + worldPoint.x / worldScale,
                mapCenter.Y - worldPoint.z / worldScale);
            return true;
        }

        private static bool IsShiftHeld()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.shiftKey.isPressed;
        }
    }
}
