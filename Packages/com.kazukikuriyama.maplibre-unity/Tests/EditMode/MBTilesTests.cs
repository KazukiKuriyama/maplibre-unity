using System.Collections.Generic;
using MapLibre.Unity.Source.MBTiles;
using NUnit.Framework;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Verify the MBTiles helpers without touching SQLite. We swap a stub
    /// backend in and assert that <see cref="MBTilesReader"/> performs the
    /// TMS → XYZ y-coordinate flip and surfaces metadata correctly. The
    /// actual SQLite binding is up to the application -- the renderer only
    /// depends on the <see cref="IMBTilesBackend"/> interface.
    /// </summary>
    public class MBTilesTests
    {
        // === Metadata parsing ===

        [Test]
        public void Metadata_ParsesStandardKeys()
        {
            var raw = new Dictionary<string, string>
            {
                ["name"] = "Test Map",
                ["format"] = "pbf",
                ["attribution"] = "© Demo",
                ["minzoom"] = "0",
                ["maxzoom"] = "14",
                ["bounds"] = "-180,-85,180,85",
            };
            var meta = MBTilesMetadata.Parse(raw);
            Assert.AreEqual("Test Map", meta.Name);
            Assert.AreEqual("pbf", meta.Format);
            Assert.IsTrue(meta.IsVector);
            Assert.AreEqual(0, meta.MinZoom);
            Assert.AreEqual(14, meta.MaxZoom);
            Assert.AreEqual(-180, meta.Bounds[0], 0.001);
            Assert.AreEqual(85, meta.Bounds[3], 0.001);
        }

        [Test]
        public void Metadata_PreservesUnknownKeys()
        {
            var raw = new Dictionary<string, string>
            {
                ["name"] = "X",
                ["custom_key"] = "custom_value",
            };
            var meta = MBTilesMetadata.Parse(raw);
            Assert.AreEqual("custom_value", meta.Raw["custom_key"]);
        }

        [Test]
        public void Metadata_DefaultsWhenSilent()
        {
            var meta = MBTilesMetadata.Parse(new Dictionary<string, string>());
            Assert.AreEqual(0, meta.MinZoom);
            Assert.AreEqual(22, meta.MaxZoom, "MaxZoom should default to 22");
            Assert.IsNull(meta.Bounds);
        }

        // === TMS → XYZ translation ===

        [Test]
        public void Reader_FlipsTmsYToXyz()
        {
            // At z=2 (4×4 tiles), XYZ (0,0) is top-left. MBTiles stores tiles
            // with TMS origin at bottom-left, so XYZ y=0 should query
            // tile_row = (2^z - 1) - 0 = 3.
            var backend = new StubBackend
            {
                Metadata = new Dictionary<string, string> { ["minzoom"] = "0", ["maxzoom"] = "5" },
                Tiles = new Dictionary<(int, int, int), byte[]>
                {
                    { (2, 0, 3), new byte[] { 1, 2, 3 } },
                },
            };
            using var reader = new MBTilesReader(backend);
            reader.Open("ignored.mbtiles");

            var bytes = reader.GetTile(2, 0, 0);
            Assert.IsNotNull(bytes);
            Assert.AreEqual(3, bytes.Length, "Stub returned the row keyed by TMS y=3");
        }

        [Test]
        public void Reader_ReturnsNullForMissingTile()
        {
            var backend = new StubBackend
            {
                Metadata = new Dictionary<string, string>(),
                Tiles = new Dictionary<(int, int, int), byte[]>(),
            };
            using var reader = new MBTilesReader(backend);
            reader.Open("ignored.mbtiles");
            Assert.IsNull(reader.GetTile(5, 1, 1));
        }

        // === Stub backend ===

        // Tiny in-memory IMBTilesBackend so the tests don't need a SQLite
        // binding installed. Production code uses a real implementation that
        // wraps Mono.Data.Sqlite, sqlite-net, etc.
        private sealed class StubBackend : IMBTilesBackend
        {
            public Dictionary<string, string> Metadata = new();
            public Dictionary<(int z, int x, int y), byte[]> Tiles = new();

            public void Open(string filePath) { /* no-op */ }
            public byte[] GetTile(int z, int x, int y) =>
                Tiles.TryGetValue((z, x, y), out var b) ? b : null;
            public Dictionary<string, string> GetMetadata() => Metadata;
            public void Close() { /* no-op */ }
        }
    }
}
