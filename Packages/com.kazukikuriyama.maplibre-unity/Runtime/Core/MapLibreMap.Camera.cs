using MapLibre.Unity.CameraControl;
using UnityEngine;

namespace MapLibre.Unity
{
    /// <summary>
    /// Camera surface -- Project / Unproject helpers, JumpTo / EaseTo / FlyTo /
    /// FitBounds, free-camera bridge, zoom / pitch / bearing setters, and
    /// state predicates (IsMoving, IsZooming, ...).
    /// Animations route through <see cref="MapLibre.Unity.CameraControl.MapAnimator"/>;
    /// instantaneous updates push into <see cref="MapState"/> directly so
    /// <c>OnMapStateChanged</c> in the main partial picks them up next frame.
    /// </summary>
    public partial class MapLibreMap
    {
        // === Projection API matching MapLibre GL JS ===

        /// <summary>
        /// Convert a geographic coordinate to screen pixel position.
        /// Matches map.project() in MapLibre GL JS.
        /// Returns null if the coordinate is behind the camera.
        /// </summary>
        public Vector2? Project(LngLat lngLat)
        {
            if (_mapCamera == null || State == null) return null;

            var merc = CoordinateConversion.LngLatToMercator(lngLat);
            var centerMerc = CoordinateConversion.LngLatToMercator(State.Center);
            float worldScale = CoordinateConversion.GetWorldScale(State.Zoom);
            var worldPos = CoordinateConversion.MercatorToUnityWorld(merc, centerMerc, worldScale);

            var screenPos = _mapCamera.WorldToScreenPoint(worldPos);
            if (screenPos.z < 0) return null;
            return new Vector2(screenPos.x, screenPos.y);
        }

        /// <summary>
        /// Convert a geographic coordinate to a Unity world position (Y=0 plane).
        /// Returns null if State is not initialized.
        /// </summary>
        public Vector3? LngLatToWorld(LngLat lngLat)
        {
            if (State == null) return null;

            var merc = CoordinateConversion.LngLatToMercator(lngLat);
            var centerMerc = CoordinateConversion.LngLatToMercator(State.Center);
            float worldScale = CoordinateConversion.GetWorldScale(State.Zoom);
            return CoordinateConversion.MercatorToUnityWorld(merc, centerMerc, worldScale);
        }

        /// <summary>
        /// The camera used for map rendering.
        /// </summary>
        public UnityEngine.Camera MapCamera => _mapCamera;

        // === Public API matching MapLibre GL JS ===

        /// <summary>
        /// Animate map to a new position. Matches map.easeTo() in MapLibre GL JS.
        /// </summary>
        public void EaseTo(EaseToOptions options)
        {
            Animator?.EaseTo(options);
        }

        /// <summary>
        /// Fly to a new position with zoom animation. Matches map.flyTo() in MapLibre GL JS.
        /// </summary>
        public void FlyTo(FlyToOptions options)
        {
            Animator?.FlyTo(options);
        }

        /// <summary>
        /// Animate to a new zoom level. Matches map.zoomTo() in MapLibre GL JS.
        /// </summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void ZoomTo(float zoom, float durationMs = 300f)
        {
            Animator?.ZoomTo(zoom, durationMs);
        }

        /// <summary>
        /// Fit the map to show the given geographic bounds. Matches map.fitBounds() in MapLibre GL JS.
        /// </summary>
        public void FitBounds(LngLatBounds bounds, FitBoundsOptions options = default)
        {
            Animator?.FitBounds(bounds, options);
        }

        /// <summary>
        /// Fit the map to show all given points.
        /// </summary>
        public void FitBounds(LngLat[] points, FitBoundsOptions options = default)
        {
            Animator?.FitPoints(points, options);
        }

        // === Camera shortcut API (matches MapLibre GL JS) ===
        // JumpTo/setCenter/setZoom/setBearing/setPitch are non-animated and change
        // state synchronously. Any in-flight animation is cancelled first so the
        // new state sticks -- otherwise the animation would overwrite it next frame.

        /// <summary>
        /// Instantly move the camera (no animation). Only non-null options are applied.
        /// Matches map.jumpTo() in MapLibre GL JS.
        /// </summary>
        public void JumpTo(JumpToOptions options)
        {
            if (State == null) return;
            Animator?.CancelAnimation();
            State.SetState(
                options.Center ?? State.Center,
                options.Zoom ?? State.Zoom,
                options.Bearing ?? State.Bearing,
                options.Pitch ?? State.Pitch);
            _inputHandler?.SyncTargetFromMapState();
        }

        /// <summary>Set the map center (no animation). Matches map.setCenter().</summary>
        public void SetCenter(LngLat center)
        {
            Animator?.CancelAnimation();
            if (State != null) State.Center = center;
        }

        /// <summary>Set the zoom level (no animation). Matches map.setZoom().</summary>
        public void SetZoom(float zoom)
        {
            Animator?.CancelAnimation();
            if (State != null) State.Zoom = zoom;
            // MapInputHandler keeps its own smooth-damped zoom target; without
            // re-syncing it the next ApplySmoothZoom would walk State.Zoom
            // back to whatever the user had cached before this call.
            _inputHandler?.SyncTargetFromMapState();
        }

        /// <summary>Set the bearing in degrees (no animation). Matches map.setBearing().</summary>
        public void SetBearing(float bearing)
        {
            Animator?.CancelAnimation();
            if (State != null) State.Bearing = bearing;
        }

        /// <summary>Set the pitch in degrees (no animation). Matches map.setPitch().</summary>
        public void SetPitch(float pitch)
        {
            Animator?.CancelAnimation();
            if (State != null) State.Pitch = pitch;
        }

        /// <summary>Current map center. Matches map.getCenter().</summary>
        public LngLat GetCenter() => State != null ? State.Center : default;

        /// <summary>Current zoom level. Matches map.getZoom().</summary>
        public float GetZoom() => State != null ? State.Zoom : 0f;

        /// <summary>Current bearing in degrees. Matches map.getBearing().</summary>
        public float GetBearing() => State != null ? State.Bearing : 0f;

        /// <summary>Current pitch in degrees. Matches map.getPitch().</summary>
        public float GetPitch() => State != null ? State.Pitch : 0f;

        /// <summary>
        /// The geographic bounds currently visible on screen. Matches map.getBounds().
        /// Computed by unprojecting the four screen corners; if the frustum extends
        /// past the horizon (high pitch), corner rays that miss the ground are
        /// replaced by points along the near-horizon ray so the returned bounds
        /// still cover visible terrain. Returns null when the camera is not ready.
        /// </summary>
        public LngLatBounds? GetBounds()
        {
            if (_mapCamera == null || State == null) return null;

            float w = Screen.width;
            float h = Screen.height;
            var corners = new Vector2[] {
                new(0, 0), new(w, 0), new(w, h), new(0, h),
                // Add the screen-center point as a fallback sample so a partial
                // ground intersection still yields a non-empty bounds.
                new(w * 0.5f, h * 0.5f),
            };

            LngLatBounds? bounds = null;
            foreach (var corner in corners)
            {
                var ll = Unproject(corner);
                if (!ll.HasValue) continue;
                bounds = bounds.HasValue ? bounds.Value.Extend(ll.Value) : LngLatBounds.FromPoints(ll.Value);
            }
            return bounds;
        }

        /// <summary>
        /// Convert a screen point to geographic coordinates.
        /// Matches map.unproject() in MapLibre GL JS.
        /// Returns null if the ray does not intersect the ground plane.
        /// </summary>
        public LngLat? Unproject(Vector2 screenPoint)
        {
            if (_mapCamera == null) return null;

            var ray = _mapCamera.ScreenPointToRay(new Vector3(screenPoint.x, screenPoint.y, 0));
            if (ray.direction.y == 0) return null;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return null;

            var worldPoint = ray.origin + ray.direction * t;
            float worldScale = CoordinateConversion.GetWorldScale(State.Zoom);
            var mapCenter = CoordinateConversion.LngLatToMercator(State.Center);
            double mercX = mapCenter.X + worldPoint.x / worldScale;
            double mercY = mapCenter.Y - worldPoint.z / worldScale;
            return CoordinateConversion.MercatorToLngLat(new MercatorCoordinate(mercX, mercY));
        }

        // === Free camera API matching MapLibre GL JS ===
        // Lets external rigs (Cinemachine, FPS controllers, Timeline) drive the
        // Unity Camera transform directly while the map state (center / zoom /
        // bearing / pitch) auto-syncs from the camera position so tile loading,
        // collision detection and projection still work.

        /// <summary>
        /// Read the current camera transform as a free-camera bundle. Matches
        /// <c>map.getFreeCameraOptions()</c>.
        /// </summary>
        public FreeCameraOptions GetFreeCameraOptions()
        {
            if (_mapCamera == null) return default;
            return new FreeCameraOptions
            {
                Position = _mapCamera.transform.localPosition,
                Rotation = _mapCamera.transform.localRotation,
            };
        }

        /// <summary>
        /// Apply a free-camera bundle: set the Unity Camera transform from
        /// <paramref name="options"/> and derive map state (center / zoom / bearing /
        /// pitch) so the map continues to load tiles for the area you're looking at.
        ///
        /// Once called, the map's automatic camera positioning (driven by SetCenter,
        /// SetZoom, EaseTo, FlyTo etc.) is suspended -- Cinemachine or your FPS
        /// controller now owns the camera transform. Call
        /// <see cref="ResumeMapDrivenCamera"/> to switch back to map-driven mode.
        ///
        /// Matches <c>map.setFreeCameraOptions()</c> in MapLibre GL JS.
        /// </summary>
        public void SetFreeCameraOptions(FreeCameraOptions options)
        {
            if (_mapCamera == null || _cameraController == null || State == null) return;

            if (options.Position.HasValue)
                _mapCamera.transform.localPosition = options.Position.Value;
            if (options.Rotation.HasValue)
                _mapCamera.transform.localRotation = options.Rotation.Value;

            _cameraController.ExternallyDriven = true;
            // Cancel any in-flight tween -- letting an animator continue to push
            // its own MapState target while the external rig drives the
            // transform produces zoom / center oscillation between the two.
            Animator?.CancelAnimation();
            DeriveMapStateFromCameraTransform();
            // Re-sync the smooth-damped zoom / pitch targets in MapInputHandler
            // so its ApplySmoothZoom doesn't drag State.Zoom back toward the
            // value cached before the external rig took over. Without this,
            // orbit / dolly setups visibly oscillate the zoom each frame even
            // when the input handler is supposed to be inert.
            _inputHandler?.SyncTargetFromMapState();
        }

        /// <summary>
        /// Switch back to map-driven camera positioning. The current map state
        /// (center / zoom / bearing / pitch) is reapplied to the camera on the
        /// next frame. Matches the typical "release control" pattern used after a
        /// Cinemachine cutscene completes.
        /// </summary>
        public void ResumeMapDrivenCamera()
        {
            if (_cameraController == null) return;
            _cameraController.ExternallyDriven = false;
            _cameraController.UpdateFromMapState(State);
        }

        /// <summary>
        /// Derive (center, zoom, bearing, pitch) from the camera's current Unity
        /// transform. Inverse of <see cref="MapCamera.UpdateFromMapState"/>.
        /// </summary>
        private void DeriveMapStateFromCameraTransform()
        {
            if (_mapCamera == null || _cameraController == null) return;

            var pos = _mapCamera.transform.localPosition;
            var euler = _mapCamera.transform.localEulerAngles;

            // Bearing = camera Y-rotation. Pitch = (camera X-rotation - 90).
            // MapState clamps both into valid ranges.
            float bearing = euler.y;
            float pitch = euler.x - 90f;

            // Camera height → zoom. height = baseCameraHeight / 2^zoom
            // ⇒ zoom = log2(baseCameraHeight / height)
            float height = Mathf.Max(pos.y, 0.0001f);
            float zoom = Mathf.Log(_cameraController.BaseCameraHeight / height, 2f);

            // Center = where the camera ray hits Y=0. Derive from position + forward
            // direction. With pitch≈0 the ray is straight down so center == camera XZ.
            // With pitch>0 it's the look-at point on the ground.
            var fwd = _mapCamera.transform.forward;
            Vector3 hit;
            if (Mathf.Abs(fwd.y) > 1e-4f)
            {
                float t = -pos.y / fwd.y; // distance along ray to Y=0
                if (t < 0f) t = 0f;       // looking up: clamp to camera position
                hit = pos + fwd * t;
            }
            else
            {
                hit = new Vector3(pos.x, 0f, pos.z);
            }

            // Convert hit (Unity world XZ) → mercator → LngLat.
            var centerMercFromCamera = CoordinateConversion.LngLatToMercator(State.Center);
            float worldScale = CoordinateConversion.GetWorldScale(State.Zoom);
            var newCenterMerc = CoordinateConversion.UnityWorldToMercator(
                hit, centerMercFromCamera, worldScale);
            var newCenter = CoordinateConversion.MercatorToLngLat(newCenterMerc);

            // Push to MapState in one shot. SetState clamps zoom/pitch, normalizes bearing.
            State.SetState(newCenter, zoom, bearing, pitch);

            // OnMapStateChanged routes back to MapCamera.UpdateFromMapState which
            // early-returns under ExternallyDriven, so the clip plane bookkeeping
            // there is skipped. Recompute manually from the actual transform --
            // otherwise an orbit whose radius exceeds the previous far-plane
            // (set when the map drove the camera) clips the entire scene.
            _cameraController.AdjustClipPlanes(Mathf.Max(pos.y, 0.0001f), State.Pitch);
        }

        /// <summary>
        /// Force re-evaluation of visible tiles.
        /// Call after SetData() to refresh GeoJSON layers with updated data.
        /// </summary>
        public void RefreshTiles()
        {
            if (!_isInitialized) return;
            // Caller is doing the refresh themselves -- cancel the deferred
            // safety-net set by AddLayer(triggerRefresh:false) so the explicit
            // batch pattern (AddLayer false × N + RefreshTiles) still results
            // in exactly one refresh.
            _pendingLayerRefresh = false;
            _tileManager?.Clear();
            _needsTileUpdate = true;
        }

        // === Convenience camera API matching MapLibre GL JS ===

        /// <summary>
        /// Pan the map by an offset in screen pixels. Matches map.panBy().
        /// </summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void PanBy(Vector2 offsetPixels, float durationMs = 300f)
        {
            if (!_isInitialized) return;
            // Convert pixel offset to a target screen-space center
            var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var target = Unproject(screenCenter - offsetPixels);
            if (!target.HasValue) return;
            EaseTo(new EaseToOptions { Center = target.Value, Duration = durationMs });
        }

        /// <summary>
        /// Animate the map center to the given coordinate. Matches map.panTo().
        /// </summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void PanTo(LngLat center, float durationMs = 300f)
            => EaseTo(new EaseToOptions { Center = center, Duration = durationMs });

        /// <summary>Increase zoom by 1. Matches map.zoomIn().</summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void ZoomIn(float durationMs = 300f)
        {
            if (!_isInitialized) return;
            ZoomTo(State.Zoom + 1f, durationMs);
        }

        /// <summary>Decrease zoom by 1. Matches map.zoomOut().</summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void ZoomOut(float durationMs = 300f)
        {
            if (!_isInitialized) return;
            ZoomTo(State.Zoom - 1f, durationMs);
        }

        /// <summary>Animate to a new bearing. Matches map.rotateTo().</summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void RotateTo(float bearing, float durationMs = 300f)
            => EaseTo(new EaseToOptions { Bearing = bearing, Duration = durationMs });

        /// <summary>Animate bearing to 0 (north up). Matches map.resetNorth().</summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void ResetNorth(float durationMs = 500f) => RotateTo(0f, durationMs);

        /// <summary>
        /// Snap to north if the current bearing is within <paramref name="threshold"/>
        /// degrees of north. Matches map.snapToNorth().
        /// </summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void SnapToNorth(float threshold = 7f, float durationMs = 300f)
        {
            if (!_isInitialized) return;
            float b = State.Bearing;
            float wrapped = b > 180f ? b - 360f : b;
            if (Mathf.Abs(wrapped) <= threshold) ResetNorth(durationMs);
        }

        /// <summary>Stop any in-progress camera animation. Matches map.stop().</summary>
        public void Stop()
        {
            Animator?.CancelAnimation();
        }

        // === Camera bounds / limits matching MapLibre GL JS ===

        /// <summary>Lower zoom bound. Matches map.setMinZoom().</summary>
        public void SetMinZoom(float minZoom)
        {
            if (State != null) State.MinZoom = minZoom;
        }

        /// <summary>Upper zoom bound. Matches map.setMaxZoom().</summary>
        public void SetMaxZoom(float maxZoom)
        {
            if (State != null) State.MaxZoom = maxZoom;
        }

        /// <summary>Lower pitch bound in degrees. Matches map.setMinPitch().</summary>
        public void SetMinPitch(float minPitch)
        {
            if (State != null) State.MinPitch = minPitch;
        }

        /// <summary>Upper pitch bound in degrees. Matches map.setMaxPitch().</summary>
        public void SetMaxPitch(float maxPitch)
        {
            if (State != null) State.MaxPitch = maxPitch;
        }

        /// <summary>Pass null to clear. Matches map.setMaxBounds().</summary>
        public void SetMaxBounds(LngLatBounds? bounds)
        {
            if (State != null) State.MaxBounds = bounds;
        }

        /// <summary>Returns null when no bounds are set. Matches map.getMaxBounds().</summary>
        public LngLatBounds? GetMaxBounds() => State?.MaxBounds;

        public float GetMinZoom() => State != null ? State.MinZoom : MapConstants.MinZoom;
        public float GetMaxZoom() => State != null ? State.MaxZoom : MapConstants.MaxZoom;
        public float GetMinPitch() => State != null ? State.MinPitch : 0f;
        public float GetMaxPitch() => State != null ? State.MaxPitch : MapState.MaxPitchHardLimit;

        // === State predicates matching MapLibre GL JS ===

        /// <summary>True after the initial style + sources have loaded. Matches map.isStyleLoaded().</summary>
        public bool IsStyleLoaded() => _isInitialized && Style != null;

        /// <summary>True if any camera animation is running. Matches map.isMoving() (animation only).</summary>
        public bool IsMoving() => _isMoving || (Animator != null && Animator.IsAnimating);
        public bool IsZooming() => _isZooming;
        public bool IsRotating() => _isRotating;
    }
}
