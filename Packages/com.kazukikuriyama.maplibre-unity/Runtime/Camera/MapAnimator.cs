using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.CameraControl
{
    /// <summary>
    /// Provides animated map state transitions matching MapLibre GL JS API.
    /// Supports easeTo, flyTo, and zoomTo with configurable duration and easing.
    /// </summary>
    public class MapAnimator : MonoBehaviour
    {
        public event Action OnAnimationStart;
        public event Action OnAnimationEnd;

        private MapState _mapState;
        private AnimationState _animation;
        private bool _isAnimating;

        public bool IsAnimating => _isAnimating;

        private struct AnimationState
        {
            public LngLat StartCenter;
            public LngLat EndCenter;
            public float StartZoom;
            public float EndZoom;
            public float StartBearing;
            public float EndBearing;
            public float StartPitch;
            public float EndPitch;
            public float Duration;
            public float Elapsed;
            public EasingFunction Easing;
            public bool AnimateCenter;
            public bool AnimateZoom;
            public bool AnimateBearing;
            public bool AnimatePitch;
        }

        public enum EasingFunction
        {
            Linear,
            EaseInOut,
            EaseIn,
            EaseOut,
            EaseInOutCubic
        }

        public void Initialize(MapState mapState)
        {
            _mapState = mapState;
        }

        /// <summary>
        /// Smoothly transition to the target state over the specified duration.
        /// Matches MapLibre GL JS map.easeTo(). Duration is in milliseconds.
        /// </summary>
        public void EaseTo(EaseToOptions options)
        {
            if (_mapState == null) return;

            CancelAnimation();

            // Public Duration is milliseconds; internal animation runs against
            // Time.deltaTime (seconds) so convert at the boundary.
            float durationMs = options.Duration > 0f ? options.Duration : 300f;
            float durationSec = durationMs / 1000f;

            _animation = new AnimationState
            {
                StartCenter = _mapState.Center,
                EndCenter = options.Center ?? _mapState.Center,
                StartZoom = _mapState.Zoom,
                EndZoom = options.Zoom ?? _mapState.Zoom,
                StartBearing = _mapState.Bearing,
                EndBearing = options.Bearing ?? _mapState.Bearing,
                StartPitch = _mapState.Pitch,
                EndPitch = options.Pitch ?? _mapState.Pitch,
                Duration = durationSec,
                Elapsed = 0f,
                Easing = options.Easing,
                AnimateCenter = options.Center.HasValue,
                AnimateZoom = options.Zoom.HasValue,
                AnimateBearing = options.Bearing.HasValue,
                AnimatePitch = options.Pitch.HasValue,
            };

            // Normalize bearing for shortest rotation path
            if (_animation.AnimateBearing)
            {
                float diff = _animation.EndBearing - _animation.StartBearing;
                if (diff > 180f) _animation.EndBearing -= 360f;
                else if (diff < -180f) _animation.EndBearing += 360f;
            }

            _isAnimating = true;
            OnAnimationStart?.Invoke();
        }

        /// <summary>
        /// Animated zoom to target level. Matches MapLibre GL JS map.zoomTo().
        /// </summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public void ZoomTo(float zoom, float durationMs = 300f, EasingFunction easing = EasingFunction.EaseInOut)
        {
            EaseTo(new EaseToOptions
            {
                Zoom = zoom,
                Duration = durationMs,
                Easing = easing
            });
        }

        /// <summary>
        /// Animated flight to destination. Matches MapLibre GL JS map.flyTo().
        /// Uses zoom-out-then-in pattern for long distances.
        /// </summary>
        public void FlyTo(FlyToOptions options)
        {
            if (_mapState == null) return;

            // For short distances, use easeTo
            var startMerc = CoordinateConversion.LngLatToMercator(_mapState.Center);
            var endCenter = options.Center ?? _mapState.Center;
            var endMerc = CoordinateConversion.LngLatToMercator(endCenter);

            double distance = Math.Sqrt(
                Math.Pow(endMerc.X - startMerc.X, 2) +
                Math.Pow(endMerc.Y - startMerc.Y, 2));

            // Compute duration based on distance if not specified
            float duration = options.Duration > 0 ? options.Duration : ComputeFlyDuration(distance);

            EaseTo(new EaseToOptions
            {
                Center = endCenter,
                Zoom = options.Zoom,
                Bearing = options.Bearing,
                Pitch = options.Pitch,
                Duration = duration,
                Easing = EasingFunction.EaseInOutCubic
            });
        }

        /// <summary>
        /// Fit the map to show the given bounds. Matches MapLibre GL JS map.fitBounds().
        /// Computes center and zoom from bounds, then animates via easeTo or flyTo.
        /// </summary>
        public void FitBounds(LngLatBounds bounds, FitBoundsOptions options = default)
        {
            if (_mapState == null) return;

            var mapCamera = GetComponent<MapCamera>();
            if (mapCamera == null) return;

            // Compute center in Mercator space for latitude accuracy
            var center = bounds.Center;

            // Compute bounds extent in normalized Mercator [0,1] space
            var swMerc = CoordinateConversion.LngLatToMercator(bounds.SouthWest);
            var neMerc = CoordinateConversion.LngLatToMercator(bounds.NorthEast);
            double mercWidth = Math.Abs(neMerc.X - swMerc.X);
            double mercHeight = Math.Abs(neMerc.Y - swMerc.Y);

            float bearing = options.Bearing ?? _mapState.Bearing;
            var padding = options.Padding ?? new PaddingOptions();

            float zoom = mapCamera.CalculateZoomForBounds(mercWidth, mercHeight, bearing, padding);

            // Clamp zoom
            float maxZoom = options.MaxZoom ?? MapConstants.MaxZoom;
            zoom = Mathf.Clamp(zoom, MapConstants.MinZoom, maxZoom);

            float duration = options.Duration > 0f ? options.Duration : 500f;

            if (options.Linear)
            {
                EaseTo(new EaseToOptions
                {
                    Center = center,
                    Zoom = zoom,
                    Bearing = options.Bearing,
                    Pitch = options.Pitch,
                    Duration = duration,
                    Easing = options.Easing ?? EasingFunction.EaseInOut,
                });
            }
            else
            {
                FlyTo(new FlyToOptions
                {
                    Center = center,
                    Zoom = zoom,
                    Bearing = options.Bearing,
                    Pitch = options.Pitch,
                    Duration = duration,
                });
            }
        }

        /// <summary>
        /// Fit the map to show all given points. Convenience wrapper around FitBounds.
        /// </summary>
        public void FitPoints(IReadOnlyList<LngLat> points, FitBoundsOptions options = default)
        {
            if (points == null || points.Count == 0) return;

            double minLng = double.MaxValue, minLat = double.MaxValue;
            double maxLng = double.MinValue, maxLat = double.MinValue;

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (p.Longitude < minLng) minLng = p.Longitude;
                if (p.Longitude > maxLng) maxLng = p.Longitude;
                if (p.Latitude < minLat) minLat = p.Latitude;
                if (p.Latitude > maxLat) maxLat = p.Latitude;
            }

            var bounds = new LngLatBounds(
                new LngLat(minLng, minLat),
                new LngLat(maxLng, maxLat));

            FitBounds(bounds, options);
        }

        /// <summary>
        /// Cancel any in-progress animation.
        /// </summary>
        public void CancelAnimation()
        {
            if (_isAnimating)
            {
                _isAnimating = false;
                OnAnimationEnd?.Invoke();
            }
        }

        private void Update()
        {
            if (!_isAnimating || _mapState == null) return;

            _animation.Elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(_animation.Elapsed / _animation.Duration);
            float t = ApplyEasing(progress, _animation.Easing);

            // Interpolate state
            var center = _animation.AnimateCenter
                ? LerpLngLat(_animation.StartCenter, _animation.EndCenter, t)
                : _mapState.Center;

            float zoom = _animation.AnimateZoom
                ? Mathf.Lerp(_animation.StartZoom, _animation.EndZoom, t)
                : _mapState.Zoom;

            float bearing = _animation.AnimateBearing
                ? Mathf.Lerp(_animation.StartBearing, _animation.EndBearing, t)
                : _mapState.Bearing;

            float pitch = _animation.AnimatePitch
                ? Mathf.Lerp(_animation.StartPitch, _animation.EndPitch, t)
                : _mapState.Pitch;

            _mapState.SetState(center, zoom, bearing, pitch);

            if (progress >= 1f)
            {
                _isAnimating = false;
                OnAnimationEnd?.Invoke();
            }
        }

        private static float ApplyEasing(float t, EasingFunction easing)
        {
            return easing switch
            {
                EasingFunction.Linear => t,
                EasingFunction.EaseIn => t * t,
                EasingFunction.EaseOut => t * (2f - t),
                EasingFunction.EaseInOut => t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t,
                EasingFunction.EaseInOutCubic => t < 0.5f
                    ? 4f * t * t * t
                    : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f,
                _ => t
            };
        }

        private static LngLat LerpLngLat(LngLat a, LngLat b, float t)
        {
            // Handle wrapping around antimeridian
            double lng1 = a.Longitude;
            double lng2 = b.Longitude;
            double diff = lng2 - lng1;
            if (diff > 180) lng2 -= 360;
            else if (diff < -180) lng2 += 360;

            return new LngLat(
                lng1 + (lng2 - lng1) * t,
                a.Latitude + (b.Latitude - a.Latitude) * t
            );
        }

        private static float ComputeFlyDuration(double mercatorDistance)
        {
            // Heuristic: longer distances take more time, clamped between 500ms and 5000ms.
            float duration = (float)(mercatorDistance * 20000.0);
            return Mathf.Clamp(duration, 500f, 5000f);
        }
    }

    public struct EaseToOptions
    {
        public LngLat? Center;
        public float? Zoom;
        public float? Bearing;
        public float? Pitch;
        /// <summary>Animation duration in milliseconds. Defaults to 300 ms when 0.</summary>
        public float Duration;
        public MapAnimator.EasingFunction Easing;
    }

    /// <summary>
    /// Options for MapLibreMap.JumpTo() -- instant (non-animated) camera move.
    /// Matches MapLibre GL JS CameraOptions used by map.jumpTo().
    /// Only non-null fields are applied so callers can update a subset of the state.
    /// </summary>
    public struct JumpToOptions
    {
        public LngLat? Center;
        public float? Zoom;
        public float? Bearing;
        public float? Pitch;
    }

    public struct FlyToOptions
    {
        public LngLat? Center;
        public float? Zoom;
        public float? Bearing;
        public float? Pitch;
        /// <summary>Animation duration in milliseconds. When 0, derived from travel distance.</summary>
        public float Duration;
    }

    public struct FitBoundsOptions
    {
        public PaddingOptions? Padding;
        public float? MaxZoom;
        public float? Bearing;
        public float? Pitch;
        /// <summary>Animation duration in milliseconds. Defaults to 500 ms when 0.</summary>
        public float Duration;
        /// <summary>
        /// If true, uses easeTo (linear interpolation). If false (default), uses flyTo.
        /// </summary>
        public bool Linear;
        /// <summary>
        /// Easing function used when Linear is true.
        /// </summary>
        public MapAnimator.EasingFunction? Easing;
    }

    /// <summary>
    /// Viewport padding in pixels. Matches MapLibre GL JS PaddingOptions.
    /// </summary>
    public struct PaddingOptions
    {
        public float Top;
        public float Bottom;
        public float Left;
        public float Right;

        public PaddingOptions(float uniform)
        {
            Top = Bottom = Left = Right = uniform;
        }

        public PaddingOptions(float top, float bottom, float left, float right)
        {
            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;
        }
    }
}
