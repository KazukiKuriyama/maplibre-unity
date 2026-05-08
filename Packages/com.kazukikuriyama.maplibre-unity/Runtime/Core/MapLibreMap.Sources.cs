using System;
using System.Collections.Generic;
using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity
{
    /// <summary>
    /// Source CRUD surface -- AddSource overloads (URL / custom raster / custom
    /// vector / image), RemoveSource, lookup helpers, and the small async
    /// callbacks that finish loading TileJSON / GeoJSON after AddSource returns.
    /// All state lives in the field declarations on the main partial.
    /// </summary>
    public partial class MapLibreMap
    {
        /// <summary>
        /// Add a GeoJSON source at runtime. Matches map.addSource() in MapLibre GL JS.
        /// </summary>
        /// <param name="id">Unique source identifier.</param>
        /// <param name="definition">Source definition with Type set to GeoJson and Data populated.</param>
        public void AddSource(string id, SourceDefinition definition)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] AddSource: map not initialized yet");
                return;
            }

            if (Style.Sources.ContainsKey(id))
            {
                Debug.LogWarning($"[MapLibreMap] AddSource: source '{id}' already exists");
                return;
            }

            Style.Sources[id] = definition;
            // Keep the effective zoom range in sync -- without this, a runtime
            // AddSource on a previously-empty style leaves the sentinels at
            // (min=22,max=0) and TileGrid.GetVisibleTilesLOD roots at z=22 on
            // the next frame, freezing the main thread.
            _effectiveMinZoom = Math.Min(_effectiveMinZoom, definition.MinZoom);
            _effectiveMaxZoom = Math.Max(_effectiveMaxZoom, definition.MaxZoom);
            FireEvent(new MapEvent(MapEventType.SourceAdd, this) { Id = id });

            if (definition.Type == SourceType.GeoJson)
            {
                var source = new GeoJsonSource();
                source.Initialize(id, definition, this, _tileCacheSize, TransformRequest);
                _geoJsonSources[id] = source;

                // Inline JObject/JArray data can be loaded synchronously (no network).
                // URL string data still needs async fetch.
                if (definition.Data != null && definition.Data.Type != JTokenType.String)
                    source.LoadDataSync();
                else
                    _ = LoadGeoJsonSourceAndRefreshAsync(source);
            }
            else if (definition.Type == SourceType.Vector)
            {
                var source = new VectorTileSource();
                source.Initialize(id, definition, this, _tileCacheSize, TransformRequest);
                _vectorSources[id] = source;

                if (!string.IsNullOrEmpty(definition.Url) && !source.HasTileUrls)
                    _ = LoadVectorSourceTileJSONAsync(id, source, definition);
            }
            else if (definition.Type == SourceType.Raster)
            {
                var source = new RasterTileSource();
                source.Initialize(id, definition, this, _tileCacheSize, TransformRequest);
                _rasterSources[id] = source;

                if (!string.IsNullOrEmpty(definition.Url) && !source.HasTileUrls)
                    _ = LoadRasterSourceTileJSONAsync(id, source, definition);
            }
            else if (definition.Type == SourceType.Image)
            {
                var source = new ImageSource();
                source.Initialize(id, definition, this, TransformRequest);
                _imageSources[id] = source;

                if (!string.IsNullOrEmpty(definition.Url))
                    _ = source.LoadFromUrlAsync();
            }
        }

        /// <summary>
        /// Register a user-implemented <see cref="ICustomRasterSource"/> as a
        /// raster source. Mirrors MapLibre GL JS <c>map.addSource(id, { type: "custom", dataType: "raster", ... })</c>.
        /// Layers can then reference the source by id like any HTTP-backed raster.
        /// </summary>
        public void AddSource(string id, ICustomRasterSource customSource)
        {
            if (customSource == null)
            {
                Debug.LogError("[MapLibreMap] AddSource: customSource is null");
                return;
            }
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] AddSource: map not initialized yet");
                return;
            }
            if (Style.Sources.ContainsKey(id))
            {
                Debug.LogWarning($"[MapLibreMap] AddSource: source '{id}' already exists");
                return;
            }

            // Build a SourceDefinition mirror so attribution / bounds / zoom-range
            // queries against Style.Sources work the same way for HTTP and custom
            // sources. Tiles list stays empty -- the loader supplies them.
            var def = new SourceDefinition
            {
                Type = SourceType.Raster,
                TileSize = customSource.TileSize > 0 ? customSource.TileSize : 256,
                MinZoom = customSource.MinZoom,
                MaxZoom = customSource.MaxZoom > 0 ? customSource.MaxZoom : 22,
                Bounds = customSource.Bounds,
                Attribution = customSource.Attribution,
            };

            var source = new RasterTileSource();
            source.Initialize(id, def, this, _tileCacheSize, TransformRequest);
            source.SetCustomLoader(customSource);
            _rasterSources[id] = source;
            Style.Sources[id] = def;
            _effectiveMinZoom = Math.Min(_effectiveMinZoom, def.MinZoom);
            _effectiveMaxZoom = Math.Max(_effectiveMaxZoom, def.MaxZoom);

            if (!string.IsNullOrEmpty(def.Attribution))
                _controlsOverlay?.AddAttribution(def.Attribution);

            FireEvent(new MapEvent(MapEventType.SourceAdd, this) { Id = id });
        }

        /// <summary>
        /// Register a user-implemented <see cref="ICustomVectorSource"/>. The
        /// supplied PBF bytes are run through the same gzip + parser pipeline
        /// as HTTP-fetched vector tiles, so all expression / filter / paint
        /// features work unchanged. Mirrors MapLibre GL JS
        /// <c>map.addSource(id, { type: "custom", dataType: "vector", ... })</c>.
        /// </summary>
        public void AddSource(string id, ICustomVectorSource customSource)
        {
            if (customSource == null)
            {
                Debug.LogError("[MapLibreMap] AddSource: customSource is null");
                return;
            }
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] AddSource: map not initialized yet");
                return;
            }
            if (Style.Sources.ContainsKey(id))
            {
                Debug.LogWarning($"[MapLibreMap] AddSource: source '{id}' already exists");
                return;
            }

            var def = new SourceDefinition
            {
                Type = SourceType.Vector,
                MinZoom = customSource.MinZoom,
                MaxZoom = customSource.MaxZoom > 0 ? customSource.MaxZoom : 22,
                Bounds = customSource.Bounds,
                Attribution = customSource.Attribution,
            };

            var source = new VectorTileSource();
            source.Initialize(id, def, this, _tileCacheSize, TransformRequest);
            source.SetCustomLoader(customSource);
            _vectorSources[id] = source;
            Style.Sources[id] = def;
            _effectiveMinZoom = Math.Min(_effectiveMinZoom, def.MinZoom);
            _effectiveMaxZoom = Math.Max(_effectiveMaxZoom, def.MaxZoom);

            if (!string.IsNullOrEmpty(def.Attribution))
                _controlsOverlay?.AddAttribution(def.Attribution);

            FireEvent(new MapEvent(MapEventType.SourceAdd, this) { Id = id });
        }

        /// <summary>
        /// Add an image source backed by an in-Unity <see cref="Texture2D"/> asset.
        /// Bypasses the URL fetch path so you can drape built-in textures onto the map.
        /// </summary>
        /// <param name="id">Unique source identifier.</param>
        /// <param name="texture">Texture to render. Caller retains ownership.</param>
        /// <param name="topLeft">Top-left corner (north-west) of the image.</param>
        /// <param name="topRight">Top-right corner (north-east).</param>
        /// <param name="bottomRight">Bottom-right corner (south-east).</param>
        /// <param name="bottomLeft">Bottom-left corner (south-west).</param>
        public void AddImageSource(string id, Texture2D texture,
            LngLat topLeft, LngLat topRight, LngLat bottomRight, LngLat bottomLeft)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] AddImageSource: map not initialized yet");
                return;
            }
            if (Style.Sources.ContainsKey(id))
            {
                Debug.LogWarning($"[MapLibreMap] AddImageSource: source '{id}' already exists");
                return;
            }

            var definition = new SourceDefinition
            {
                Type = SourceType.Image,
                Coordinates = new[]
                {
                    new[] { topLeft.Longitude, topLeft.Latitude },
                    new[] { topRight.Longitude, topRight.Latitude },
                    new[] { bottomRight.Longitude, bottomRight.Latitude },
                    new[] { bottomLeft.Longitude, bottomLeft.Latitude }
                }
            };
            Style.Sources[id] = definition;
            _effectiveMinZoom = Math.Min(_effectiveMinZoom, definition.MinZoom);
            _effectiveMaxZoom = Math.Max(_effectiveMaxZoom, definition.MaxZoom);

            var source = new ImageSource();
            source.Initialize(id, definition, this, TransformRequest);
            source.SetTexture(texture);
            _imageSources[id] = source;

            FireEvent(new MapEvent(MapEventType.SourceAdd, this) { Id = id });
        }

        private async Awaitable LoadGeoJsonSourceAndRefreshAsync(GeoJsonSource source)
        {
            await source.LoadDataAsync();
            // RefreshTiles (not _needsTileUpdate) so tiles already added to
            // _activeTiles before data was ready get re-requested. Setting only
            // _needsTileUpdate would skip them -- UpdateVisibleTiles fires
            // OnTileNeeded for new tile IDs only -- leaving them stuck in Error
            // state until the user pans/zooms the active set.
            RefreshTiles();
        }

        private async Awaitable LoadVectorSourceTileJSONAsync(string id, VectorTileSource source, SourceDefinition def)
        {
            try
            {
                var data = await TileJSONFetcher.FetchAsync(def.Url, TransformRequest);
                source.SetTileUrls(data.Tiles);
                def.MinZoom = data.MinZoom;
                def.MaxZoom = data.MaxZoom;
            }
            catch (Exception e)
            {
                FireError($"TileJSON fetch failed for '{id}': {e.Message}", id);
            }
            RefreshTiles();
        }

        private async Awaitable LoadRasterSourceTileJSONAsync(string id, RasterTileSource source, SourceDefinition def)
        {
            try
            {
                var data = await TileJSONFetcher.FetchAsync(def.Url, TransformRequest);
                source.SetTileUrls(data.Tiles);
                def.MinZoom = data.MinZoom;
                def.MaxZoom = data.MaxZoom;
            }
            catch (Exception e)
            {
                FireError($"TileJSON fetch failed for '{id}': {e.Message}", id);
            }
            RefreshTiles();
        }

        /// <summary>
        /// Remove a source at runtime. Matches map.removeSource() in MapLibre GL JS.
        /// All layers referencing this source must be removed first.
        /// </summary>
        public void RemoveSource(string id)
        {
            if (!_isInitialized) return;

            // Check for layers still using this source
            foreach (var layer in Style.Layers)
            {
                if (layer.Source == id)
                {
                    Debug.LogWarning($"[MapLibreMap] RemoveSource: layer '{layer.Id}' still references source '{id}'. Remove layers first.");
                    return;
                }
            }

            // Capture the attribution before we remove the SourceDefinition so
            // we can release it from the overlay. AddSource always pushes the
            // string when non-empty, so this keeps the visible row in sync.
            string releasedAttribution = null;
            if (Style.Sources.TryGetValue(id, out var removedDef))
            {
                releasedAttribution = removedDef?.Attribution;
            }
            Style.Sources.Remove(id);
            FireEvent(new MapEvent(MapEventType.SourceRemove, this) { Id = id });

            if (_geoJsonSources.TryGetValue(id, out var geoJsonSource))
            {
                geoJsonSource.Dispose();
                _geoJsonSources.Remove(id);
            }
            else if (_vectorSources.TryGetValue(id, out var vectorSource))
            {
                vectorSource.Dispose();
                _vectorSources.Remove(id);
            }
            else if (_rasterSources.TryGetValue(id, out var rasterSource))
            {
                rasterSource.Dispose();
                _rasterSources.Remove(id);
            }
            else if (_imageSources.TryGetValue(id, out var imageSource))
            {
                imageSource.Dispose();
                _imageSources.Remove(id);
            }

            if (!string.IsNullOrEmpty(releasedAttribution))
                _controlsOverlay?.RemoveAttribution(releasedAttribution);
        }

        /// <summary>
        /// Resolve a source id to its type for the MapRenderer. Returns null when the
        /// source is missing so the caller can keep the current default behavior.
        /// </summary>
        private SourceType? ResolveSourceType(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return null;
            if (Style != null && Style.Sources.TryGetValue(sourceId, out var def))
                return def.Type;
            return null;
        }

        /// <summary>
        /// Returns the <c>lineMetrics</c> flag declared by the source. line-gradient
        /// is gated on this per Style Spec -- without lineMetrics the line shader has
        /// no per-vertex progress to sample with, and the property has no effect.
        /// </summary>
        private bool ResolveSourceLineMetrics(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return false;
            if (Style != null && Style.Sources.TryGetValue(sourceId, out var def))
                return def.LineMetrics;
            return false;
        }

        /// <summary>
        /// Get a source by ID. Matches map.getSource() in MapLibre GL JS.
        /// Returns null if the source does not exist.
        /// </summary>
        public ISource GetSource(string id)
        {
            if (_rasterSources.TryGetValue(id, out var raster)) return raster;
            if (_vectorSources.TryGetValue(id, out var vector)) return vector;
            if (_geoJsonSources.TryGetValue(id, out var geoJson)) return geoJson;
            if (_imageSources.TryGetValue(id, out var image)) return image;
            return null;
        }

        /// <summary>
        /// Get an image source by ID so you can update its texture or corners at runtime.
        /// Returns null if the source does not exist or is not an image source.
        /// </summary>
        public ImageSource GetImageSource(string id)
        {
            return _imageSources.TryGetValue(id, out var source) ? source : null;
        }

        /// <summary>
        /// Get a GeoJSON source by ID to call SetData() on it.
        /// Convenience overload of GetSource() with type cast.
        /// Matches map.getSource() in MapLibre GL JS.
        /// </summary>
        public GeoJsonSource GetGeoJsonSource(string id)
        {
            return _geoJsonSources.TryGetValue(id, out var source) ? source : null;
        }

        // === Cluster query API matching MapLibre GL JS ===

        /// <summary>
        /// Returns the recommended zoom to fly to in order to break the cluster apart.
        /// Use the <c>cluster_id</c> property from a clicked cluster feature to look it up.
        /// Returns -1 when the source is not clustered or the id is unknown.
        /// Matches map.getSource(id).getClusterExpansionZoom(clusterId).
        /// </summary>
        public int GetClusterExpansionZoom(string sourceId, long clusterId)
            => GetGeoJsonSource(sourceId)?.GetClusterExpansionZoom(clusterId) ?? -1;

        /// <summary>
        /// Direct children (one zoom level finer) of the cluster.
        /// Matches map.getSource(id).getClusterChildren(clusterId).
        /// </summary>
        public List<SuperclusterLite.ClusterPoint> GetClusterChildren(string sourceId, long clusterId)
            => GetGeoJsonSource(sourceId)?.GetClusterChildren(clusterId)
               ?? new List<SuperclusterLite.ClusterPoint>();

        /// <summary>
        /// All leaf points contained by the cluster, recursively. Limit/offset for pagination.
        /// Matches map.getSource(id).getClusterLeaves(clusterId, limit, offset).
        /// </summary>
        public List<SuperclusterLite.ClusterPoint> GetClusterLeaves(string sourceId, long clusterId,
            int limit = 10, int offset = 0)
            => GetGeoJsonSource(sourceId)?.GetClusterLeaves(clusterId, limit, offset)
               ?? new List<SuperclusterLite.ClusterPoint>();

        /// <summary>True if a source with the given ID exists. Matches map.getSource() != null.</summary>
        public bool HasSource(string sourceId) => GetSource(sourceId) != null;

        private VectorTileCache GetCacheForSource(string sourceId)
        {
            if (_vectorSources.TryGetValue(sourceId, out var vs)) return vs.Cache;
            if (_geoJsonSources.TryGetValue(sourceId, out var gs)) return gs.Cache;
            return null;
        }
    }
}
