using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Fetches and caches Mapbox Vector Tiles (PBF format).
    /// PBF parsing and gzip decompression are performed on background threads.
    /// </summary>
    public class VectorTileSource : ISource
    {
        public string Id { get; private set; }
        public SourceDefinition Definition { get; private set; }

        /// <summary>
        /// Read-only access to the tile cache for feature queries.
        /// </summary>
        public VectorTileCache Cache => _cache;

        private List<string> _tileUrlTemplates = new();
        private VectorTileCache _cache;
        private MonoBehaviour _coroutineHost;
        private int _maxConcurrentRequests = 6;
        private int _activeRequests;
        private RequestTransformFunction _transformRequest;
        private readonly Queue<TileRequest> _requestQueue = new();
        private readonly HashSet<CanonicalTileID> _pendingRequests = new();
        private readonly Dictionary<CanonicalTileID, UnityWebRequest> _activeWebRequests = new();
        private readonly Dictionary<CanonicalTileID, CancellationTokenSource> _activeParseTasks = new();
        // When non-null, ProcessQueue routes tile loads through this user-supplied
        // hook instead of HTTP. Mirrors MapLibre GL JS type:"custom" / dataType:"vector".
        private ICustomVectorSource _customLoader;

        /// <summary>
        /// Timeout in seconds for background PBF parsing per tile.
        /// If parsing exceeds this duration, the task is cancelled and an error is reported.
        /// Includes ThreadPool scheduling latency, so it must be generous enough to cover
        /// large planet tiles on player builds with constrained core counts. Low-zoom
        /// tiles over dense regions (e.g. openfreemap planet z8 over Asia) genuinely
        /// take tens of seconds to parse on a single thread; the Profiler being
        /// attached pushes this further. 90s leaves headroom for those cases without
        /// holding broken tiles forever.
        /// </summary>
        private const float ParseTimeoutSeconds = 90f;

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// Per-frame parse budget on WebGL Player, where the parser runs on
        /// the main thread (no real worker available). 4 ms keeps us well
        /// under a 16.6 ms / 60 fps frame even when other game work runs on
        /// the same frame; the cost is more yields and a longer wall-clock
        /// for the full parse (since each yield drops out to the scheduler).
        /// </summary>
        private const int WebGLParseBudgetMs = 4;
#endif

        private struct TileRequest
        {
            public CanonicalTileID TileId;
            public Action<CanonicalTileID, VectorTileData> OnComplete;
            public Action<CanonicalTileID, string> OnError;
        }

        public void Initialize(string id, SourceDefinition definition, MonoBehaviour coroutineHost,
            int cacheCapacity = 256, RequestTransformFunction transformRequest = null)
        {
            Id = id;
            Definition = definition;
            _coroutineHost = coroutineHost;
            _cache = new VectorTileCache(cacheCapacity);
            _transformRequest = transformRequest;

            if (definition.Tiles != null)
                _tileUrlTemplates = new List<string>(definition.Tiles);
        }

        public void SetTileUrls(List<string> urls)
        {
            _tileUrlTemplates = new List<string>(urls);
        }

        public bool HasTileUrls => _tileUrlTemplates.Count > 0;

        /// <summary>
        /// Replace the URL fetcher with a user-supplied <see cref="ICustomVectorSource"/>.
        /// Subsequent tile requests are forwarded to <c>loader.LoadTile</c>; the
        /// returned bytes go through the same gzip + PBF parser used for HTTP tiles.
        /// Pass null to revert to URL template fetching.
        /// </summary>
        public void SetCustomLoader(ICustomVectorSource loader)
        {
            _customLoader = loader;
        }

        public void RequestTile(CanonicalTileID tileId,
            Action<CanonicalTileID, VectorTileData> onComplete,
            Action<CanonicalTileID, string> onError)
        {
            if (tileId.Z < Definition.MinZoom || tileId.Z > Definition.MaxZoom)
                return;

            // Tiles outside the source's declared bounds are served as empty
            // instead of being requested -- matches MapLibre GL JS behavior.
            if (!Definition.ContainsTile(tileId))
            {
                var empty = new VectorTileData();
                _cache.Put(tileId, empty);
                onComplete?.Invoke(tileId, empty);
                return;
            }

            if (_cache.TryGet(tileId, out var cached))
            {
                onComplete?.Invoke(tileId, cached);
                return;
            }

            if (_pendingRequests.Contains(tileId))
                return;

            _pendingRequests.Add(tileId);
            _requestQueue.Enqueue(new TileRequest
            {
                TileId = tileId,
                OnComplete = onComplete,
                OnError = onError
            });
            ProcessQueue();
        }

        public void CancelRequest(CanonicalTileID tileId)
        {
            _pendingRequests.Remove(tileId);

            // Cancel background parse task if running
            if (_activeParseTasks.TryGetValue(tileId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _activeParseTasks.Remove(tileId);
            }

            if (_activeWebRequests.TryGetValue(tileId, out var request))
            {
                request.Abort();
                request.Dispose();
                _activeWebRequests.Remove(tileId);
                _activeRequests--;
                ProcessQueue();
            }
        }

        private void ProcessQueue()
        {
            while (_activeRequests < _maxConcurrentRequests && _requestQueue.Count > 0)
            {
                var req = _requestQueue.Dequeue();

                if (!_pendingRequests.Contains(req.TileId))
                    continue;

                if (_customLoader != null)
                {
                    _activeRequests++;
                    DispatchCustomLoad(req);
                    continue;
                }

                if (_tileUrlTemplates.Count == 0)
                {
                    req.OnError?.Invoke(req.TileId, "No tile URL templates configured");
                    _pendingRequests.Remove(req.TileId);
                    continue;
                }

                _activeRequests++;
                string url = BuildTileUrl(req.TileId);
                _coroutineHost.StartCoroutine(FetchTile(req.TileId, url, req.OnComplete, req.OnError));
            }
        }

        private void DispatchCustomLoad(TileRequest req)
        {
            var loader = _customLoader;
            var tileId = req.TileId;
            var onComplete = req.OnComplete;
            var onError = req.OnError;
            // Guard against double-callback from the user loader.
            bool finished = false;

            try
            {
                loader.LoadTile(tileId,
                    bytes =>
                    {
                        if (finished) return;
                        finished = true;
                        if (bytes == null || bytes.Length == 0)
                        {
                            // Treat empty as "no data here" -- same behaviour as
                            // an HTTP 404 in the URL fetcher.
                            var empty = new VectorTileData();
                            _cache.Put(tileId, empty);
                            _activeRequests--;
                            _pendingRequests.Remove(tileId);
                            onComplete?.Invoke(tileId, empty);
                            ProcessQueue();
                            return;
                        }
                        _coroutineHost.StartCoroutine(
                            ParseCustomBytes(tileId, bytes, onComplete, onError));
                    },
                    err =>
                    {
                        if (finished) return;
                        finished = true;
                        _activeRequests--;
                        _pendingRequests.Remove(tileId);
                        onError?.Invoke(tileId, err ?? "Custom source error");
                        ProcessQueue();
                    });
            }
            catch (Exception e)
            {
                if (!finished)
                {
                    finished = true;
                    _activeRequests--;
                    _pendingRequests.Remove(tileId);
                    onError?.Invoke(tileId, $"Custom loader threw: {e.Message}");
                    ProcessQueue();
                }
            }
        }

        private IEnumerator ParseCustomBytes(CanonicalTileID tileId, byte[] bytes,
            Action<CanonicalTileID, VectorTileData> onComplete,
            Action<CanonicalTileID, string> onError)
        {
            var cts = new CancellationTokenSource();
            _activeParseTasks[tileId] = cts;
            var token = cts.Token;

#if UNITY_WEBGL && !UNITY_EDITOR
            // Single-thread WebGL: drive ParseLazy with a frame budget so the
            // tile's worth of layers is spread across multiple frames.
            VectorTileData parsed = null;
            string failMessage = null;
            bool timedOut = false;
            yield return ParseTileBytesIncremental(bytes, token,
                onParsed: t => parsed = t,
                onTimedOut: msg => { failMessage = msg; timedOut = true; },
                onFaulted: msg => failMessage = msg);

            _activeParseTasks.Remove(tileId);
            cts.Dispose();
            _activeRequests--;
            _pendingRequests.Remove(tileId);

            if (parsed != null)
            {
                _cache.Put(tileId, parsed);
                onComplete?.Invoke(tileId, parsed);
            }
            else if (timedOut)
            {
                onError?.Invoke(tileId, $"Custom tile parse timeout: {failMessage}");
            }
            else
            {
                onError?.Invoke(tileId,
                    $"Custom tile parse: {failMessage ?? "Unknown parse error"}");
            }
#else
            Task<VectorTileData> parseTask = BackgroundTask.Run(() =>
            {
                byte[] data = bytes;
                if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
                {
                    data = DecompressGzip(data);
                }
                token.ThrowIfCancellationRequested();
                var result = VectorTileParser.Parse(data);
                token.ThrowIfCancellationRequested();
                return result;
            }, token);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!parseTask.IsCompleted)
            {
                if (watch.Elapsed.TotalSeconds > ParseTimeoutSeconds)
                {
                    cts.Cancel();
                    while (!parseTask.IsCompleted)
                        yield return null;
                    break;
                }
                yield return null;
            }

            _activeParseTasks.Remove(tileId);
            cts.Dispose();

            _activeRequests--;
            _pendingRequests.Remove(tileId);

            if (parseTask.Status == TaskStatus.RanToCompletion && parseTask.Result != null)
            {
                _cache.Put(tileId, parseTask.Result);
                onComplete?.Invoke(tileId, parseTask.Result);
            }
            else if (parseTask.IsCanceled)
            {
                onError?.Invoke(tileId,
                    $"Custom tile parse timeout: exceeded {ParseTimeoutSeconds}s");
            }
            else
            {
                string errorMsg = parseTask.Exception?.InnerException?.Message
                                  ?? "Unknown parse error";
                onError?.Invoke(tileId, $"Custom tile parse: {errorMsg}");
            }
#endif

            ProcessQueue();
        }

        private IEnumerator FetchTile(CanonicalTileID tileId, string url,
            Action<CanonicalTileID, VectorTileData> onComplete,
            Action<CanonicalTileID, string> onError)
        {
            var transformed = RequestTransformer.Apply(_transformRequest, url, ResourceKind.Tile);
            if (transformed.Abort)
            {
                _activeRequests--;
                _pendingRequests.Remove(tileId);
                ProcessQueue();
                yield break;
            }

            // Disk cache lookup. A fresh hit short-circuits the HTTP request entirely;
            // a stale hit forwards the ETag in If-None-Match for revalidation.
            byte[] rawData = null;
            string responseCacheControl = null;
            string responseEtag = null;
            bool servedFromCache = false;
            TileDiskCache.CacheEntry cacheMeta = null;
            if (TileDiskCache.TryGet(transformed.Url, out var cachedBytes, out cacheMeta, out var cachedFresh)
                && cachedFresh)
            {
                rawData = cachedBytes;
                servedFromCache = true;
            }

            UnityWebRequest request = null;
            int attempt = 0;
            string lastError = null;

            if (!servedFromCache)
            {
                while (attempt < HttpRetryPolicy.DefaultMaxAttempts)
                {
                    request = UnityWebRequest.Get(transformed.Url);
                    request.SetRequestHeader("User-Agent", "MapLibre-Unity/0.1");
                    request.SetRequestHeader("Accept", "application/x-protobuf, application/octet-stream");
                    ApplyHeaders(request, transformed.Headers);
                    if (cacheMeta != null && !string.IsNullOrEmpty(cacheMeta.ETag))
                        request.SetRequestHeader("If-None-Match", cacheMeta.ETag);
                    _activeWebRequests[tileId] = request;

                    yield return request.SendWebRequest();

                    // Unity-internal cleanup (e.g. PlayMode test scene teardown) can
                    // invalidate the UnityWebRequest's native handle without going
                    // through CancelRequest/Dispose. The entry stays in
                    // _activeWebRequests but the request.result getter throws
                    // NullReferenceException, so guard a probe property with
                    // try-catch to detect external disposal.
                    bool externallyDisposed = false;
                    try { _ = request.isDone; }
                    catch (System.NullReferenceException) { externallyDisposed = true; }

                    if (externallyDisposed || !_activeWebRequests.ContainsKey(tileId))
                    {
                        _activeWebRequests.Remove(tileId);
                        ProcessQueue();
                        yield break;
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                        break;

                    long status = request.responseCode;
                    if (status == 404 || !HttpRetryPolicy.ShouldRetry(request))
                        break;

                    lastError = request.error;
                    request.Dispose();
                    request = null;
                    _activeWebRequests.Remove(tileId);
                    attempt++;
                    if (attempt >= HttpRetryPolicy.DefaultMaxAttempts) break;

                    yield return new UnityEngine.WaitForSeconds(HttpRetryPolicy.BackoffSeconds(attempt));
                    if (!_pendingRequests.Contains(tileId))
                    {
                        ProcessQueue();
                        yield break;
                    }
                }
            }

            _activeRequests--;
            _pendingRequests.Remove(tileId);
            _activeWebRequests.Remove(tileId);

            // Promote cached body when the server reported 304 Not Modified.
            if (request != null && request.responseCode == 304 && cacheMeta != null && cachedBytes != null)
            {
                rawData = cachedBytes;
                responseCacheControl = request.GetResponseHeader("Cache-Control");
                TileDiskCache.Touch(transformed.Url, responseCacheControl);
                request.Dispose();
                request = null;
            }

            if (request != null && request.result == UnityWebRequest.Result.Success)
            {
                rawData = request.downloadHandler.data;
                responseCacheControl = request.GetResponseHeader("Cache-Control");
                responseEtag = request.GetResponseHeader("ETag");
                request.Dispose();
                if (rawData != null && rawData.Length > 0)
                    TileDiskCache.Put(transformed.Url, rawData, responseEtag, responseCacheControl);
            }

            if (rawData != null)
            {

                // gzip decompress + PBF parse. On threaded platforms this runs
                // on a worker via BackgroundTask.Run. On WebGL Player the same
                // wrapper would inline the work and freeze the main thread for
                // the duration of a heavy tile, so we route through the
                // incremental ParseLazy-driven coroutine instead.
                var cts = new CancellationTokenSource();
                _activeParseTasks[tileId] = cts;
                var token = cts.Token;

#if UNITY_WEBGL && !UNITY_EDITOR
                VectorTileData parsed = null;
                string failMessage = null;
                bool timedOut = false;
                yield return ParseTileBytesIncremental(rawData, token,
                    onParsed: t => parsed = t,
                    onTimedOut: msg => { failMessage = msg; timedOut = true; },
                    onFaulted: msg => failMessage = msg);

                _activeParseTasks.Remove(tileId);
                cts.Dispose();

                if (parsed != null)
                {
                    _cache.Put(tileId, parsed);
                    onComplete?.Invoke(tileId, parsed);
                }
                else if (timedOut)
                {
                    onError?.Invoke(tileId, $"Parse timeout for {url}: {failMessage}");
                }
                else
                {
                    onError?.Invoke(tileId,
                        $"Parse error for {url}: {failMessage ?? "Unknown parse error"}");
                }
#else
                Task<VectorTileData> parseTask = BackgroundTask.Run(() =>
                {
                    byte[] data = rawData;
                    if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
                    {
                        data = DecompressGzip(data);
                    }
                    token.ThrowIfCancellationRequested();
                    var result = VectorTileParser.Parse(data);
                    token.ThrowIfCancellationRequested();
                    return result;
                }, token);

                // Wait for background completion with a wall-clock timeout. Stopwatch
                // is used (not Time.deltaTime) so the timer is unaffected by Time.timeScale
                // or Time.maximumDeltaTime clamping during low frame rates.
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!parseTask.IsCompleted)
                {
                    if (watch.Elapsed.TotalSeconds > ParseTimeoutSeconds)
                    {
                        cts.Cancel();
                        // Wait for the task to wind down (cancellation isn't instant)
                        while (!parseTask.IsCompleted)
                            yield return null;
                        break;
                    }
                    yield return null;
                }

                _activeParseTasks.Remove(tileId);
                cts.Dispose();

                if (parseTask.Status == TaskStatus.RanToCompletion && parseTask.Result != null)
                {
                    _cache.Put(tileId, parseTask.Result);
                    onComplete?.Invoke(tileId, parseTask.Result);
                }
                else if (parseTask.IsCanceled)
                {
                    onError?.Invoke(tileId,
                        $"Parse timeout for {url}: exceeded {ParseTimeoutSeconds}s");
                }
                else if (parseTask.IsFaulted)
                {
                    string errorMsg = parseTask.Exception?.InnerException?.Message
                                      ?? "Unknown parse error";
                    onError?.Invoke(tileId, $"Parse error for {url}: {errorMsg}");
                }
                else
                {
                    onError?.Invoke(tileId, $"Parse returned null for {url}");
                }
#endif
            }
            else
            {
                // 404 = tile has no data. MapLibre GL JS treats this as an empty
                // tile (state=loaded, no features) rather than an error, since
                // sparse vector datasets commonly have gaps.
                //
                // The request may have been externally disposed (e.g. by a
                // SetStyle teardown that ran while we were yielding). Probing
                // any property on a disposed UnityWebRequest throws NRE even
                // though the managed reference is non-null, so guard here
                // mirroring the SendWebRequest-loop check above.
                long status = 0;
                string errorText = lastError ?? "no response";
                if (request != null)
                {
                    try
                    {
                        status = request.responseCode;
                        errorText = request.error ?? errorText;
                    }
                    catch (System.NullReferenceException)
                    {
                        // request was externally disposed; fall back to lastError
                    }
                }
                request?.Dispose();

                if (status == 404)
                {
                    var empty = new VectorTileData();
                    _cache.Put(tileId, empty);
                    onComplete?.Invoke(tileId, empty);
                }
                else
                {
                    onError?.Invoke(tileId, $"{url}: {errorText}");
                }
            }

            ProcessQueue();
        }

        private static byte[] DecompressGzip(byte[] compressed)
        {
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// WebGL-only incremental parser. Decompresses gzip and feeds the raw
        /// bytes through <see cref="VectorTileParser.ParseLazy"/>, accumulating
        /// layers into a <see cref="VectorTileData"/>. Yields back to the
        /// coroutine scheduler every <see cref="WebGLParseBudgetMs"/> ms so a
        /// heavy planet tile (~1-3 sec total parse on a single thread) shows
        /// up as a string of small frame budget overruns rather than a single
        /// multi-second freeze. The non-WebGL path keeps using
        /// <c>BackgroundTask.Run</c> + eager Parse, since a worker thread can
        /// finish in one go without affecting the main loop.
        /// </summary>
        private IEnumerator ParseTileBytesIncremental(byte[] bytes, CancellationToken token,
            Action<VectorTileData> onParsed,
            Action<string> onTimedOut,
            Action<string> onFaulted)
        {
            byte[] data = bytes;
            // gzip step is fast (~1-5 ms even for planet z9); skip the yield.
            string preError = null;
            try
            {
                if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
                    data = DecompressGzip(data);
            }
            catch (Exception e) { preError = $"Gzip decompress failed: {e.Message}"; }
            if (preError != null) { onFaulted?.Invoke(preError); yield break; }

            IEnumerator<VectorTileLayer> layerStream = null;
            string initError = null;
            try { layerStream = VectorTileParser.ParseLazy(data).GetEnumerator(); }
            catch (Exception e) { initError = e.Message; }
            if (initError != null) { onFaulted?.Invoke(initError); yield break; }

            var tile = new VectorTileData();
            var totalWatch = System.Diagnostics.Stopwatch.StartNew();
            var budgetWatch = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                if (token.IsCancellationRequested ||
                    totalWatch.Elapsed.TotalSeconds > ParseTimeoutSeconds)
                {
                    layerStream.Dispose();
                    onTimedOut?.Invoke($"exceeded {ParseTimeoutSeconds}s");
                    yield break;
                }

                bool hasNext;
                string stepError = null;
                try { hasNext = layerStream.MoveNext(); }
                catch (Exception e) { hasNext = false; stepError = e.Message; }
                if (stepError != null)
                {
                    layerStream.Dispose();
                    onFaulted?.Invoke(stepError);
                    yield break;
                }
                if (!hasNext) break;

                tile._layers.Add(layerStream.Current);
                if (budgetWatch.ElapsedMilliseconds > WebGLParseBudgetMs)
                {
                    yield return null;
                    budgetWatch.Restart();
                }
            }

            layerStream.Dispose();
            onParsed?.Invoke(tile);
        }
#endif

        private string BuildTileUrl(CanonicalTileID tileId)
        {
            int templateIndex = Math.Abs(tileId.GetHashCode()) % _tileUrlTemplates.Count;
            return tileId.ToUrl(_tileUrlTemplates[templateIndex]);
        }

        private static void ApplyHeaders(UnityWebRequest request, Dictionary<string, string> headers)
        {
            if (headers == null) return;
            foreach (var kvp in headers)
            {
                if (!string.IsNullOrEmpty(kvp.Key))
                    request.SetRequestHeader(kvp.Key, kvp.Value ?? string.Empty);
            }
        }

        public void Dispose()
        {
            foreach (var kvp in _activeParseTasks)
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            _activeParseTasks.Clear();

            foreach (var kvp in _activeWebRequests)
            {
                kvp.Value.Abort();
                kvp.Value.Dispose();
            }
            _activeWebRequests.Clear();
            _requestQueue.Clear();
            _pendingRequests.Clear();
            _cache?.Clear();
        }
    }
}
