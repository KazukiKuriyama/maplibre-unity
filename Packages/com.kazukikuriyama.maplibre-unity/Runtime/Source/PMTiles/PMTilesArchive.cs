using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Source.PMTiles
{
    /// <summary>
    /// Reads a PMTiles archive over HTTP Range requests or from a local file.
    /// The header (127 bytes) and root directory are fetched up front and
    /// kept in memory; leaf directories are loaded lazily and cached.
    /// One archive instance is shared across all renderers -- the wrapping
    /// <see cref="PMTilesRasterSource"/> / <see cref="PMTilesVectorSource"/>
    /// just funnels per-tile lookups through here.
    /// </summary>
    public sealed class PMTilesArchive
    {
        private readonly string _url;       // HTTP/HTTPS source, null for local file
        private readonly string _filePath;  // Local file source, null for URL

        private PMTilesHeader _header;
        private List<PMTilesEntry> _rootDir;
        // Leaf directory cache keyed by (offset, length). Both keys come
        // straight from a parent directory entry so they're stable for the
        // lifetime of the archive.
        private readonly Dictionary<(ulong off, uint len), List<PMTilesEntry>> _leafCache = new();

        public PMTilesHeader Header => _header;
        public bool IsReady => _header != null && _rootDir != null;

        public PMTilesArchive(string urlOrPath, MonoBehaviour coroutineHost = null)
        {
            if (string.IsNullOrEmpty(urlOrPath))
                throw new ArgumentException("PMTiles URL/path cannot be empty");
            // coroutineHost retained for backwards-compat with samples that
            // still pass `this`; the archive no longer needs it now that
            // I/O runs through Awaitable.
            _ = coroutineHost;
            if (urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _url = urlOrPath;
            }
            else
            {
                // Strip a "file://" prefix so File.OpenRead works on every platform.
                _filePath = urlOrPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(urlOrPath).LocalPath
                    : urlOrPath;
            }
        }

        /// <summary>
        /// Open the archive: fetch + parse the header and root directory.
        /// Throws <see cref="PMTilesException"/> on any failure (network,
        /// gzip, varint underrun). Subsequent tile lookups via
        /// <see cref="LoadTileAsync"/> assume Open has succeeded.
        /// </summary>
        public async Awaitable OpenAsync()
        {
            var headerBytes = await ReadRangeAsync(0, PMTilesHeader.HeaderSize);
            if (headerBytes == null || headerBytes.Length < PMTilesHeader.HeaderSize)
            {
                int got = headerBytes?.Length ?? 0;
                throw new PMTilesException(
                    $"PMTiles header short: expected {PMTilesHeader.HeaderSize} bytes, got {got}. " +
                    $"Source: {(_url ?? _filePath)}");
            }

            try { _header = PMTilesHeader.Parse(headerBytes); }
            catch (Exception e) { throw new PMTilesException($"Header parse: {e.Message}", e); }

            byte[] rootBytes;
            try
            {
                rootBytes = await ReadRangeAsync(_header.RootDirOffset, (int)_header.RootDirLength);
            }
            catch (Exception e) when (!(e is PMTilesException))
            {
                throw new PMTilesException($"Root dir fetch: {e.Message}", e);
            }

            try { _rootDir = PMTilesDirectory.Decode(rootBytes, _header.InternalCompression); }
            catch (Exception e) { throw new PMTilesException($"Root dir decode: {e.Message}", e); }
        }

        /// <summary>
        /// Look up a tile by (z, x, y). Returns the raw bytes (still in the
        /// archive's tile compression -- the source wrappers handle gzip
        /// unwrapping for vector tiles, and Texture2D.LoadImage handles
        /// PNG / JPEG natively). Returns <c>null</c> when the tile id has
        /// no entry in the archive (normal for sparse archives). Throws
        /// <see cref="PMTilesException"/> on I/O / decode failure.
        /// </summary>
        public async Awaitable<byte[]> LoadTileAsync(int z, uint x, uint y)
        {
            if (!IsReady)
                throw new PMTilesException("PMTiles archive not opened yet");
            if (z < _header.MinZoom || z > _header.MaxZoom)
                return null;

            ulong tileId = HilbertCurve.ZxyToTileId(z, x, y);
            return await ResolveTileAsync(tileId, _rootDir);
        }

        // Walk the directory tree (root → leaf) until we find a tile entry.
        // PMTiles allows arbitrary leaf depth in principle, but real archives
        // generally have at most one leaf level. We loop instead of recursing
        // so depths > 2 still terminate cleanly. Returns null on missing.
        private async Awaitable<byte[]> ResolveTileAsync(ulong tileId, List<PMTilesEntry> dir)
        {
            // Bound the loop. Real archives are flat or one-level deep; even
            // 8 is a generous safety margin.
            const int MaxLeafDepth = 8;
            for (int depth = 0; depth < MaxLeafDepth; depth++)
            {
                if (!PMTilesDirectory.TryFindEntry(dir, tileId, out var entry))
                    return null;

                if (!entry.IsLeaf)
                {
                    // Tile entry -- fetch the bytes from the tile-data section.
                    ulong absOff = _header.TileDataOffset + entry.Offset;
                    return await ReadRangeAsync(absOff, (int)entry.Length);
                }

                // Leaf pointer -- fetch the leaf directory, then re-search.
                if (!_leafCache.TryGetValue((entry.Offset, entry.Length), out var leaf))
                {
                    ulong absOff = _header.LeafDirsOffset + entry.Offset;
                    var leafBytes = await ReadRangeAsync(absOff, (int)entry.Length);

                    try
                    {
                        leaf = PMTilesDirectory.Decode(leafBytes, _header.InternalCompression);
                        _leafCache[(entry.Offset, entry.Length)] = leaf;
                    }
                    catch (Exception ex)
                    {
                        throw new PMTilesException($"Leaf decode: {ex.Message}", ex);
                    }
                }
                dir = leaf;
            }
            throw new PMTilesException("PMTiles leaf depth exceeded -- archive may be malformed");
        }

        // === Range-read primitive ===

        // Returns [offset, offset+length) bytes from the source. Local files
        // use FileStream.Read; URLs use UnityWebRequest with a Range header.
        // Throws PMTilesException on failure.
        private async Awaitable<byte[]> ReadRangeAsync(ulong offset, int length)
        {
            if (length <= 0) return Array.Empty<byte>();

            if (_filePath != null)
            {
                // Local file path: synchronous read is fine here. We yield
                // once afterwards so big tiles don't stall the frame.
                byte[] buffer;
                try
                {
                    using var fs = File.OpenRead(_filePath);
                    fs.Seek((long)offset, SeekOrigin.Begin);
                    buffer = new byte[length];
                    int total = 0;
                    while (total < length)
                    {
                        int read = fs.Read(buffer, total, length - total);
                        if (read <= 0) break;
                        total += read;
                    }
                    if (total != length)
                        Array.Resize(ref buffer, total);
                }
                catch (Exception e)
                {
                    throw new PMTilesException($"PMTiles local read: {e.Message}", e);
                }
                await Awaitable.NextFrameAsync();
                return buffer;
            }

            // HTTP path. Some CDNs ignore Range headers; we treat anything
            // outside of 200/206 as failure. The timeout guards against
            // unreachable hosts (DNS failure, hung connection): without it
            // SendWebRequest sits forever and freezes the calling scene's
            // boot sequence -- visible as an apparent Editor hang.
            using var request = UnityWebRequest.Get(_url);
            // Servers that honour Range respond with 206 + the requested
            // slice. Servers that don't (some object stores, some static
            // hosts) reply with 200 + the full file; we accept either and
            // slice locally below so the rest of the parser keeps the same
            // contract.
            string rangeStart = offset.ToString();
            string rangeEnd = (offset + (ulong)length - 1).ToString();
            request.SetRequestHeader("Range", $"bytes={rangeStart}-{rangeEnd}");
            // Disable transparent gzip: GitHub Pages and similar static
            // hosts re-encode octet-stream responses with Content-Encoding:
            // gzip when the client advertises it. Range then applies to
            // the *gzipped* bytes, the prefix doesn't decode to a useful
            // length, and PMTiles header parsing fails. Forcing identity
            // makes Range apply to file bytes as the spec requires.
            request.SetRequestHeader("Accept-Encoding", "identity");
            request.timeout = HttpTimeoutSeconds;
            await request.SendAsync();
            if (request.result != UnityWebRequest.Result.Success)
                throw new PMTilesException($"PMTiles HTTP {request.responseCode}: {request.error}");

            byte[] body = request.downloadHandler?.data;
            if (body == null || body.Length == 0)
            {
                throw new PMTilesException(
                    $"PMTiles HTTP {request.responseCode}: empty body " +
                    $"(Content-Length={request.GetResponseHeader("Content-Length")})");
            }
            // Status 200 means the server ignored Range -- slice the prefix
            // ourselves so callers always get exactly `length` bytes from
            // `offset`. Status 206 means the server already returned just
            // the slice we asked for.
            if (request.responseCode == 200 && body.Length > length)
            {
                var slice = new byte[length];
                Array.Copy(body, (long)offset, slice, 0, length);
                return slice;
            }
            return body;
        }

        // 30 seconds covers any slow-but-real CDN response while still
        // bailing out on dead URLs / blocked networks before the user
        // notices a freeze. Tune at instantiation site if you serve
        // unusually large directory blobs.
        private const int HttpTimeoutSeconds = 30;
    }

    /// <summary>
    /// Thrown by <see cref="PMTilesArchive"/> on I/O, parse, or structural
    /// failure. Sparse-archive misses are <em>not</em> reported as exceptions;
    /// <see cref="PMTilesArchive.LoadTileAsync"/> returns <c>null</c> instead.
    /// </summary>
    public class PMTilesException : Exception
    {
        public PMTilesException(string message) : base(message) { }
        public PMTilesException(string message, Exception inner) : base(message, inner) { }
    }
}
