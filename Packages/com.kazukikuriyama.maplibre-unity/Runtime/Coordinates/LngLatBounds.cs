using System;

namespace MapLibre.Unity
{
    /// <summary>
    /// Geographic bounding box defined by southwest and northeast corners.
    /// Matches MapLibre GL JS LngLatBounds.
    /// </summary>
    public struct LngLatBounds : IEquatable<LngLatBounds>
    {
        public LngLat SouthWest;
        public LngLat NorthEast;

        public LngLatBounds(LngLat southWest, LngLat northEast)
        {
            SouthWest = southWest;
            NorthEast = northEast;
        }

        /// <summary>
        /// Create bounds from two corners (order-independent).
        /// The constructor normalizes so SouthWest has the smaller lat/lng.
        /// </summary>
        public static LngLatBounds FromCorners(LngLat a, LngLat b)
        {
            return new LngLatBounds(
                new LngLat(Math.Min(a.Longitude, b.Longitude), Math.Min(a.Latitude, b.Latitude)),
                new LngLat(Math.Max(a.Longitude, b.Longitude), Math.Max(a.Latitude, b.Latitude))
            );
        }

        /// <summary>
        /// Create bounds that contain all given points.
        /// </summary>
        public static LngLatBounds FromPoints(params LngLat[] points)
        {
            if (points == null || points.Length == 0)
                throw new ArgumentException("At least one point is required", nameof(points));

            double minLng = double.MaxValue, minLat = double.MaxValue;
            double maxLng = double.MinValue, maxLat = double.MinValue;

            foreach (var p in points)
            {
                if (p.Longitude < minLng) minLng = p.Longitude;
                if (p.Longitude > maxLng) maxLng = p.Longitude;
                if (p.Latitude < minLat) minLat = p.Latitude;
                if (p.Latitude > maxLat) maxLat = p.Latitude;
            }

            return new LngLatBounds(
                new LngLat(minLng, minLat),
                new LngLat(maxLng, maxLat));
        }

        /// <summary>
        /// Extend bounds to include a point.
        /// </summary>
        public LngLatBounds Extend(LngLat point)
        {
            return new LngLatBounds(
                new LngLat(
                    Math.Min(SouthWest.Longitude, point.Longitude),
                    Math.Min(SouthWest.Latitude, point.Latitude)),
                new LngLat(
                    Math.Max(NorthEast.Longitude, point.Longitude),
                    Math.Max(NorthEast.Latitude, point.Latitude)));
        }

        /// <summary>
        /// Geographic center of the bounds.
        /// </summary>
        public LngLat Center
        {
            get
            {
                // Compute center in Mercator space for latitude accuracy
                var swMerc = CoordinateConversion.LngLatToMercator(SouthWest);
                var neMerc = CoordinateConversion.LngLatToMercator(NorthEast);
                var centerMerc = new MercatorCoordinate(
                    (swMerc.X + neMerc.X) * 0.5,
                    (swMerc.Y + neMerc.Y) * 0.5);
                return CoordinateConversion.MercatorToLngLat(centerMerc);
            }
        }

        public bool IsEmpty => SouthWest == NorthEast;

        /// <summary>
        /// Serialize as [[west, south], [east, north]] matching MapLibre GL JS
        /// LngLatBounds.toArray().
        /// </summary>
        public double[][] ToArray() => new[]
        {
            new[] { SouthWest.Longitude, SouthWest.Latitude },
            new[] { NorthEast.Longitude, NorthEast.Latitude }
        };

        public bool Contains(LngLat point)
        {
            return point.Longitude >= SouthWest.Longitude &&
                   point.Longitude <= NorthEast.Longitude &&
                   point.Latitude >= SouthWest.Latitude &&
                   point.Latitude <= NorthEast.Latitude;
        }

        public bool Equals(LngLatBounds other) =>
            SouthWest == other.SouthWest && NorthEast == other.NorthEast;

        public override bool Equals(object obj) => obj is LngLatBounds other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SouthWest, NorthEast);
        public override string ToString() =>
            $"LngLatBounds(SW:{SouthWest}, NE:{NorthEast})";

        public static bool operator ==(LngLatBounds a, LngLatBounds b) => a.Equals(b);
        public static bool operator !=(LngLatBounds a, LngLatBounds b) => !a.Equals(b);
    }
}
