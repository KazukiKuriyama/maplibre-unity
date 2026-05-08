using System;

namespace MapLibre.Unity.Source.PMTiles
{
    /// <summary>
    /// PMTiles v3 maps (z, x, y) tile coordinates onto a single 64-bit
    /// Hilbert-curve index. Hilbert ordering is what makes the directory
    /// blocks compact: tiles that are close on the map land close in the
    /// index, so a single contiguous run in the directory matches a square
    /// region of the map.
    ///
    /// <para>
    /// Reference implementation: https://github.com/protomaps/PMTiles/blob/main/spec/v3/spec.md#tile-id-calculation
    /// </para>
    /// </summary>
    public static class HilbertCurve
    {
        /// <summary>
        /// Convert a (z, x, y) tile coordinate to its PMTiles tile id.
        /// Throws for z &gt; 27 -- the spec caps zoom there because higher
        /// values would require &gt; 64-bit ids.
        /// </summary>
        public static ulong ZxyToTileId(int z, uint x, uint y)
        {
            if (z < 0 || z > 27)
                throw new ArgumentOutOfRangeException(nameof(z),
                    "PMTiles zoom must be in [0, 27]");
            if (x >= (1u << z) || y >= (1u << z))
                throw new ArgumentOutOfRangeException(
                    "Tile (x, y) is out of range for the supplied zoom");

            // Tiles in zooms below z each contribute (4^t_z) ids before the
            // current zoom block starts. Sum is a closed form ((4^z) - 1) / 3.
            ulong acc = 0;
            for (int tz = 0; tz < z; tz++)
                acc += (1UL << tz) * (1UL << tz);

            // Standard Hilbert d2xy/xy2d translation, adapted from Wikipedia
            // and the protomaps reference. 'd' here is the offset within
            // this zoom level; we add it to the zoom-block base above.
            uint n = 1u << z;
            ulong d = 0;
            uint tx = x;
            uint ty = y;
            for (uint s = n / 2; s > 0; s /= 2)
            {
                uint rx = (tx & s) > 0 ? 1u : 0u;
                uint ry = (ty & s) > 0 ? 1u : 0u;
                d += (ulong)s * s * ((3u * rx) ^ ry);
                if (ry == 0)
                {
                    if (rx == 1)
                    {
                        tx = s - 1 - tx;
                        ty = s - 1 - ty;
                    }
                    (tx, ty) = (ty, tx);
                }
            }
            return acc + d;
        }
    }
}
