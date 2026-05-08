using System;
using System.IO;
using System.IO.Compression;
using MapLibre.Unity.Source.PMTiles;
using NUnit.Framework;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// PMTiles helpers we can test without spinning up the runtime: header
    /// parsing, Hilbert curve mapping, and directory varint decoding. The
    /// archive class itself talks to UnityWebRequest / FileStream which is
    /// covered separately by the PlayMode smoke tests.
    /// </summary>
    public class PMTilesTests
    {
        // === Hilbert curve ===

        [Test]
        public void HilbertCurve_Z0_AlwaysReturnsZero()
        {
            Assert.AreEqual(0UL, HilbertCurve.ZxyToTileId(0, 0, 0));
        }

        [Test]
        public void HilbertCurve_Z1_FourTiles_IndicesOneToFour()
        {
            // Spec section: tile id 0 is (z=0, 0, 0); the next 4 ids cover z=1.
            var ids = new[]
            {
                HilbertCurve.ZxyToTileId(1, 0, 0),
                HilbertCurve.ZxyToTileId(1, 1, 0),
                HilbertCurve.ZxyToTileId(1, 1, 1),
                HilbertCurve.ZxyToTileId(1, 0, 1),
            };

            // The four z=1 tiles must occupy ids 1-4 (in some order).
            // Sorting + comparing avoids depending on the curve traversal
            // order, which is an implementation detail.
            Array.Sort(ids);
            Assert.AreEqual(new ulong[] { 1, 2, 3, 4 }, ids);
        }

        [Test]
        public void HilbertCurve_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => HilbertCurve.ZxyToTileId(28, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => HilbertCurve.ZxyToTileId(2, 4, 0)); // x >= 2^z
        }

        // === Header ===

        [Test]
        public void Header_RejectsBadMagic()
        {
            var bytes = new byte[PMTilesHeader.HeaderSize];
            // First 7 bytes are not "PMTiles" -- should reject.
            Assert.Throws<InvalidOperationException>(() => PMTilesHeader.Parse(bytes));
        }

        [Test]
        public void Header_RejectsUnsupportedVersion()
        {
            var bytes = MakeMinimalHeader(version: 2);
            Assert.Throws<InvalidOperationException>(() => PMTilesHeader.Parse(bytes));
        }

        [Test]
        public void Header_ParsesValidV3()
        {
            var bytes = MakeMinimalHeader(version: 3);
            var h = PMTilesHeader.Parse(bytes);
            Assert.AreEqual(0u, h.MinZoom);
            Assert.AreEqual(14u, h.MaxZoom);
            Assert.AreEqual(PMTilesType.Mvt, h.TileType);
            Assert.AreEqual(PMTilesCompression.Gzip, h.InternalCompression);
            // Bounds round-trip via the E7 fixed-point representation.
            Assert.AreEqual(-180.0, h.MinLon, 1e-6);
            Assert.AreEqual(85.0, h.MaxLat, 1e-6);
        }

        // Build a minimum-viable v3 header that only sets the fields the
        // tests above check. Other bytes stay zero.
        private static byte[] MakeMinimalHeader(byte version)
        {
            var b = new byte[PMTilesHeader.HeaderSize];
            // Magic "PMTiles" then version byte.
            b[0] = (byte)'P'; b[1] = (byte)'M'; b[2] = (byte)'T'; b[3] = (byte)'i';
            b[4] = (byte)'l'; b[5] = (byte)'e'; b[6] = (byte)'s';
            b[7] = version;
            // InternalCompression / TileCompression = Gzip; TileType = MVT;
            // minzoom 0, maxzoom 14.
            b[97]  = (byte)PMTilesCompression.Gzip;
            b[98]  = (byte)PMTilesCompression.Gzip;
            b[99]  = (byte)PMTilesType.Mvt;
            b[100] = 0;
            b[101] = 14;
            // Bounds: -180,-85,180,85 in E7.
            WriteInt32(b, 102, -180_0000000);
            WriteInt32(b, 106, -85_0000000);
            WriteInt32(b, 110, 180_0000000);
            WriteInt32(b, 114, 85_0000000);
            return b;
        }

        private static void WriteInt32(byte[] b, int o, int v)
        {
            b[o]     = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
            b[o + 2] = (byte)((v >> 16) & 0xFF);
            b[o + 3] = (byte)((v >> 24) & 0xFF);
        }

        // === Directory varint decoding ===

        [Test]
        public void Directory_DecodesSingleTileEntry()
        {
            // Build one directory entry for a single tile:
            //   tile_id = 5, run_length = 1, length = 100, offset = 0
            // Encoded as four parallel varint streams of length 1.
            var ms = new MemoryStream();
            WriteVarUInt(ms, 1);    // entry count
            WriteVarUInt(ms, 5);    // tile_id delta
            WriteVarUInt(ms, 1);    // run_length
            WriteVarUInt(ms, 100);  // length
            WriteVarUInt(ms, 1);    // offset diff (= absolute offset 0 + 1)
            byte[] raw = ms.ToArray();

            // Compress with gzip so we exercise the decompression path too.
            byte[] gzipped;
            using (var gz = new MemoryStream())
            {
                using (var stream = new GZipStream(gz, CompressionLevel.Fastest))
                    stream.Write(raw, 0, raw.Length);
                gzipped = gz.ToArray();
            }

            var entries = PMTilesDirectory.Decode(gzipped, PMTilesCompression.Gzip);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(5UL, entries[0].TileId);
            Assert.AreEqual(0UL, entries[0].Offset);
            Assert.AreEqual(100u, entries[0].Length);
            Assert.AreEqual(1u, entries[0].RunLength);
            Assert.IsFalse(entries[0].IsLeaf);
        }

        [Test]
        public void Directory_TryFindEntry_HitsRunLengthRange()
        {
            // One entry that covers tile_ids [10, 12] inclusive.
            var entries = new System.Collections.Generic.List<PMTilesEntry>
            {
                new PMTilesEntry(tileId: 10, offset: 0, length: 100, runLength: 3),
            };
            Assert.IsTrue(PMTilesDirectory.TryFindEntry(entries, 10, out _));
            Assert.IsTrue(PMTilesDirectory.TryFindEntry(entries, 11, out _));
            Assert.IsTrue(PMTilesDirectory.TryFindEntry(entries, 12, out _));
            Assert.IsFalse(PMTilesDirectory.TryFindEntry(entries, 13, out _),
                "tile 13 is past the run end");
            Assert.IsFalse(PMTilesDirectory.TryFindEntry(entries, 9, out _),
                "tile 9 is before the run start");
        }

        private static void WriteVarUInt(MemoryStream ms, ulong value)
        {
            while (value >= 0x80)
            {
                ms.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            ms.WriteByte((byte)value);
        }
    }
}
