using System.Collections.Generic;

namespace MapLibre.Unity.Source.MBTiles
{
    /// <summary>
    /// SQL backend abstraction for MBTiles. The MBTiles spec (a SQLite
    /// database with `tiles` and `metadata` tables) only needs three
    /// operations from the renderer's point of view: open the file, read a
    /// tile blob by (z, x, y), and dump the metadata key/value pairs. Pulling
    /// those out behind an interface lets us:
    /// <list type="bullet">
    ///   <item>Ship the wrapper without forcing every project to bundle SQLite.</item>
    ///   <item>Swap in the user's preferred SQLite binding (Mono.Data.Sqlite,
    ///         sqlite-net, etc.) without touching <see cref="MBTilesReader"/>.</item>
    ///   <item>Stub the backend in tests for hermetic decoding checks.</item>
    /// </list>
    /// Unity does not include a SQLite binding by default; see the README
    /// section on MBTiles for the recommended runtime setup per platform.
    /// </summary>
    public interface IMBTilesBackend
    {
        /// <summary>Open the file. Throws on missing / corrupt databases.</summary>
        void Open(string filePath);

        /// <summary>
        /// Fetch the raw blob for (z, x, y). MBTiles stores `tile_row` flipped
        /// (TMS scheme) -- implementations are responsible for translating to /
        /// from XYZ if the source schema is XYZ. Returns null when no row
        /// matches; the renderer treats that as "no data here", not an error.
        /// </summary>
        byte[] GetTile(int z, int x, int y);

        /// <summary>
        /// All <c>metadata</c> rows as a key/value dictionary. Common keys per
        /// the spec: <c>name</c>, <c>format</c>, <c>bounds</c>, <c>minzoom</c>,
        /// <c>maxzoom</c>, <c>attribution</c>, <c>type</c>.
        /// </summary>
        Dictionary<string, string> GetMetadata();

        /// <summary>Release the underlying connection / file handle.</summary>
        void Close();
    }
}
