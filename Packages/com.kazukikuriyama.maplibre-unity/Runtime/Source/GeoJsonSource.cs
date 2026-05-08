using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// GeoJSON source that converts GeoJSON data into vector tile format on-the-fly.
    /// Supports inline GeoJSON data (via style "data" field) and external URL references.
    /// Matching MapLibre GL JS "geojson" source type behavior.
    /// </summary>
    public class GeoJsonSource : ISource
    {
        public string Id { get; private set; }
        public SourceDefinition Definition { get; private set; }

        /// <summary>
        /// Read-only access to the tile cache for feature queries.
        /// </summary>
        public VectorTileCache Cache => _cache;

        private List<GeoJsonToVectorTile.GeoJsonFeature> _features = new();
        private VectorTileCache _cache;
        private MonoBehaviour _coroutineHost;
        private bool _isDataLoaded;
        private RequestTransformFunction _transformRequest;
        private SuperclusterLite _clusterer;
        private List<GeoJsonToVectorTile.GeoJsonFeature> _nonPointFeatures;
        private readonly HashSet<CanonicalTileID> _pendingRequests = new();
        private readonly Dictionary<CanonicalTileID, CancellationTokenSource> _activeTasks = new();

        /// <summary>
        /// Layer name used as source-layer in the generated VectorTileData.
        /// Matches the source ID so that layers can reference it via source-layer
        /// or fall back to the layer's own ID.
        /// </summary>
        private string _layerName;

        public void Initialize(string id, SourceDefinition definition, MonoBehaviour coroutineHost,
            int cacheCapacity = 256, RequestTransformFunction transformRequest = null)
        {
            Id = id;
            Definition = definition;
            _coroutineHost = coroutineHost;
            _cache = new VectorTileCache(cacheCapacity);
            _layerName = id;
            _transformRequest = transformRequest;
        }

        /// <summary>
        /// Synchronously load inline GeoJSON data (JObject/JArray).
        /// Use this when Data is not a URL string, to avoid one-frame delay from coroutine.
        /// </summary>
        public void LoadDataSync()
        {
            if (Definition.Data == null || Definition.Data.Type == JTokenType.String) return;
            _features = GeoJsonToVectorTile.ParseGeoJson(Definition.Data);
            _isDataLoaded = true;
            BuildClustersIfNeeded();
            Debug.Log($"[GeoJsonSource] '{Id}' loaded {_features.Count} features (inline/sync)");
        }

        /// <summary>
        /// Load GeoJSON data. Must be called before requesting tiles.
        /// For inline data, pass the JToken directly. For URL data, fetches and parses.
        /// </summary>
        public async Awaitable LoadDataAsync()
        {
            if (Definition.Data == null) return;

            if (Definition.Data.Type == JTokenType.String)
            {
                // Data is a URL - fetch it
                string url = Definition.Data.ToString();
                await FetchAndParseAsync(url);
            }
            else
            {
                // Inline GeoJSON object
                _features = GeoJsonToVectorTile.ParseGeoJson(Definition.Data);
                _isDataLoaded = true;
                BuildClustersIfNeeded();
                Debug.Log($"[GeoJsonSource] '{Id}' loaded {_features.Count} features (inline)");
            }
        }

        private async Awaitable FetchAndParseAsync(string url)
        {
            var transformed = RequestTransformer.Apply(_transformRequest, url, ResourceKind.Geojson);
            if (transformed.Abort) return;

            using var request = UnityWebRequest.Get(transformed.Url);
            request.SetRequestHeader("User-Agent", "MapLibre-Unity/0.1");
            ApplyHeaders(request, transformed.Headers);
            await request.SendAsync();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var token = JToken.Parse(request.downloadHandler.text);
                    _features = GeoJsonToVectorTile.ParseGeoJson(token);
                    _isDataLoaded = true;
                    BuildClustersIfNeeded();
                    Debug.Log($"[GeoJsonSource] '{Id}' loaded {_features.Count} features from {url}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GeoJsonSource] Failed to parse GeoJSON from {url}: {e.Message}");
                }
            }
            else
            {
                Debug.LogError($"[GeoJsonSource] Failed to fetch {url}: {request.error}");
            }
        }

        /// <summary>
        /// Set GeoJSON data programmatically (matching MapLibre GL JS setData()).
        /// Clears the tile cache so new tiles are generated from the updated data.
        /// </summary>
        public void SetData(JToken geoJson)
        {
            _features = GeoJsonToVectorTile.ParseGeoJson(geoJson);
            _isDataLoaded = true;
            _cache.Clear();
            BuildClustersIfNeeded();
        }

        /// <summary>
        /// Build the cluster pyramid if Definition.Cluster is enabled. Splits features
        /// into "points to cluster" and "lines/polygons to pass through" so non-point
        /// geometries continue rendering normally underneath the clusters.
        /// </summary>
        private void BuildClustersIfNeeded()
        {
            _clusterer = null;
            _nonPointFeatures = null;
            if (Definition == null || !Definition.Cluster) return;
            if (_features == null || _features.Count == 0) return;

            var clusterPoints = new List<SuperclusterLite.ClusterPoint>(_features.Count);
            _nonPointFeatures = new List<GeoJsonToVectorTile.GeoJsonFeature>();

            foreach (var feature in _features)
            {
                if (feature.Type != VectorTile.GeometryType.Point)
                {
                    _nonPointFeatures.Add(feature);
                    continue;
                }

                // Point / MultiPoint: each coordinate becomes one ClusterPoint.
                if (feature.Rings == null) continue;
                foreach (var ring in feature.Rings)
                {
                    foreach (var coord in ring)
                    {
                        if (coord == null || coord.Length < 2) continue;
                        clusterPoints.Add(new SuperclusterLite.ClusterPoint
                        {
                            X = coord[0],
                            Y = coord[1],
                            Id = feature.Id,
                            NumPoints = 1,
                            Properties = feature.Properties,
                        });
                    }
                }
            }

            int maxClusterZ = Definition.ClusterMaxZoom >= 0
                ? Definition.ClusterMaxZoom
                : System.Math.Max(0, Definition.MaxZoom - 1);
            _clusterer = new SuperclusterLite(
                Definition.MinZoom, maxClusterZ,
                Definition.ClusterRadius, Definition.ClusterMinPoints);
            _clusterer.Load(clusterPoints);
        }

        public void RequestTile(CanonicalTileID tileId,
            Action<CanonicalTileID, VectorTileData> onComplete,
            Action<CanonicalTileID, string> onError)
        {
            if (!_isDataLoaded)
            {
                onError?.Invoke(tileId, "GeoJSON data not loaded yet");
                return;
            }

            if (tileId.Z < Definition.MinZoom || tileId.Z > Definition.MaxZoom)
                return;

            if (_cache.TryGet(tileId, out var cached))
            {
                onComplete?.Invoke(tileId, cached);
                return;
            }

            if (_pendingRequests.Contains(tileId))
                return;

            _pendingRequests.Add(tileId);

            // Generate tile on background thread
            var cts = new CancellationTokenSource();
            _activeTasks[tileId] = cts;
            var token = cts.Token;

            string layerName = _layerName;
            int z = tileId.Z, x = tileId.X, y = tileId.Y;

            // When clustering is enabled, fetch the appropriate cluster level points and
            // mix them with the (untouched) non-point features. Otherwise pass the raw
            // feature list straight through.
            List<GeoJsonToVectorTile.GeoJsonFeature> features;
            if (_clusterer != null)
            {
                features = new List<GeoJsonToVectorTile.GeoJsonFeature>();
                if (_nonPointFeatures != null) features.AddRange(_nonPointFeatures);
                var clusterPts = _clusterer.GetTile(z, x, y);
                ulong nextId = (ulong)features.Count + 1;
                foreach (var cp in clusterPts)
                {
                    features.Add(ClusterPointToFeature(cp, nextId++));
                }
            }
            else
            {
                features = _features;
            }

            _coroutineHost.StartCoroutine(GenerateTileAsync(tileId, features, layerName,
                z, x, y, token, onComplete, onError));
        }

        /// <summary>
        /// Convert a SuperclusterLite point/cluster to a GeoJsonFeature. Cluster nodes
        /// expose <c>cluster=true</c>, <c>cluster_id</c>, and <c>point_count</c> properties
        /// that style filters can use (matches MapLibre GL JS). The <c>cluster_id</c> is
        /// the stable SuperclusterLite ClusterId, suitable for round-tripping back into
        /// <see cref="GetClusterChildren"/> / <see cref="GetClusterExpansionZoom"/>.
        /// </summary>
        private static GeoJsonToVectorTile.GeoJsonFeature ClusterPointToFeature(
            SuperclusterLite.ClusterPoint cp, ulong fallbackId)
        {
            var ring = new List<double[]> { new[] { cp.X, cp.Y } };
            Dictionary<string, object> props;
            if (cp.NumPoints > 1)
            {
                props = new Dictionary<string, object>
                {
                    ["cluster"] = true,
                    ["cluster_id"] = cp.ClusterId,
                    ["point_count"] = (long)cp.NumPoints,
                    ["point_count_abbreviated"] = AbbreviateCount(cp.NumPoints),
                };
            }
            else
            {
                props = cp.Properties != null
                    ? new Dictionary<string, object>(cp.Properties)
                    : new Dictionary<string, object>();
            }

            return new GeoJsonToVectorTile.GeoJsonFeature
            {
                Id = cp.Id != 0 ? cp.Id : fallbackId,
                Type = VectorTile.GeometryType.Point,
                GeometryTypeName = "Point",
                Rings = new List<List<double[]>> { ring },
                Properties = props,
                MinX = cp.X, MinY = cp.Y, MaxX = cp.X, MaxY = cp.Y,
            };
        }

        // === Cluster query API matching MapLibre GL JS ===

        /// <summary>
        /// Return the recommended zoom to fly to in order to break the cluster apart.
        /// Returns -1 if the source is not clustered or the cluster_id is unknown.
        /// </summary>
        public int GetClusterExpansionZoom(long clusterId)
            => _clusterer?.GetClusterExpansionZoom(clusterId) ?? -1;

        /// <summary>
        /// Return the direct children of a cluster (one zoom level finer). Empty when
        /// the source is not clustered or the cluster_id is unknown.
        /// </summary>
        public List<SuperclusterLite.ClusterPoint> GetClusterChildren(long clusterId)
            => _clusterer != null
                ? _clusterer.GetClusterChildren(clusterId)
                : new List<SuperclusterLite.ClusterPoint>();

        /// <summary>
        /// Return all leaf points contained by a cluster (recursively), with optional
        /// pagination via offset + limit. Empty when not clustered / unknown id.
        /// </summary>
        public List<SuperclusterLite.ClusterPoint> GetClusterLeaves(long clusterId,
            int limit = 10, int offset = 0)
            => _clusterer != null
                ? _clusterer.GetClusterLeaves(clusterId, limit, offset)
                : new List<SuperclusterLite.ClusterPoint>();

        private static string AbbreviateCount(int count)
        {
            if (count >= 10000) return (count / 1000) + "k";
            if (count >= 1000) return (count / 100 / 10.0).ToString("0.0") + "k";
            return count.ToString();
        }

        private IEnumerator GenerateTileAsync(CanonicalTileID tileId,
            List<GeoJsonToVectorTile.GeoJsonFeature> features, string layerName,
            int z, int x, int y, CancellationToken token,
            Action<CanonicalTileID, VectorTileData> onComplete,
            Action<CanonicalTileID, string> onError)
        {
            Task<VectorTileData> task = BackgroundTask.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return GeoJsonToVectorTile.GenerateTile(features, z, x, y, layerName);
            }, token);

            while (!task.IsCompleted)
                yield return null;

            _pendingRequests.Remove(tileId);
            _activeTasks.Remove(tileId);

            if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
            {
                _cache.Put(tileId, task.Result);
                onComplete?.Invoke(tileId, task.Result);
            }
            else if (task.IsCanceled)
            {
                // Cancelled -- no callback
            }
            else if (task.IsFaulted)
            {
                string err = task.Exception?.InnerException?.Message ?? "Unknown error";
                onError?.Invoke(tileId, $"GeoJSON tile generation failed: {err}");
            }
        }

        public void CancelRequest(CanonicalTileID tileId)
        {
            _pendingRequests.Remove(tileId);
            if (_activeTasks.TryGetValue(tileId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _activeTasks.Remove(tileId);
            }
        }

        public void Dispose()
        {
            foreach (var kvp in _activeTasks)
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            _activeTasks.Clear();
            _pendingRequests.Clear();
            _cache?.Clear();
            _features.Clear();
        }

        private static void ApplyHeaders(UnityWebRequest request,
            Dictionary<string, string> headers)
        {
            if (headers == null) return;
            foreach (var kvp in headers)
            {
                if (!string.IsNullOrEmpty(kvp.Key))
                    request.SetRequestHeader(kvp.Key, kvp.Value ?? string.Empty);
            }
        }
    }
}
