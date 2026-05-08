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

        // Bit-exact equality so the Equals/GetHashCode contract holds. Use
        // <see cref="ApproxEquals"/> when comparing values produced by
        // floating-point math.
        public bool Equals(MercatorCoordinate other) =>
            X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj) => obj is MercatorCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"Mercator({X:F10}, {Y:F10})";

        /// <summary>
        /// Component-wise comparison with a tolerance. Use this for comparing
        /// coordinates that came out of floating-point math.
        /// </summary>
        public bool ApproxEquals(MercatorCoordinate other, double tolerance = 1e-15) =>
            Math.Abs(X - other.X) < tolerance &&
            Math.Abs(Y - other.Y) < tolerance;
    }
}
