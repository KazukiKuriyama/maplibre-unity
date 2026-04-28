using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MapLibre.Unity.Source.PMTiles
{
    /// <summary>
    /// One PMTiles directory entry. <see cref="RunLength"/> = 0 marks a leaf
    /// directory pointer instead of a tile pointer; the <see cref="Offset"/>
    /// / <see cref="Length"/> in that case point into the leaf-directory
    /// section rather than the tile-data section.
    /// </summary>
    public readonly struct PMTilesEntry
    {
        public readonly ulong TileId;
        public readonly ulong Offset;
        public readonly uint Length;
        public readonly uint RunLength;

        public PMTilesEntry(ulong tileId, ulong offset, uint length, uint runLength)
        {
            TileId = tileId;
            Offset = offset;
            Length = length;
            RunLength = runLength;
        }

        public bool IsLeaf => RunLength == 0;
    }

    /// <summary>
    /// Decoder for a PMTiles directory block. The block on disk is
    /// gzip-compressed (typical) or raw, and contains four parallel
    /// varint-encoded streams:
    /// <list type="number">
    ///   <item>tile_id deltas (cumulative)</item>
    ///   <item>run lengths</item>
    ///   <item>lengths</item>
    ///   <item>offset diffs (0 = "previous offset + previous length")</item>
    /// </list>
    /// Spec section 2.3.
    /// </summary>
    public static class PMTilesDirectory
    {
        public static List<PMTilesEntry> Decode(byte[] raw, PMTilesCompression compression)
        {
            var data = Decompress(raw, compression);
            int pos = 0;
            ulong n = ReadVarUInt(data, ref pos);
            var entries = new List<PMTilesEntry>((int)n);

            // 1) tile_id deltas → absolute tile ids (cumulative sum).
            var tileIds = new ulong[n];
            ulong tid = 0;
            for (ulong i = 0; i < n; i++)
            {
                tid += ReadVarUInt(data, ref pos);
                tileIds[i] = tid;
            }

            // 2) run lengths.
            var runLengths = new uint[n];
            for (ulong i = 0; i < n; i++)
                runLengths[i] = (uint)ReadVarUInt(data, ref pos);

            // 3) lengths.
            var lengths = new uint[n];
            for (ulong i = 0; i < n; i++)
                lengths[i] = (uint)ReadVarUInt(data, ref pos);

            // 4) offset diffs. Spec rule: when the diff is 0, the entry is
            //    contiguous with the previous one (offset = prev offset +
            //    prev length). Otherwise, the stored value is offset + 1.
            var offsets = new ulong[n];
            for (ulong i = 0; i < n; i++)
            {
                ulong d = ReadVarUInt(data, ref pos);
                if (d == 0 && i > 0)
                    offsets[i] = offsets[i - 1] + lengths[i - 1];
                else
                    offsets[i] = d - 1;
            }

            for (ulong i = 0; i < n; i++)
                entries.Add(new PMTilesEntry(tileIds[i], offsets[i], lengths[i], runLengths[i]));
            return entries;
        }

        /// <summary>
        /// Find the directory entry that addresses <paramref name="tileId"/>.
        /// Entries are stored sorted by TileId so a binary search runs in
        /// O(log n). Run-length entries cover a half-open range
        /// [TileId, TileId+RunLength).
        /// </summary>
        public static bool TryFindEntry(IReadOnlyList<PMTilesEntry> entries,
            ulong tileId, out PMTilesEntry entry)
        {
            int lo = 0;
            int hi = entries.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var e = entries[mid];
                if (tileId < e.TileId) hi = mid - 1;
                else if (e.RunLength == 0)
                {
                    // Leaf pointer covers tile ids >= e.TileId until the next
                    // entry's TileId. Walk forward to confirm the leaf claim.
                    if (mid + 1 < entries.Count && tileId >= entries[mid + 1].TileId)
                        lo = mid + 1;
                    else { entry = e; return true; }
                }
                else if (tileId < e.TileId + e.RunLength)
                {
                    entry = e;
                    return true;
                }
                else lo = mid + 1;
            }
            entry = default;
            return false;
        }

        // Standard protobuf-style varint. PMTiles caps values at 64 bits.
        private static ulong ReadVarUInt(byte[] b, ref int pos)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                if (pos >= b.Length)
                    throw new InvalidDataException("Truncated varint in PMTiles directory");
                byte cur = b[pos++];
                result |= (ulong)(cur & 0x7F) << shift;
                if ((cur & 0x80) == 0) return result;
                shift += 7;
                if (shift >= 64)
                    throw new InvalidDataException("Varint too long in PMTiles directory");
            }
        }

        private static byte[] Decompress(byte[] data, PMTilesCompression compression)
        {
            if (data == null || data.Length == 0) return data;
            switch (compression)
            {
                case PMTilesCompression.None:
                case PMTilesCompression.Unknown:
                    return data;
                case PMTilesCompression.Gzip:
                    using (var ms = new MemoryStream(data))
                    using (var gz = new GZipStream(ms, CompressionMode.Decompress))
                    using (var dst = new MemoryStream())
                    {
                        gz.CopyTo(dst);
                        return dst.ToArray();
                    }
                default:
                    throw new NotSupportedException(
                        $"PMTiles compression {compression} is not supported (only None / Gzip)");
            }
        }
    }
}
