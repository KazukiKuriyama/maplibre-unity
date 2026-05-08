using System;
using UnityEngine;

namespace MapLibre.Unity.CameraControl
{
    /// <summary>
    /// Positions the Unity camera based on the map state.
    /// Camera looks down at the map plane (Y=0) from above.
    /// Zoom controls height, bearing rotates, pitch tilts toward horizon.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class MapCamera : MonoBehaviour
    {
        [SerializeField] private float _baseCameraHeight = 50000f;
        [SerializeField] private float _minCameraHeight = 5f;
        [Tooltip("Automatically adjust near/far clip planes based on camera height. " +
                 "Disable if you manage clipping planes manually.")]
        [SerializeField] private bool _autoAdjustClipPlanes = true;

        private UnityEngine.Camera _camera;

        /// <summary>
        /// Base height in Unity world units used for the
        /// height = BaseCameraHeight / 2^zoom mapping. Exposed so
        /// FreeCameraOptions can do the inverse calculation
        /// (zoom = log2(BaseCameraHeight / height)).
        /// </summary>
        public float BaseCameraHeight => _baseCameraHeight;

        /// <summary>
        /// When true, <see cref="UpdateFromMapState"/> becomes a no-op and the camera
        /// transform is left untouched. Set automatically by
        /// <see cref="MapLibreMap.SetFreeCameraOptions"/> so external rigs
        /// (Cinemachine, FPS controllers, Timeline) can drive the camera without
        /// having their changes overwritten by the map's own layout pass.
        /// Reset via <see cref="MapLibreMap.ResumeMapDrivenCamera"/>.
        /// </summary>
        public bool ExternallyDriven { get; set; }

        private void Awake()
        {
            _camera = GetComponent<UnityEngine.Camera>();
        }

        public void UpdateFromMapState(MapState state)
        {
            if (ExternallyDriven) return;

            float zoom = state.Zoom;
            float bearing = state.Bearing;
            float pitch = state.Pitch;

            // Height decreases exponentially with zoom
            float height = _baseCameraHeight / Mathf.Pow(2f, zoom);
            height = Mathf.Max(height, _minCameraHeight);

            float pitchRad = pitch * Mathf.Deg2Rad;
            float bearingRad = bearing * Mathf.Deg2Rad;

            // At pitch=0: straight down. At pitch>0: offset backward
            float horizontalOffset = height * Mathf.Tan(pitchRad);

            Vector3 backward = new Vector3(
                -Mathf.Sin(bearingRad),
                0f,
                -Mathf.Cos(bearingRad)
            );

            Vector3 position = new Vector3(0f, height, 0f) + backward * horizontalOffset;
            transform.localPosition = position;
            transform.localRotation = Quaternion.Euler(90f - pitch, bearing, 0f);

            AdjustClipPlanes(height, pitch);
        }

        /// <summary>
        /// Adjust near/far clip planes based on camera height + pitch so the
        /// visible ground stays inside the frustum at any tilt. The far-plane
        /// multiplier grows with pitch so the horizon (visible at high pitch)
        /// isn't clipped: tan(pitch) blows up near 90°, so we cap conservatively.
        /// LOD tile selection keeps tile counts bounded even when the far plane
        /// is pushed out for steep pitches.
        ///
        /// Public so external rigs (FreeCameraOptions / Cinemachine) can keep
        /// the frustum honest after they move the transform -- without this,
        /// orbit / dolly setups whose camera-to-target distance exceeds the
        /// map-driven far plane render only background.
        /// </summary>
        public void AdjustClipPlanes(float height, float pitch)
        {
            if (!_autoAdjustClipPlanes || _camera == null) return;
            float pitchRad = pitch * Mathf.Deg2Rad;
            float distToGround = height / Mathf.Max(Mathf.Cos(pitchRad), 0.01f);
            float farMultiplier = Mathf.Lerp(4f, 80f, Mathf.Clamp01(pitch / 80f));
            _camera.nearClipPlane = Mathf.Max(distToGround * 0.001f, 0.01f);
            _camera.farClipPlane = distToGround * farMultiplier;
        }

        /// <summary>
        /// Calculate the zoom level that fits the given Mercator-space bounds
        /// within the camera frustum, accounting for bearing and padding.
        /// </summary>
        /// <param name="mercatorWidth">Width of bounds in normalized Mercator [0,1] space.</param>
        /// <param name="mercatorHeight">Height of bounds in normalized Mercator [0,1] space.</param>
        /// <param name="bearing">Map bearing in degrees.</param>
        /// <param name="padding">Viewport padding in pixels (top, bottom, left, right).</param>
        /// <returns>Zoom level that fits the bounds.</returns>
        public float CalculateZoomForBounds(
            double mercatorWidth, double mercatorHeight, float bearing, PaddingOptions padding)
        {
            if (_camera == null) return 0f;

            // Rotate bounds extent by bearing
            float bearingRad = bearing * Mathf.Deg2Rad;
            float cosB = Mathf.Abs(Mathf.Cos(bearingRad));
            float sinB = Mathf.Abs(Mathf.Sin(bearingRad));
            double rotatedWidth = mercatorWidth * cosB + mercatorHeight * sinB;
            double rotatedHeight = mercatorWidth * sinB + mercatorHeight * cosB;

            // Guard against zero-size bounds (single point)
            rotatedWidth = Math.Max(rotatedWidth, 1e-10);
            rotatedHeight = Math.Max(rotatedHeight, 1e-10);

            // Camera frustum geometry at pitch=0:
            //   cameraHeight = baseCameraHeight / 2^zoom
            //   visibleHeight = 2 * cameraHeight * tan(vfov/2)
            //   visibleWidth  = visibleHeight * aspect
            //
            // Bounds in world units = mercExtent * TileSize * 2^zoom
            //
            // For fit: mercExtent * TileSize * 2^zoom <= visible * usableFraction
            // Solving: zoom <= 0.5 * log2(2 * baseCH * tan(vfov/2) * factor / (mercExtent * TileSize))

            float vfovRad = _camera.fieldOfView * Mathf.Deg2Rad;
            float tanHalfVfov = Mathf.Tan(vfovRad * 0.5f);
            float aspect = _camera.aspect;

            // Usable viewport fraction after padding
            float pixelWidth = _camera.pixelWidth;
            float pixelHeight = _camera.pixelHeight;
            float usableWidthFraction = Mathf.Max(
                1f - (padding.Left + padding.Right) / pixelWidth, 0.1f);
            float usableHeightFraction = Mathf.Max(
                1f - (padding.Top + padding.Bottom) / pixelHeight, 0.1f);

            float baseVisibleHeight = 2f * _baseCameraHeight * tanHalfVfov;
            float baseVisibleWidth = baseVisibleHeight * aspect;

            // zoom = 0.5 * log2(baseVisible * usable / (mercExtent * TileSize))
            float zoomForWidth = 0.5f * (float)Math.Log(
                baseVisibleWidth * usableWidthFraction / (rotatedWidth * MapConstants.TileSize), 2.0);
            float zoomForHeight = 0.5f * (float)Math.Log(
                baseVisibleHeight * usableHeightFraction / (rotatedHeight * MapConstants.TileSize), 2.0);

            return Mathf.Min(zoomForWidth, zoomForHeight);
        }

        /// <summary>
        /// Project viewport corners onto the Y=0 ground plane.
        /// Returns the projected polygon vertices for accurate tile coverage calculation.
        /// When a ray doesn't hit the ground (looking above horizon), it is clamped
        /// to the horizon distance derived from camera height.
        /// </summary>
        public Vector3[] GetGroundPlaneFootprint()
        {
            if (_camera == null) return null;

            float farClip = _camera.farClipPlane;

            // 4 viewport corners
            Vector3[] viewportCorners = new Vector3[]
            {
                new(0, 0, 0), // bottom-left
                new(1, 0, 0), // bottom-right
                new(1, 1, 0), // top-right
                new(0, 1, 0), // top-left
            };

            Vector3[] groundPoints = new Vector3[4];

            for (int i = 0; i < 4; i++)
            {
                Ray ray = _camera.ViewportPointToRay(viewportCorners[i]);

                if (ray.direction.y < -0.0001f)
                {
                    float t = -ray.origin.y / ray.direction.y;

                    // Clamp ray distance to far clip plane -- ground beyond it won't render
                    t = Mathf.Min(t, farClip);

                    Vector3 hit = ray.origin + ray.direction * t;
                    groundPoints[i] = new Vector3(hit.x, 0, hit.z);
                }
                else
                {
                    // Ray parallel to or away from ground -- project to far clip distance
                    Vector3 hit = ray.origin + ray.direction * farClip;
                    groundPoints[i] = new Vector3(hit.x, 0, hit.z);
                }
            }

            return groundPoints;
        }
    }
}
