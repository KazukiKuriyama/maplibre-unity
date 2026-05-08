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

        // Bit-exact equality so the Equals/GetHashCode contract holds (a value
        // 1e-12 apart from another would otherwise compare equal but hash
        // differently, breaking HashSet<LngLat> / Dictionary keys). Use
        // <see cref="ApproxEquals"/> when comparing values produced by
        // floating-point math (e.g. Mercator round-trips).
        public bool Equals(LngLat other) =>
            Longitude.Equals(other.Longitude) && Latitude.Equals(other.Latitude);

        public override bool Equals(object obj) => obj is LngLat other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Longitude, Latitude);
        public override string ToString() => $"LngLat({Longitude:F6}, {Latitude:F6})";

        /// <summary>
        /// Component-wise comparison with a tolerance. Use this for comparing
        /// coordinates that came out of floating-point math; for hash-keyed
        /// collections use <see cref="Equals(LngLat)"/> / <c>==</c> instead.
        /// </summary>
        public bool ApproxEquals(LngLat other, double tolerance = 1e-10) =>
            Math.Abs(Longitude - other.Longitude) < tolerance &&
            Math.Abs(Latitude - other.Latitude) < tolerance;

        public static bool operator ==(LngLat a, LngLat b) => a.Equals(b);
        public static bool operator !=(LngLat a, LngLat b) => !a.Equals(b);
    }
}
