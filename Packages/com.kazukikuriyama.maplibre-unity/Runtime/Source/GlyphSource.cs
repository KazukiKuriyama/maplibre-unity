using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Fetches glyph PBFs from a MapLibre style's <c>glyphs</c> URL template
    /// (e.g. <c>https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf</c>),
    /// parses them, and packs the resulting glyphs into a <see cref="GlyphAtlas"/>.
    ///
    /// <para>
    /// Ranges are 256 codepoints wide (0-255, 256-511, ...). Each (fontstack,
    /// range) pair is requested at most once; subsequent <see cref="EnsureRange"/>
    /// calls return immediately if the range is already loaded or in flight.
    /// </para>
    /// </summary>
    public class GlyphSource
    {
        // MapLibre Spec uses fixed 256-codepoint ranges for glyph URLs.
        public const int RangeSize = 256;

        private string _urlTemplate;
        private MonoBehaviour _coroutineHost;
        private RequestTransformFunction _transformRequest;
        private readonly GlyphAtlas _atlas;

        // (fontstack, rangeStart) → state. We do not retry failed requests
        // automatically -- the user can call EnsureRange again after fixing
        // the underlying issue.
        private enum RangeState { Pending, Loaded, Failed }
        private readonly Dictionary<(string fontstack, int rangeStart), RangeState> _ranges = new();

        // Listeners notified when any new range finishes loading. SymbolRenderer
        // uses this to re-trigger a label re-layout once previously-missing
        // glyphs become available.
        public event Action OnAnyRangeLoaded;

        public GlyphAtlas Atlas => _atlas;
        public bool HasUrlTemplate => !string.IsNullOrEmpty(_urlTemplate);

        // Disk cache. Subdirectory of Application.persistentDataPath created
        // on first use; we cache the raw PBF bytes per (fontstack, range)
        // pair so subsequent runs of the app skip the network entirely.
        // Disabled when Unity's persistent data path is unavailable (e.g.
        // headless test environments).
        private string _diskCacheDir;
        public bool DiskCacheEnabled => !string.IsNullOrEmpty(_diskCacheDir);

        public GlyphSource(GlyphAtlas atlas = null)
        {
            _atlas = atlas ?? new GlyphAtlas();
        }

        public void Initialize(string urlTemplate, MonoBehaviour coroutineHost,
            RequestTransformFunction transformRequest = null)
        {
            _urlTemplate = urlTemplate;
            _coroutineHost = coroutineHost;
            _transformRequest = transformRequest;
            // Lazy-create the cache dir so unit tests that never touch the
            // disk path don't litter the filesystem.
            TryEnableDiskCache();
        }

        public void SetUrlTemplate(string urlTemplate) => _urlTemplate = urlTemplate;

        private void TryEnableDiskCache()
        {
            if (_diskCacheDir != null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            // On WebGL Player builds Application.persistentDataPath is
            // IDBFS-backed: synchronous File.* calls block on IndexedDB sync
            // and may also miss bytes that haven't been flushed yet. The
            // browser's HTTP cache covers the same ground for free, so leave
            // disk cache disabled here and rely on the network path.
            _diskCacheDir = null;
            return;
#else
            try
            {
                string root = Application.persistentDataPath;
                if (string.IsNullOrEmpty(root)) return;
                string dir = Path.Combine(root, "maplibre-glyph-cache");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _diskCacheDir = dir;
            }
            catch (Exception e)
            {
                // Fall back to network-only when persistentDataPath isn't
                // writeable (some restricted platforms, locked-down sandboxes).
                Debug.LogWarning($"[GlyphSource] Disk cache disabled: {e.Message}");
                _diskCacheDir = null;
            }
#endif
        }

        // Cache key: fontstack name URL-escaped (so colons / spaces survive
        // a filesystem round-trip) joined with the range. e.g.
        // "maplibre-glyph-cache/Open%20Sans%20Regular__0-255.pbf".
        private string CachePath(string fontstack, int rangeStart)
        {
            if (_diskCacheDir == null) return null;
            int rangeEnd = rangeStart + RangeSize - 1;
            string safeFont = UnityWebRequest.EscapeURL(fontstack);
            return Path.Combine(_diskCacheDir, $"{safeFont}__{rangeStart}-{rangeEnd}.pbf");
        }

        /// <summary>
        /// Request the range that contains <paramref name="codepoint"/> for
        /// the supplied font stack. Returns true when the range is already in
        /// the atlas (caller can render immediately); false when a fetch was
        /// kicked off or has previously failed.
        /// </summary>
        public bool EnsureRange(string fontstack, uint codepoint)
        {
            if (string.IsNullOrEmpty(_urlTemplate) || _coroutineHost == null) return false;
            if (string.IsNullOrEmpty(fontstack)) return false;

            int rangeStart = (int)(codepoint / RangeSize) * RangeSize;
            var key = (fontstack, rangeStart);
            if (_ranges.TryGetValue(key, out var state))
            {
                return state == RangeState.Loaded;
            }
            _ranges[key] = RangeState.Pending;
            _coroutineHost.StartCoroutine(FetchRange(fontstack, rangeStart));
            return false;
        }

        private IEnumerator FetchRange(string fontstack, int rangeStart)
        {
            int rangeEnd = rangeStart + RangeSize - 1;

            // Disk cache hit: skip the network entirely. The cached file is
            // raw PBF bytes (same format the HTTP path returns), so the rest
            // of the loader is unchanged.
            string cachePath = CachePath(fontstack, rangeStart);
            byte[] data = null;
            bool cameFromCache = false;
            if (cachePath != null && File.Exists(cachePath))
            {
                try
                {
                    data = File.ReadAllBytes(cachePath);
                    cameFromCache = data.Length > 0;
                }
                catch (Exception e)
                {
                    // Treat a corrupt / unreadable cache entry as "not in cache".
                    Debug.LogWarning($"[GlyphSource] Cache read failed: {e.Message}");
                    data = null;
                }
            }

            if (!cameFromCache)
            {
                // Use Uri.EscapeDataString rather than UnityWebRequest.EscapeURL.
                // EscapeURL is form-encoding (space → '+'), which is wrong for
                // URL path components -- MapLibre's glyph endpoint expects
                // %20 (e.g. demotiles 404s on "Noto+Sans+Regular" but 200s on
                // "Noto%20Sans%20Regular").
                string url = _urlTemplate
                    .Replace("{fontstack}", Uri.EscapeDataString(fontstack))
                    .Replace("{range}", $"{rangeStart}-{rangeEnd}");

                var transformed = RequestTransformer.Apply(_transformRequest, url, ResourceKind.Glyphs);
                if (transformed.Abort)
                {
                    _ranges[(fontstack, rangeStart)] = RangeState.Failed;
                    yield break;
                }

                using var request = UnityWebRequest.Get(transformed.Url);
                request.SetRequestHeader("User-Agent", "MapLibre-Unity/0.1");
                request.SetRequestHeader("Accept", "application/x-protobuf, application/octet-stream");
                ApplyHeaders(request, transformed.Headers);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning(
                        $"[GlyphSource] Fetch failed for '{url}': {request.error} ({request.responseCode})");
                    _ranges[(fontstack, rangeStart)] = RangeState.Failed;
                    yield break;
                }

                data = request.downloadHandler.data;
                if (data == null || data.Length == 0)
                {
                    _ranges[(fontstack, rangeStart)] = RangeState.Failed;
                    yield break;
                }

                // Persist the freshly-fetched bytes for next time. Errors
                // here are non-fatal -- we already have the data in memory.
                if (cachePath != null)
                {
                    try { File.WriteAllBytes(cachePath, data); }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[GlyphSource] Cache write failed: {e.Message}");
                    }
                }
            }

            // PBF parse runs on the main thread for now. Glyph PBFs are tiny
            // (a 256-codepoint range is typically < 50KB) so this stays cheap;
            // if profiling shows otherwise we can move it to Task.Run like the
            // vector tile path does.
            GlyphPbf parsed;
            try
            {
                parsed = GlyphPbfParser.Parse(data);
            }
            catch (Exception e)
            {
                // `url` is no longer in scope here when the bytes came from
                // disk cache -- log the (fontstack, range) pair instead.
                Debug.LogWarning(
                    $"[GlyphSource] Parse failed for {fontstack} {rangeStart}-{rangeEnd}: {e.Message}");
                _ranges[(fontstack, rangeStart)] = RangeState.Failed;
                yield break;
            }

            // Pack every glyph into the atlas under the *requested* fontstack
            // name. The PBF's internal Stack.Name often differs from what the
            // style asked for (servers normalise / canonicalise), so keying
            // by the request makes SdfTextMeshBuilder.TryGet(fontstack, cp)
            // succeed with the same name the style used. Multi-stack PBFs
            // (rare -- comma-joined URL templates) merge their glyphs together
            // under the same key, matching MapLibre GL JS behaviour where the
            // first stack to provide a codepoint wins.
            foreach (var stack in parsed.Stacks)
            {
                foreach (var g in stack.Glyphs)
                    _atlas.Add(fontstack, g);
            }

            _ranges[(fontstack, rangeStart)] = RangeState.Loaded;
            OnAnyRangeLoaded?.Invoke();
        }

        private static void ApplyHeaders(UnityWebRequest request,
            IReadOnlyDictionary<string, string> headers)
        {
            if (headers == null) return;
            foreach (var kv in headers)
                request.SetRequestHeader(kv.Key, kv.Value);
        }

        /// <summary>
        /// Delete every cached glyph PBF file under
        /// Application.persistentDataPath. Use sparingly -- this removes the
        /// disk-cache benefit for the next session. The in-memory atlas is
        /// not touched.
        /// </summary>
        public void ClearDiskCache()
        {
            if (string.IsNullOrEmpty(_diskCacheDir)) return;
            try
            {
                if (Directory.Exists(_diskCacheDir))
                    Directory.Delete(_diskCacheDir, recursive: true);
                Directory.CreateDirectory(_diskCacheDir);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GlyphSource] Cache clear failed: {e.Message}");
            }
        }
    }
}
