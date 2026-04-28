using System;

namespace MapLibre.Unity
{
    public struct MercatorCoordinate : IEquatable<MercatorCoordinate>
    {
        public double X;
        public double Y;

        public MercatorCoordinate(double x, double y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(MercatorCoordinate other) =>
            Math.Abs(X - other.X) < 1e-15 &&
            Math.Abs(Y - other.Y) < 1e-15;

        public override bool Equals(object obj) => obj is MercatorCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"Mercator({X:F10}, {Y:F10})";
    }
}
