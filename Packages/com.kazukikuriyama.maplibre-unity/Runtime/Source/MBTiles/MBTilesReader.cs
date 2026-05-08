using System;

namespace MapLibre.Unity.Source.MBTiles
{
    /// <summary>
    /// Front for an <see cref="IMBTilesBackend"/>. Keeps the open
    /// <c>metadata</c> dictionary in memory so the source wrappers can
    /// surface bounds / attribution / minzoom / maxzoom without reissuing
    /// SQL each time, and translates MBTiles' TMS-style <c>tile_row</c> to
    /// the XYZ scheme the rest of the library uses.
    /// </summary>
    public sealed class MBTilesReader : IDisposable
    {
        private readonly IMBTilesBackend _backend;
        public MBTilesMetadata Metadata { get; private set; }

        public MBTilesReader(IMBTilesBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <summary>
        /// Open <paramref name="filePath"/> via the backend, parse metadata.
        /// Throws when the backend can't open the file or the file is missing
        /// the <c>tiles</c>/<c>metadata</c> tables -- both turn into "no map
        /// at all" so failing fast is the right behaviour.
        /// </summary>
        public void Open(string filePath)
        {
            _backend.Open(filePath);
            Metadata = MBTilesMetadata.Parse(_backend.GetMetadata());
        }

        /// <summary>
        /// Look up a tile by XYZ coordinates. Returns null when the database
        /// has no row for this tile -- sparse archives are normal in MBTiles.
        /// </summary>
        public byte[] GetTile(int z, int x, int y)
        {
            // MBTiles stores rows in TMS order (origin at bottom-left), but
            // the XYZ scheme used everywhere else has origin at top-left.
            // Convert before querying.
            int tmsY = (1 << z) - 1 - y;
            return _backend.GetTile(z, x, tmsY);
        }

        public void Dispose() => _backend.Close();
    }
}
