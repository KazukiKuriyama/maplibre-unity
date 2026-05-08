using System.Collections.Generic;
using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity
{
    /// <summary>
    /// Layer CRUD + paint / layout / filter mutators + queryRenderedFeatures.
    /// Layer GameObjects live under MapRenderer; this partial only edits the
    /// LayerDefinition list on Style and notifies the renderer.
    /// </summary>
    public partial class MapLibreMap
    {
        /// <summary>
        /// Add a layer at runtime. Matches map.addLayer() in MapLibre GL JS.
        /// The layer's source must already exist (added via style JSON or AddSource).
        /// </summary>
        /// <param name="layer">Layer definition to add.</param>
        /// <param name="triggerRefresh">When true (default), immediately refreshes tiles
        /// so the new layer renders. Set to false to skip the eager refresh when adding
        /// many layers in a batch -- a deferred refresh runs at the start of the next
        /// Update() so the layers still appear without the caller having to remember a
        /// trailing <see cref="RefreshTiles"/> call. Calling <see cref="RefreshTiles"/>
        /// yourself before the next Update cancels the deferred refresh, so the explicit
        /// "false × N + RefreshTiles" batch pattern still produces exactly one refresh.</param>
        public void AddLayer(LayerDefinition layer, bool triggerRefresh = true)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] AddLayer: map not initialized yet");
                return;
            }

            // Duplicate check
            foreach (var existing in Style.Layers)
            {
                if (existing.Id == layer.Id)
                {
                    Debug.LogWarning($"[MapLibreMap] AddLayer: layer '{layer.Id}' already exists");
                    return;
                }
            }

            // Source existence check
            if (!string.IsNullOrEmpty(layer.Source) && !Style.Sources.ContainsKey(layer.Source))
            {
                Debug.LogWarning($"[MapLibreMap] AddLayer: source '{layer.Source}' not found");
                return;
            }

            Style.Layers.Add(layer);
            _mapRenderer.AddRendererForLayer(layer);

            // Image sources render a single quad; wire up the just-created renderer
            // to the source so it can fetch the texture / corners.
            if (layer.Type == LayerType.Raster && !string.IsNullOrEmpty(layer.Source)
                && _imageSources.TryGetValue(layer.Source, out var imageSource))
            {
                _mapRenderer.GetImageRenderer(layer.Id)?.SetSource(imageSource);
            }

            FireEvent(new MapEvent(MapEventType.LayerAdd, this) { Id = layer.Id });

            // Existing active tiles won't re-trigger OnTileNeeded, so force a
            // full tile cycle. Sources use LRU caches -- no network re-fetches.
            // triggerRefresh: false defers to the safety-net in Update() so a
            // batch of AddLayer(false) calls still becomes visible next frame
            // even if the caller never issues an explicit RefreshTiles.
            if (triggerRefresh)
                RefreshTiles();
            else
                _pendingLayerRefresh = true;
        }

        /// <summary>
        /// Remove a layer at runtime. Matches map.removeLayer() in MapLibre GL JS.
        /// For custom layers, the user's <see cref="ICustomLayer.OnRemove"/> is
        /// invoked so it can free GameObjects and unsubscribe.
        /// </summary>
        public void RemoveLayer(string layerId)
        {
            if (!_isInitialized) return;

            LayerDefinition target = null;
            for (int i = 0; i < Style.Layers.Count; i++)
            {
                if (Style.Layers[i].Id == layerId)
                {
                    target = Style.Layers[i];
                    Style.Layers.RemoveAt(i);
                    break;
                }
            }

            if (target == null)
            {
                Debug.LogWarning($"[MapLibreMap] RemoveLayer: layer '{layerId}' not found");
                return;
            }

            // Notify ICustomLayer before tearing down internal renderers so the
            // callback still sees a coherent map state.
            if (target.Type == LayerType.Custom
                && _customLayers.TryGetValue(layerId, out var custom))
            {
                try { custom.OnRemove(this); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[MapLibreMap] RemoveLayer: ICustomLayer.OnRemove threw: {e}");
                }
                _customLayers.Remove(layerId);
            }

            _mapRenderer.RemoveRendererForLayer(target);
            FireEvent(new MapEvent(MapEventType.LayerRemove, this) { Id = layerId });
        }

        /// <summary>
        /// Register a user-implemented <see cref="ICustomLayer"/> in the map's
        /// render order. Matches MapLibre GL JS <c>map.addLayer(customLayer, beforeId?)</c>.
        ///
        /// <para>
        /// The map owns the slot -- <c>beforeId</c>, <c>moveLayer</c>, and
        /// minzoom/maxzoom on the corresponding LayerDefinition all apply. The
        /// custom layer is responsible for its own rendering (typically by
        /// instantiating a Unity GameObject in <see cref="ICustomLayer.OnAdd"/>
        /// and updating its transform in <see cref="ICustomLayer.Render"/>).
        /// </para>
        /// </summary>
        /// <param name="customLayer">The layer to add. Its <c>Id</c> must be unique.</param>
        /// <param name="beforeId">
        /// If set, the new layer is inserted just before the layer with this id.
        /// If null or the id is not found, the layer is appended (drawn on top).
        /// </param>
        public void AddLayer(ICustomLayer customLayer, string beforeId = null)
        {
            if (customLayer == null)
            {
                Debug.LogError("[MapLibreMap] AddLayer: customLayer is null");
                return;
            }
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] AddLayer: map not initialized yet");
                return;
            }
            if (string.IsNullOrEmpty(customLayer.Id))
            {
                Debug.LogError("[MapLibreMap] AddLayer: customLayer.Id is required");
                return;
            }
            if (_customLayers.ContainsKey(customLayer.Id))
            {
                Debug.LogWarning($"[MapLibreMap] AddLayer: custom layer '{customLayer.Id}' already exists");
                return;
            }
            // Catch the rarer case where a non-custom layer with this id exists.
            foreach (var existing in Style.Layers)
            {
                if (existing.Id == customLayer.Id)
                {
                    Debug.LogWarning($"[MapLibreMap] AddLayer: layer '{customLayer.Id}' already exists");
                    return;
                }
            }

            var layerDef = new LayerDefinition
            {
                Id = customLayer.Id,
                Type = LayerType.Custom,
                Visibility = "visible",
            };

            int insertAt = Style.Layers.Count;
            if (!string.IsNullOrEmpty(beforeId))
            {
                for (int i = 0; i < Style.Layers.Count; i++)
                {
                    if (Style.Layers[i].Id == beforeId)
                    {
                        insertAt = i;
                        break;
                    }
                }
            }
            Style.Layers.Insert(insertAt, layerDef);
            _customLayers[customLayer.Id] = customLayer;

            // OnAdd may throw without leaving the map in a bad state -- we keep
            // the registration so RemoveLayer still cleans up, and surface the
            // exception to the user.
            try { customLayer.OnAdd(this); }
            catch (System.Exception e)
            {
                Debug.LogError($"[MapLibreMap] AddLayer: ICustomLayer.OnAdd threw: {e}");
            }

            FireEvent(new MapEvent(MapEventType.LayerAdd, this) { Id = customLayer.Id });
        }

        /// <summary>
        /// Reorder <paramref name="layerId"/> so it renders immediately before
        /// <paramref name="beforeId"/>. Pass <c>null</c> to move the layer to the
        /// top of the stack (renders last). Matches map.moveLayer() in MapLibre GL JS.
        /// </summary>
        public void MoveLayer(string layerId, string beforeId = null)
        {
            if (!_isInitialized) return;

            int srcIdx = Style.Layers.FindIndex(l => l.Id == layerId);
            if (srcIdx < 0)
            {
                Debug.LogWarning($"[MapLibreMap] MoveLayer: layer '{layerId}' not found");
                return;
            }

            int destIdx;
            if (string.IsNullOrEmpty(beforeId))
            {
                destIdx = Style.Layers.Count; // insert at end
            }
            else
            {
                destIdx = Style.Layers.FindIndex(l => l.Id == beforeId);
                if (destIdx < 0)
                {
                    Debug.LogWarning($"[MapLibreMap] MoveLayer: beforeId '{beforeId}' not found");
                    return;
                }
            }

            var layer = Style.Layers[srcIdx];
            Style.Layers.RemoveAt(srcIdx);
            // Account for the shift when removing before the destination.
            if (destIdx > srcIdx) destIdx--;
            Style.Layers.Insert(destIdx, layer);

            // Re-apply layer orders so renderQueue matches the new position.
            // Use the layer's index in Style.Layers as its order -- this matches
            // how MapRenderer.Initialize originally assigned orders.
            for (int i = 0; i < Style.Layers.Count; i++)
                _mapRenderer.SetLayerOrder(Style.Layers[i].Id, i);
        }

        /// <summary>
        /// Get a layer definition by ID. Matches map.getLayer() in MapLibre GL JS.
        /// Returns null if the layer does not exist.
        /// </summary>
        public LayerDefinition GetLayer(string id)
        {
            foreach (var layer in Style.Layers)
            {
                if (layer.Id == id) return layer;
            }
            return null;
        }

        // === Property API matching MapLibre GL JS ===

        /// <summary>
        /// Set a paint property on a layer. Matches map.setPaintProperty() in MapLibre GL JS.
        /// </summary>
        /// <param name="layerId">Layer identifier.</param>
        /// <param name="name">Paint property name (e.g. "fill-color", "line-width").</param>
        /// <param name="value">New value. Accepts the same types as a style JSON value
        /// (string color, number, array, expression array, etc.).</param>
        public void SetPaintProperty(string layerId, string name, object value)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] SetPaintProperty: map not initialized yet");
                return;
            }

            var layer = GetLayer(layerId);
            if (layer == null)
            {
                Debug.LogWarning($"[MapLibreMap] SetPaintProperty: layer '{layerId}' not found");
                return;
            }

            layer.Paint ??= new Dictionary<string, object>();
            layer.Paint[name] = value;
            // Paint just changed, so the cached UsesFeatureState() decision may
            // no longer be accurate.
            layer.InvalidateFeatureStateUsage();

            // Background layer: re-parse paint immediately (no tile refresh needed)
            if (layer.Type == LayerType.Background)
            {
                _mapRenderer.UpdateBackgroundPaint(layer.Paint);
                return;
            }

            // Vector / circle / heatmap / fillExtrusion / symbol: rebuild only
            // this layer's GameObjects from the source's cached tile data -- no
            // network fetch and unrelated sources (raster basemap, other
            // vector layers) keep their loaded tiles instead of being expired
            // by TileManager.Clear and re-fetched on the next dispatch.
            if (InvalidateLayer(layerId)) return;

            // Raster / hillshade / terrain / image: no in-place rebuild path
            // for paint changes yet, so fall back to the global refresh.
            RefreshTiles();
        }

        /// <summary>
        /// Get the current value of a paint property. Matches map.getPaintProperty() in MapLibre GL JS.
        /// Returns null if the layer or property is not found.
        /// </summary>
        public object GetPaintProperty(string layerId, string name)
        {
            var layer = GetLayer(layerId);
            if (layer?.Paint == null) return null;
            return layer.Paint.TryGetValue(name, out var val) ? val : null;
        }

        /// <summary>
        /// Set a layout property on a layer. Matches map.setLayoutProperty() in MapLibre GL JS.
        /// </summary>
        /// <param name="layerId">Layer identifier.</param>
        /// <param name="name">Layout property name (e.g. "visibility", "text-field").</param>
        /// <param name="value">New value.</param>
        public void SetLayoutProperty(string layerId, string name, object value)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[MapLibreMap] SetLayoutProperty: map not initialized yet");
                return;
            }

            var layer = GetLayer(layerId);
            if (layer == null)
            {
                Debug.LogWarning($"[MapLibreMap] SetLayoutProperty: layer '{layerId}' not found");
                return;
            }

            layer.Layout ??= new Dictionary<string, object>();
            layer.Layout[name] = value;
            layer.InvalidateFeatureStateUsage();

            // "visibility" is stored on LayerDefinition directly
            if (name == "visibility")
            {
                layer.Visibility = value?.ToString() ?? "visible";
            }

            // Per-layer rebuild keeps unrelated sources (raster basemap,
            // sibling vector layers) from being expired and re-fetched.
            // visibility="none" or out-of-range zoom is honoured by
            // InvalidateLayer leaving the layer empty after teardown.
            if (InvalidateLayer(layerId)) return;

            // Raster / hillshade / terrain / image: fall back to the global
            // refresh until those layer types support in-place rebuilds.
            RefreshTiles();
        }

        /// <summary>
        /// Get the current value of a layout property. Matches map.getLayoutProperty() in MapLibre GL JS.
        /// Returns null if the layer or property is not found.
        /// </summary>
        public object GetLayoutProperty(string layerId, string name)
        {
            var layer = GetLayer(layerId);
            if (name == "visibility") return layer?.Visibility;
            if (layer?.Layout == null) return null;
            return layer.Layout.TryGetValue(name, out var val) ? val : null;
        }

        // === Query API matching MapLibre GL JS ===

        /// <summary>
        /// Query visible features at a screen point. Matches map.queryRenderedFeatures() in MapLibre GL JS.
        /// Returns features ordered from top-most to bottom-most layer.
        /// </summary>
        /// <param name="screenPoint">Screen-space point (pixels).</param>
        /// <param name="options">Optional query filters (layers, filter, tolerance).</param>
        public List<QueryFeature> QueryRenderedFeatures(Vector2 screenPoint, QueryOptions options = null)
        {
            var lngLat = Unproject(screenPoint);
            if (!lngLat.HasValue) return new List<QueryFeature>();
            return QueryRenderedFeatures(lngLat.Value, options);
        }

        /// <summary>
        /// Query visible features at a geographic coordinate.
        /// Matches map.queryRenderedFeatures() in MapLibre GL JS.
        /// </summary>
        public List<QueryFeature> QueryRenderedFeatures(LngLat lngLat, QueryOptions options = null)
        {
            if (!_isInitialized || _tileManager == null)
                return new List<QueryFeature>();

            return FeatureQuery.QueryRenderedFeatures(
                lngLat,
                State.Zoom,
                Style.Layers,
                GetCacheForSource,
                _tileManager.ActiveTiles,
                options ?? new QueryOptions());
        }

        /// <summary>
        /// Query features from a source's cached tiles. No geometric filtering.
        /// Matches map.querySourceFeatures() in MapLibre GL JS.
        /// </summary>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="options">Optional source-layer and filter.</param>
        public List<QueryFeature> QuerySourceFeatures(string sourceId, QuerySourceOptions options = null)
        {
            if (!_isInitialized || _tileManager == null)
                return new List<QueryFeature>();

            var cache = GetCacheForSource(sourceId);
            if (cache == null) return new List<QueryFeature>();

            return FeatureQuery.QuerySourceFeatures(
                sourceId,
                cache,
                _tileManager.ActiveTiles,
                State.Zoom,
                options ?? new QuerySourceOptions());
        }

        /// <summary>True if a layer with the given ID exists. Matches map.getLayer() != null.</summary>
        public bool HasLayer(string layerId) => GetLayer(layerId) != null;

        /// <summary>
        /// Replace a layer's filter expression. Pass null to remove the filter.
        /// Matches map.setFilter(layerId, filter).
        /// </summary>
        public void SetFilter(string layerId, object filter)
        {
            if (!_isInitialized) return;
            var layer = GetLayer(layerId);
            if (layer == null)
            {
                Debug.LogWarning($"[MapLibreMap] SetFilter: layer '{layerId}' not found");
                return;
            }

            layer.Filter = filter == null
                ? null
                : Expressions.ExpressionParser.ParseFilter(JToken.FromObject(filter));
            layer.InvalidateFeatureStateUsage();
            RefreshTiles();
        }

        /// <summary>Returns the parsed filter expression or null. Matches map.getFilter().</summary>
        public Expressions.Expression GetFilter(string layerId)
        {
            var layer = GetLayer(layerId);
            return layer?.Filter;
        }

        /// <summary>
        /// Update a layer's zoom range. Matches map.setLayerZoomRange(layerId, minzoom, maxzoom).
        /// </summary>
        public void SetLayerZoomRange(string layerId, float? minZoom, float? maxZoom)
        {
            if (!_isInitialized) return;
            var layer = GetLayer(layerId);
            if (layer == null)
            {
                Debug.LogWarning($"[MapLibreMap] SetLayerZoomRange: layer '{layerId}' not found");
                return;
            }
            layer.MinZoom = minZoom;
            layer.MaxZoom = maxZoom;
            RefreshTiles();
        }

        /// <summary>List of layer IDs in render order. Matches map.getLayersOrder().</summary>
        public List<string> GetLayersOrder()
        {
            var result = new List<string>();
            if (Style?.Layers == null) return result;
            foreach (var layer in Style.Layers) result.Add(layer.Id);
            return result;
        }

        /// <summary>
        /// Rebuild a single layer's tiles in-place using the source's cached
        /// tile data (no network fetch). Useful after a property change that
        /// only affects one layer -- for example a feature-state hover update
        /// or a SetPaintProperty call. Returns false (no-op) for raster /
        /// hillshade / terrain / image layers; the caller should fall back to
        /// <see cref="RefreshTiles"/> for those types.
        /// </summary>
        /// <returns>True if the layer was matched and rebuilt in place; false
        /// if the layer type is not supported by this code path.</returns>
        public bool InvalidateLayer(string layerId)
        {
            if (!_isInitialized) return false;
            var layer = GetLayer(layerId);
            if (layer == null) return false;

            // Tear down active GameObjects for the layer. Returns false for
            // layer types we don't expose this on (raster/hillshade/terrain) --
            // those still flow through full RefreshTiles when needed.
            if (!_mapRenderer.InvalidateLayer(layerId)) return false;

            // Visibility / minzoom-maxzoom: leave the layer empty after teardown
            // so visibility="none" or out-of-range zoom takes effect immediately.
            // Becomes visible again on the next pan/zoom dispatch when this
            // method is invoked with the layer back in range.
            if (string.IsNullOrEmpty(layer.Source) || !layer.IsVisibleAtZoom(State.Zoom))
                return true;

            // Re-show every active tile from the source cache. The cache is
            // populated by the live tile loader, so this is essentially free --
            // no decode, no network. Tiles past the source maxzoom resolve to
            // the same parent tile the dispatch would pick.
            var centerMerc = CoordinateConversion.LngLatToMercator(State.Center);
            float zoom = State.Zoom;

            if (_vectorSources.TryGetValue(layer.Source, out var vs))
            {
                foreach (var tileId in _tileManager.ActiveTiles)
                {
                    var lookupId = tileId;
                    while (lookupId.Z > vs.Definition.MaxZoom && lookupId.Z > 0)
                        lookupId = lookupId.Parent();
                    if (vs.Cache.TryGet(lookupId, out var data))
                        ReShowVectorTileForLayer(layer, tileId, data, centerMerc, zoom);
                }
            }
            else if (_geoJsonSources.TryGetValue(layer.Source, out var gs))
            {
                foreach (var tileId in _tileManager.ActiveTiles)
                {
                    if (gs.Cache.TryGet(tileId, out var data))
                        ReShowVectorTileForLayer(layer, tileId, data, centerMerc, zoom);
                }
            }
            return true;
        }

        private void ReShowVectorTileForLayer(LayerDefinition layer, CanonicalTileID tileId,
            VectorTileData data, MercatorCoordinate centerMerc, float zoom)
        {
            switch (layer.Type)
            {
                case LayerType.Fill:
                case LayerType.Line:
                    _mapRenderer.GetVectorRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                    break;
                case LayerType.Circle:
                    _mapRenderer.GetCircleRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                    break;
                case LayerType.FillExtrusion:
                    _mapRenderer.GetFillExtrusionRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                    break;
                case LayerType.Heatmap:
                    _mapRenderer.GetHeatmapRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                    break;
                case LayerType.Symbol:
                    _mapRenderer.GetSymbolRenderer(layer.Id)?.ShowTile(tileId, data, layer, centerMerc, zoom);
                    break;
            }
        }
    }
}
