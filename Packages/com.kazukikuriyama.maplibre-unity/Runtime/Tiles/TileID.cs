using System;

namespace MapLibre.Unity
{
    /// <summary>
    /// Immutable canonical tile identifier (XYZ/Slippy Map convention).
    /// Z = zoom level, X = column (0 = west), Y = row (0 = north).
    /// </summary>
    public readonly struct CanonicalTileID : IEquatable<CanonicalTileID>
    {
        public readonly int Z;
        public readonly int X;
        public readonly int Y;

        public CanonicalTileID(int z, int x, int y)
        {
            Z = z;
            X = x;
            Y = y;
        }

        public bool IsValid()
        {
            int max = 1 << Z;
            return X >= 0 && X < max && Y >= 0 && Y < max && Z >= 0;
        }

        public string ToUrl(string urlTemplate)
        {
            return urlTemplate
                .Replace("{z}", Z.ToString())
                .Replace("{x}", X.ToString())
                .Replace("{y}", Y.ToString());
        }

        public CanonicalTileID Parent()
        {
            return new CanonicalTileID(Z - 1, X >> 1, Y >> 1);
        }

        public CanonicalTileID[] Children()
        {
            int cz = Z + 1;
            int cx = X * 2;
            int cy = Y * 2;
            return new[]
            {
                new CanonicalTileID(cz, cx, cy),
                new CanonicalTileID(cz, cx + 1, cy),
                new CanonicalTileID(cz, cx, cy + 1),
                new CanonicalTileID(cz, cx + 1, cy + 1)
            };
        }

        public bool Equals(CanonicalTileID other) => Z == other.Z && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is CanonicalTileID other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Z, X, Y);
        public override string ToString() => $"{Z}/{X}/{Y}";

        public static bool operator ==(CanonicalTileID a, CanonicalTileID b) => a.Equals(b);
        public static bool operator !=(CanonicalTileID a, CanonicalTileID b) => !a.Equals(b);
    }

    /// <summary>
    /// Overscaled tile ID for LOD management. Tracks logical zoom separately
    /// from actual tile data zoom.
    /// </summary>
    public readonly struct OverscaledTileID : IEquatable<OverscaledTileID>
    {
        public readonly int OverscaledZ;
        public readonly int Wrap;
        public readonly CanonicalTileID Canonical;

        public OverscaledTileID(int overscaledZ, int wrap, CanonicalTileID canonical)
        {
            OverscaledZ = overscaledZ;
            Wrap = wrap;
            Canonical = canonical;
        }

        public bool Equals(OverscaledTileID other) =>
            OverscaledZ == other.OverscaledZ && Wrap == other.Wrap && Canonical.Equals(other.Canonical);

        public override bool Equals(object obj) => obj is OverscaledTileID other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(OverscaledZ, Wrap, Canonical);
        public override string ToString() => $"{OverscaledZ}/{Canonical} (wrap:{Wrap})";
    }
}
