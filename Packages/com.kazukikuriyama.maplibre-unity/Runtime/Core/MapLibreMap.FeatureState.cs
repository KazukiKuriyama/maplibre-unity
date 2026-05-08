using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity
{
    /// <summary>
    /// Feature-state surface. Mirrors map.setFeatureState / getFeatureState /
    /// removeFeatureState in MapLibre GL JS, plus the Expressions.IFeatureStateStore
    /// fast-path used by FeatureStateExpression evaluations on background threads.
    /// </summary>
    public partial class MapLibreMap
    {
        /// <summary>
        /// Identifies a single feature for feature-state operations. Mirrors the
        /// "feature selector" object MapLibre GL JS accepts:
        /// <c>{ source, sourceLayer, id }</c>. <c>SourceLayer</c> is required for
        /// vector sources and ignored for GeoJSON / image / raster sources.
        /// </summary>
        public struct FeatureSelector
        {
            public string Source;
            public string SourceLayer;
            public long Id;

            public FeatureSelector(string source, long id)
            {
                Source = source; SourceLayer = null; Id = id;
            }
            public FeatureSelector(string source, string sourceLayer, long id)
            {
                Source = source; SourceLayer = sourceLayer; Id = id;
            }
        }

        // Storage layout: per (source, source-layer) bucket -> per feature id -> per
        // state key. ConcurrentDictionary at every level so that the renderer's
        // background mesh-build threads can read feature-state while the main
        // thread is mutating it (typical hover-driven workload). All three
        // levels are concurrent because per-key updates also race against
        // per-key reads inside a single feature.
        private readonly ConcurrentDictionary<string,
            ConcurrentDictionary<long, ConcurrentDictionary<string, object>>>
            _featureStates = new();

        private static string FeatureStateBucket(string source, string sourceLayer)
            => string.IsNullOrEmpty(sourceLayer) ? source + "|" : source + "|" + sourceLayer;

        /// <summary>
        /// Merge the supplied state values into the feature's state. Existing keys
        /// not present in <paramref name="state"/> are preserved (matches MapLibre
        /// GL JS, which performs a shallow merge per call).
        /// </summary>
        public void SetFeatureState(FeatureSelector feature, IDictionary<string, object> state)
        {
            if (state == null || state.Count == 0) return;
            if (string.IsNullOrEmpty(feature.Source))
            {
                Debug.LogWarning("[MapLibreMap] SetFeatureState: source is required");
                return;
            }

            string bucket = FeatureStateBucket(feature.Source, feature.SourceLayer);
            // GetOrAdd is atomic -- if two threads race here they both see the
            // same (first-winner) inner dictionary, so the foreach below still
            // merges into a single canonical store.
            var perFeature = _featureStates.GetOrAdd(bucket,
                _ => new ConcurrentDictionary<long, ConcurrentDictionary<string, object>>());
            var perKey = perFeature.GetOrAdd(feature.Id,
                _ => new ConcurrentDictionary<string, object>());
            foreach (var kv in state) perKey[kv.Key] = kv.Value;

            InvalidateFeatureStateRender(feature.Source);
        }

        /// <summary>
        /// Convenience overload for setting a single key/value pair.
        /// </summary>
        public void SetFeatureState(FeatureSelector feature, string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            SetFeatureState(feature, new Dictionary<string, object> { { key, value } });
        }

        /// <summary>
        /// Returns a copy of all state values registered for the feature, or an
        /// empty dictionary when none have been set. The copy is a snapshot --
        /// subsequent SetFeatureState calls won't mutate the returned dictionary.
        /// </summary>
        public Dictionary<string, object> GetFeatureState(FeatureSelector feature)
        {
            string bucket = FeatureStateBucket(feature.Source, feature.SourceLayer);
            if (_featureStates.TryGetValue(bucket, out var perFeature)
                && perFeature.TryGetValue(feature.Id, out var perKey))
            {
                // ConcurrentDictionary's enumerator returns a moment-in-time
                // snapshot, so iterating it is safe even while another thread
                // writes new keys.
                var copy = new Dictionary<string, object>(perKey.Count);
                foreach (var kv in perKey) copy[kv.Key] = kv.Value;
                return copy;
            }
            return new Dictionary<string, object>();
        }

        /// <summary>
        /// Remove a single key, the entire feature, or the entire source bucket:
        /// <list type="bullet">
        ///   <item>Pass <paramref name="key"/>=null to remove all state for the feature.</item>
        ///   <item>Pass <c>FeatureSelector</c> with <c>Id=0</c> and a non-null key to no-op (matches MapLibre GL JS guard).</item>
        /// </list>
        /// </summary>
        public void RemoveFeatureState(FeatureSelector feature, string key = null)
        {
            if (string.IsNullOrEmpty(feature.Source)) return;
            string bucket = FeatureStateBucket(feature.Source, feature.SourceLayer);
            if (!_featureStates.TryGetValue(bucket, out var perFeature)) return;

            if (key == null)
            {
                perFeature.TryRemove(feature.Id, out _);
            }
            else if (perFeature.TryGetValue(feature.Id, out var perKey))
            {
                perKey.TryRemove(key, out _);
            }

            InvalidateFeatureStateRender(feature.Source);
        }

        /// <summary>
        /// Drop every feature-state entry for the source. Useful when a source is
        /// being replaced or the application enters a fresh interaction context.
        /// </summary>
        public void ClearFeatureState(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return;
            string prefix = sourceId + "|";
            // ConcurrentDictionary.Keys returns a snapshot, so this is safe to
            // enumerate alongside writers; TryRemove is the concurrent-safe
            // delete primitive.
            bool removedAny = false;
            foreach (var bucket in _featureStates.Keys)
            {
                if (bucket.StartsWith(prefix, StringComparison.Ordinal)
                    && _featureStates.TryRemove(bucket, out _))
                {
                    removedAny = true;
                }
            }
            if (removedAny) InvalidateFeatureStateRender(sourceId);
        }

        // IFeatureStateStore -- used by FeatureStateExpression on hot evaluation paths.
        // Explicit interface implementation keeps the four-arg lookup out of the
        // public API surface; users should go through GetFeatureState(FeatureSelector).
        object Expressions.IFeatureStateStore.GetFeatureState(string sourceId, string sourceLayer,
            long featureId, string key)
        {
            string bucket = FeatureStateBucket(sourceId, sourceLayer);
            if (_featureStates.TryGetValue(bucket, out var perFeature)
                && perFeature.TryGetValue(featureId, out var perKey)
                && perKey.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        private void InvalidateFeatureStateRender(string sourceId)
        {
            // Skip the rebuild entirely when no layer reads feature-state from
            // the source we just mutated. Common case for apps that call
            // SetFeatureState before any layer references it, and for sources
            // whose layers happen not to use feature-state at all.
            if (!_isInitialized) return;
            if (Style?.Layers == null) { RefreshTiles(); return; }

            // Per-layer rebuild: tear down only the renderers whose paint /
            // layout / filter actually reads feature-state for this source,
            // then re-show their tiles from the source cache. This avoids the
            // global RefreshTiles path which rebuilds every layer's tiles and
            // forces a network round-trip for non-cached tiles.
            foreach (var layer in Style.Layers)
            {
                if (!string.IsNullOrEmpty(sourceId)
                    && !string.Equals(layer.Source, sourceId, StringComparison.Ordinal))
                    continue;
                if (!layer.UsesFeatureState()) continue;
                InvalidateLayer(layer.Id);
            }
        }
    }
}
