using System;

namespace MapLibre.Unity.Source.PMTiles
{
    /// <summary>
    /// Compression scheme used for either the directory blocks or the tile
    /// payloads. PMTiles v3 spec section 2.4. Most archives use Gzip for
    /// directories; tile compression depends on TileType (vector tiles use
    /// Gzip, raster usually None because PNG/JPEG are already compressed).
    /// </summary>
    public enum PMTilesCompression : byte
    {
        Unknown = 0,
        None = 1,
        Gzip = 2,
        Brotli = 3,
        Zstd = 4,
    }

    /// <summary>
    /// PMTiles v3 tile content type. We treat MVT as a vector source and the
    /// raster types (PNG / JPEG / WebP / AVIF) as raster sources. Brotli /
    /// AVIF are accepted in metadata but the renderer can only decode formats
    /// Unity's Texture2D supports (PNG / JPEG; WebP only with packages).
    /// </summary>
    public enum PMTilesType : byte
    {
        Unknown = 0,
        Mvt = 1,
        Png = 2,
        Jpeg = 3,
        Webp = 4,
        Avif = 5,
    }

    /// <summary>
    /// Fixed 127-byte header at the start of every PMTiles archive. Stores
    /// pointers into the rest of the file so a single 127-byte HTTP Range
    /// request locates every other piece (root dir, JSON metadata, leaf dirs,
    /// tile data). Spec: https://github.com/protomaps/PMTiles/blob/main/spec/v3/spec.md
    /// </summary>
    public sealed class PMTilesHeader
    {
        /// <summary>Total header length in bytes -- fixed by the spec.</summary>
        public const int HeaderSize = 127;

        public ulong RootDirOffset;
        public ulong RootDirLength;
        public ulong JsonMetadataOffset;
        public ulong JsonMetadataLength;
        public ulong LeafDirsOffset;
        public ulong LeafDirsLength;
        public ulong TileDataOffset;
        public ulong TileDataLength;
        public ulong AddressedTilesCount;
        public ulong TileEntriesCount;
        public ulong TileContentsCount;
        public bool Clustered;
        public PMTilesCompression InternalCompression;
        public PMTilesCompression TileCompression;
        public PMTilesType TileType;
        public byte MinZoom;
        public byte MaxZoom;
        // Bounds and centre are stored as fixed-point E7 (degrees * 1e7).
        // We keep the raw int form so callers can compare-without-loss
        // and provide convenience properties for degrees.
        public int MinLonE7;
        public int MinLatE7;
        public int MaxLonE7;
        public int MaxLatE7;
        public byte CenterZoom;
        public int CenterLonE7;
        public int CenterLatE7;

        public double MinLon => MinLonE7 / 1e7;
        public double MinLat => MinLatE7 / 1e7;
        public double MaxLon => MaxLonE7 / 1e7;
        public double MaxLat => MaxLatE7 / 1e7;
        public double CenterLon => CenterLonE7 / 1e7;
        public double CenterLat => CenterLatE7 / 1e7;

        /// <summary>
        /// Parse a 127-byte header buffer. Throws when the magic / version
        /// don't match a PMTiles v3 archive -- older versions use a different
        /// header layout and would silently produce garbage offsets.
        /// </summary>
        public static PMTilesHeader Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderSize)
                throw new ArgumentException(
                    $"PMTiles header requires {HeaderSize} bytes, got {bytes?.Length ?? 0}");

            // Spec section 2.1: magic "PMTiles" then 1-byte version.
            if (bytes[0] != (byte)'P' || bytes[1] != (byte)'M'
                || bytes[2] != (byte)'T' || bytes[3] != (byte)'i'
                || bytes[4] != (byte)'l' || bytes[5] != (byte)'e'
                || bytes[6] != (byte)'s')
                throw new InvalidOperationException("Not a PMTiles archive (magic mismatch)");
            if (bytes[7] != 3)
                throw new InvalidOperationException(
                    $"Unsupported PMTiles version {bytes[7]} -- only v3 is supported");

            var h = new PMTilesHeader
            {
                RootDirOffset       = ReadUInt64(bytes, 8),
                RootDirLength       = ReadUInt64(bytes, 16),
                JsonMetadataOffset  = ReadUInt64(bytes, 24),
                JsonMetadataLength  = ReadUInt64(bytes, 32),
                LeafDirsOffset      = ReadUInt64(bytes, 40),
                LeafDirsLength      = ReadUInt64(bytes, 48),
                TileDataOffset      = ReadUInt64(bytes, 56),
                TileDataLength      = ReadUInt64(bytes, 64),
                AddressedTilesCount = ReadUInt64(bytes, 72),
                TileEntriesCount    = ReadUInt64(bytes, 80),
                TileContentsCount   = ReadUInt64(bytes, 88),
                Clustered           = bytes[96] != 0,
                InternalCompression = (PMTilesCompression)bytes[97],
                TileCompression     = (PMTilesCompression)bytes[98],
                TileType            = (PMTilesType)bytes[99],
                MinZoom             = bytes[100],
                MaxZoom             = bytes[101],
                MinLonE7            = ReadInt32(bytes, 102),
                MinLatE7            = ReadInt32(bytes, 106),
                MaxLonE7            = ReadInt32(bytes, 110),
                MaxLatE7            = ReadInt32(bytes, 114),
                CenterZoom          = bytes[118],
                CenterLonE7         = ReadInt32(bytes, 119),
                CenterLatE7         = ReadInt32(bytes, 123),
            };
            return h;
        }

        // PMTiles is little-endian everywhere. BitConverter is host-endian on
        // most desktop targets but spec compliance demands explicit LE reads.
        private static ulong ReadUInt64(byte[] b, int o) =>
            (ulong)b[o] |
            ((ulong)b[o + 1] << 8) |
            ((ulong)b[o + 2] << 16) |
            ((ulong)b[o + 3] << 24) |
            ((ulong)b[o + 4] << 32) |
            ((ulong)b[o + 5] << 40) |
            ((ulong)b[o + 6] << 48) |
            ((ulong)b[o + 7] << 56);

        private static int ReadInt32(byte[] b, int o) =>
            b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    }
}
