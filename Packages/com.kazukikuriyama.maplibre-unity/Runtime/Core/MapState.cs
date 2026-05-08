using System;

namespace MapLibre.Unity
{
    /// <summary>
    /// Flags indicating which state properties changed in the last update.
    /// </summary>
    [Flags]
    public enum StateChangeFlags
    {
        None = 0,
        Center = 1 << 0,
        Zoom = 1 << 1,
        Bearing = 1 << 2,
        Pitch = 1 << 3,
    }

    [Serializable]
    public class MapState
    {
        public event Action OnStateChanged;

        /// <summary>
        /// Hard upper bound for pitch in degrees. Matches MapLibre GL JS' maximum
        /// settable maxPitch (85°) so the camera can tilt close to the horizon
        /// for 3D terrain views. LOD tile selection keeps the tile count bounded
        /// at high pitch -- without LOD the horizon would demand thousands of tiles.
        /// </summary>
        public const float MaxPitchHardLimit = 85f;

        private LngLat _center;
        private float _zoom;
        private float _bearing;
        private float _pitch;
        private float _minZoom = MapConstants.MinZoom;
        private float _maxZoom = MapConstants.MaxZoom;
        private float _minPitch;
        private float _maxPitch = MaxPitchHardLimit;
        private LngLatBounds? _maxBounds;

        /// <summary>
        /// Flags indicating which properties changed in the most recent update.
        /// Reset each time before a new change is applied.
        /// </summary>
        public StateChangeFlags LastChangeFlags { get; private set; }

        /// <summary>
        /// Lower zoom bound applied to all programmatic and user-driven zoom changes.
        /// Matches map.setMinZoom in MapLibre GL JS. Default 0.
        /// </summary>
        public float MinZoom
        {
            get => _minZoom;
            set
            {
                _minZoom = Math.Clamp(value, MapConstants.MinZoom, _maxZoom);
                if (_zoom < _minZoom) Zoom = _minZoom;
            }
        }

        /// <summary>
        /// Upper zoom bound. Matches map.setMaxZoom. Default 22.
        /// </summary>
        public float MaxZoom
        {
            get => _maxZoom;
            set
            {
                _maxZoom = Math.Clamp(value, _minZoom, MapConstants.MaxZoom);
                if (_zoom > _maxZoom) Zoom = _maxZoom;
            }
        }

        /// <summary>
        /// Lower pitch bound in degrees. Matches map.setMinPitch. Default 0.
        /// </summary>
        public float MinPitch
        {
            get => _minPitch;
            set
            {
                _minPitch = Math.Clamp(value, 0f, _maxPitch);
                if (_pitch < _minPitch) Pitch = _minPitch;
            }
        }

        /// <summary>
        /// Upper pitch bound. Matches map.setMaxPitch. Capped at <see cref="MaxPitchHardLimit"/>.
        /// </summary>
        public float MaxPitch
        {
            get => _maxPitch;
            set
            {
                _maxPitch = Math.Clamp(value, _minPitch, MaxPitchHardLimit);
                if (_pitch > _maxPitch) Pitch = _maxPitch;
            }
        }

        /// <summary>
        /// Optional pan bounds. When set, the camera center is clamped inside this
        /// rectangle on every update. Matches map.setMaxBounds.
        /// </summary>
        public LngLatBounds? MaxBounds
        {
            get => _maxBounds;
            set
            {
                _maxBounds = value;
                if (value.HasValue) Center = ClampToBounds(_center, value.Value);
            }
        }

        public LngLat Center
        {
            get => _center;
            set
            {
                var clamped = _maxBounds.HasValue
                    ? ClampToBounds(value, _maxBounds.Value) : value;
                if (_center.Longitude == clamped.Longitude && _center.Latitude == clamped.Latitude) return;
                _center = clamped;
                NotifyChanged(StateChangeFlags.Center);
            }
        }

        public float Zoom
        {
            get => _zoom;
            set
            {
                float clamped = Math.Clamp(value, _minZoom, _maxZoom);
                if (Math.Abs(_zoom - clamped) < 1e-6f) return;
                _zoom = clamped;
                NotifyChanged(StateChangeFlags.Zoom);
            }
        }

        public float Bearing
        {
            get => _bearing;
            set
            {
                float normalized = ((value % 360f) + 360f) % 360f;
                if (Math.Abs(_bearing - normalized) < 1e-6f) return;
                _bearing = normalized;
                NotifyChanged(StateChangeFlags.Bearing);
            }
        }

        public float Pitch
        {
            get => _pitch;
            set
            {
                float clamped = Math.Clamp(value, _minPitch, _maxPitch);
                if (Math.Abs(_pitch - clamped) < 1e-6f) return;
                _pitch = clamped;
                NotifyChanged(StateChangeFlags.Pitch);
            }
        }

        public int IntegerZoom => (int)Math.Floor(_zoom);

        public void SetState(LngLat center, float zoom, float bearing = 0f, float pitch = 0f)
        {
            var flags = StateChangeFlags.None;

            var clampedCenter = _maxBounds.HasValue
                ? ClampToBounds(center, _maxBounds.Value) : center;
            if (_center.Longitude != clampedCenter.Longitude || _center.Latitude != clampedCenter.Latitude)
                flags |= StateChangeFlags.Center;
            _center = clampedCenter;

            float clampedZoom = Math.Clamp(zoom, _minZoom, _maxZoom);
            if (Math.Abs(_zoom - clampedZoom) >= 1e-6f)
                flags |= StateChangeFlags.Zoom;
            _zoom = clampedZoom;

            float normalizedBearing = ((bearing % 360f) + 360f) % 360f;
            if (Math.Abs(_bearing - normalizedBearing) >= 1e-6f)
                flags |= StateChangeFlags.Bearing;
            _bearing = normalizedBearing;

            float clampedPitch = Math.Clamp(pitch, _minPitch, _maxPitch);
            if (Math.Abs(_pitch - clampedPitch) >= 1e-6f)
                flags |= StateChangeFlags.Pitch;
            _pitch = clampedPitch;

            if (flags != StateChangeFlags.None)
                NotifyChanged(flags);
        }

        private void NotifyChanged(StateChangeFlags flags)
        {
            LastChangeFlags = flags;
            OnStateChanged?.Invoke();
        }

        private static LngLat ClampToBounds(LngLat point, LngLatBounds bounds)
        {
            double lng = Math.Clamp(point.Longitude,
                bounds.SouthWest.Longitude, bounds.NorthEast.Longitude);
            double lat = Math.Clamp(point.Latitude,
                bounds.SouthWest.Latitude, bounds.NorthEast.Latitude);
            return new LngLat(lng, lat);
        }
    }
}
