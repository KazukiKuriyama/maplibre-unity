using System;

namespace MapLibre.Unity
{
    [Serializable]
    public struct LngLat : IEquatable<LngLat>
    {
        public double Longitude;
        public double Latitude;

        public LngLat(double longitude, double latitude)
        {
            Longitude = longitude;
            Latitude = latitude;
        }

        public LngLat Wrap()
        {
            double lng = Longitude;
            while (lng > 180.0) lng -= 360.0;
            while (lng < -180.0) lng += 360.0;
            return new LngLat(lng, Math.Clamp(Latitude, -MaxLatitude, MaxLatitude));
        }

        private const double MaxLatitude = MapConstants.MaxLatitude;

        public bool Equals(LngLat other) =>
            Math.Abs(Longitude - other.Longitude) < 1e-10 &&
            Math.Abs(Latitude - other.Latitude) < 1e-10;

        public override bool Equals(object obj) => obj is LngLat other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Longitude, Latitude);
        public override string ToString() => $"LngLat({Longitude:F6}, {Latitude:F6})";

        public static bool operator ==(LngLat a, LngLat b) => a.Equals(b);
        public static bool operator !=(LngLat a, LngLat b) => !a.Equals(b);
    }
}
