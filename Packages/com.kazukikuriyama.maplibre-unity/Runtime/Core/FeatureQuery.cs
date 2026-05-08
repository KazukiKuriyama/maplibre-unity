using System;
using System.Collections.Generic;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using UnityEngine;

namespace MapLibre.Unity
{
    /// <summary>
    /// Internal engine for queryRenderedFeatures and querySourceFeatures.
    /// Handles coordinate conversion, tile lookup, filter evaluation, and hit testing.
    /// </summary>
    internal static class FeatureQuery
    {
        /// <summary>
        /// Query rendered features at a geographic coordinate.
        /// Returns features ordered from top-most to bottom-most layer (reverse render order).
        /// </summary>
        internal static List<QueryFeature> QueryRenderedFeatures(
            LngLat lngLat,
            float zoom,
            IReadOnlyList<LayerDefinition> layers,
            Func<string, VectorTileCache> getCacheForSource,
            IReadOnlyCollection<CanonicalTileID> activeTiles,
            QueryOptions options)
        {
            var results = new List<QueryFeature>();
            if (layers == null || activeTiles == null) return results;

            int intZoom = Mathf.Clamp(Mathf.FloorToInt(zoom), 0, MapConstants.MaxZoom);

            // Convert LngLat to fractional tile coordinates at the integer zoom
            var (fracTileX, fracTileY) = CoordinateConversion.LngLatToTileXY(lngLat, intZoom);

            // Build a set of layer IDs to filter (if specified)
            HashSet<string> layerFilter = null;
            if (options.Layers != null && options.Layers.Length > 0)
            {
                layerFilter = new HashSet<string>(options.Layers);
            }

            // Collect candidate tiles that contain the query point
            var candidateTiles = new List<CanonicalTileID>();
            foreach (var tile in activeTiles)
            {
                if (TileContainsPoint(tile, lngLat, intZoom))
                    candidateTiles.Add(tile);
            }

            if (candidateTiles.Count == 0) return results;

            // Iterate layers in reverse order (top-most first, matching MapLibre GL JS)
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];

                // Skip non-queryable layer types
                if (layer.Type == LayerType.Background || layer.Type == LayerType.Raster ||
                    layer.Type == LayerType.Hillshade)
                    continue;

                if (!layer.IsVisibleAtZoom(zoom)) continue;
                if (layerFilter != null && !layerFilter.Contains(layer.Id)) continue;
                if (string.IsNullOrEmpty(layer.Source)) continue;

                var cache = getCacheForSource(layer.Source);
                if (cache == null) continue;

                foreach (var tile in candidateTiles)
                {
                    if (!cache.TryGet(tile, out var tileData)) continue;

                    // Find the matching VectorTileLayer
                    string sourceLayerName = !string.IsNullOrEmpty(layer.SourceLayer)
                        ? layer.SourceLayer
                        : layer.Id;
                    var vtLayer = tileData.GetLayer(sourceLayerName);
                    if (vtLayer == null) continue;

                    // Compute the query point in tile-local coordinates [0, extent]
                    var queryPoint = ComputeTileLocalPoint(lngLat, tile, vtLayer.Extent);

                    // Pixel-to-tile-local tolerance conversion
                    float tileScreenSize = MapConstants.TileSize * Mathf.Pow(2f, zoom - tile.Z);
                    float toleranceTileUnits = options.Tolerance * vtLayer.Extent / tileScreenSize;

                    foreach (var feature in vtLayer.Features)
                    {
                        var ctx = new EvaluationContext(zoom, feature);

                        // Apply layer filter
                        if (layer.Filter != null && !layer.Filter.EvaluateBool(ctx, true))
                            continue;

                        // Apply additional query filter
                        if (options.Filter != null && !options.Filter.EvaluateBool(ctx, true))
                            continue;

                        // Decode geometry and hit test
                        var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);
                        if (rings == null || rings.Count == 0) continue;

                        if (!HitTest(feature.Type, layer.Type, rings, queryPoint, toleranceTileUnits,
                                ctx, layer))
                            continue;

                        results.Add(BuildQueryFeature(feature, layer, tile, rings));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Query features from all cached tiles of a source. No geometric hit testing.
        /// </summary>
        internal static List<QueryFeature> QuerySourceFeatures(
            string sourceId,
            VectorTileCache cache,
            IReadOnlyCollection<CanonicalTileID> activeTiles,
            float zoom,
            QuerySourceOptions options)
        {
            var results = new List<QueryFeature>();
            if (cache == null || activeTiles == null) return results;

            // Deduplicate by feature ID (features appear in multiple tiles)
            var seen = new HashSet<(string, ulong)>();

            foreach (var tileId in activeTiles)
            {
                if (!cache.TryGet(tileId, out var tileData)) continue;

                // Iterate all layers or filter by source-layer
                for (int li = 0; li < tileData.Layers.Count; li++)
                {
                    var vtLayer = tileData.Layers[li];
                    if (!string.IsNullOrEmpty(options.SourceLayer) && vtLayer.Name != options.SourceLayer)
                        continue;

                    foreach (var feature in vtLayer.Features)
                    {
                        // Deduplicate by (sourceLayer, featureId) -- skip features with Id=0 (unset)
                        if (feature.Id != 0)
                        {
                            var key = (vtLayer.Name, feature.Id);
                            if (!seen.Add(key)) continue;
                        }

                        // Apply filter
                        if (options.Filter != null)
                        {
                            var ctx = new EvaluationContext(zoom, feature);
                            if (!options.Filter.EvaluateBool(ctx, true)) continue;
                        }

                        var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);
                        results.Add(BuildQueryFeature(feature, null, tileId, rings, sourceId, vtLayer.Name));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Check if a tile contains a geographic point.
        /// </summary>
        private static bool TileContainsPoint(CanonicalTileID tile, LngLat lngLat, int queryZoom)
        {
            var (fracX, fracY) = CoordinateConversion.LngLatToTileXY(lngLat, tile.Z);
            return (int)fracX == tile.X && (int)fracY == tile.Y;
        }

        /// <summary>
        /// Convert a LngLat to tile-local coordinates [0, extent] for a given tile.
        /// </summary>
        private static Vector2 ComputeTileLocalPoint(LngLat lngLat, CanonicalTileID tile, int extent)
        {
            var (fracX, fracY) = CoordinateConversion.LngLatToTileXY(lngLat, tile.Z);
            float localX = (float)(fracX - tile.X) * extent;
            float localY = (float)(fracY - tile.Y) * extent;
            return new Vector2(localX, localY);
        }

        /// <summary>
        /// Perform hit testing based on geometry and layer type.
        /// </summary>
        private static bool HitTest(
            GeometryType geomType, LayerType layerType,
            List<List<Vector2>> rings, Vector2 queryPoint,
            float toleranceTileUnits, EvaluationContext ctx, LayerDefinition layer)
        {
            switch (geomType)
            {
                case GeometryType.Polygon:
                {
                    var polygons = GeometryDecoder.ClassifyPolygonRings(rings);
                    foreach (var polygon in polygons)
                    {
                        if (GeometryHitTest.PointInPolygon(queryPoint, polygon))
                            return true;
                    }
                    return false;
                }

                case GeometryType.LineString:
                {
                    float tolerance = toleranceTileUnits;
                    // Add half line-width to tolerance for line layers
                    if (layerType == LayerType.Line && layer.Paint != null)
                    {
                        var lineProps = StyleParser.ParseLinePaint(layer.Paint);
                        float lineWidth = lineProps.ResolveLineWidth(ctx);
                        // line-width is in pixels -- convert to tile units using the same factor
                        tolerance += lineWidth * 0.5f * toleranceTileUnits / 3f;
                    }

                    foreach (var ring in rings)
                    {
                        if (GeometryHitTest.PointNearLine(queryPoint, ring, tolerance))
                            return true;
                    }
                    return false;
                }

                case GeometryType.Point:
                {
                    float tolerance = toleranceTileUnits;
                    // For circle layers, use circle-radius as effective radius
                    if (layerType == LayerType.Circle && layer.Paint != null)
                    {
                        var circleProps = StyleParser.ParseCirclePaint(layer.Paint);
                        float radius = circleProps.ResolveCircleRadius(ctx);
                        tolerance = Mathf.Max(tolerance, radius * toleranceTileUnits / 3f);
                    }

                    foreach (var ring in rings)
                    {
                        foreach (var pt in ring)
                        {
                            if (GeometryHitTest.PointNearPoint(queryPoint, pt, tolerance))
                                return true;
                        }
                    }
                    return false;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Build a QueryFeature result from a VectorTileFeature.
        /// </summary>
        private static QueryFeature BuildQueryFeature(
            VectorTileFeature feature, LayerDefinition layer,
            CanonicalTileID tile, List<List<Vector2>> rings,
            string sourceId = null, string sourceLayerName = null)
        {
            // Convert properties
            var rawProps = feature.GetProperties();
            var props = new Dictionary<string, object>(rawProps.Count);
            foreach (var kvp in rawProps)
            {
                props[kvp.Key] = ConvertValue(kvp.Value);
            }

            // Convert geometry to LngLat
            var geometry = new List<List<LngLat>>(rings.Count);
            int n = 1 << tile.Z;
            foreach (var ring in rings)
            {
                var lngLatRing = new List<LngLat>(ring.Count);
                int extent = feature.Layer?.Extent ?? 4096;
                foreach (var pt in ring)
                {
                    double mercX = (tile.X + pt.x / extent) / n;
                    double mercY = (tile.Y + pt.y / extent) / n;
                    lngLatRing.Add(CoordinateConversion.MercatorToLngLat(new MercatorCoordinate(mercX, mercY)));
                }
                geometry.Add(lngLatRing);
            }

            string geomTypeName = feature.Type switch
            {
                GeometryType.Point => "Point",
                GeometryType.LineString => "LineString",
                GeometryType.Polygon => "Polygon",
                _ => "Unknown"
            };

            return new QueryFeature
            {
                Id = feature.Id,
                GeometryType = geomTypeName,
                Properties = props,
                Layer = layer,
                Source = sourceId ?? layer?.Source,
                SourceLayer = sourceLayerName ?? layer?.SourceLayer,
                Geometry = geometry
            };
        }

        private static object ConvertValue(VectorTileValue val)
        {
            if (val.StringValue != null) return val.StringValue;
            if (val.DoubleValue.HasValue) return val.DoubleValue.Value;
            if (val.FloatValue.HasValue) return (double)val.FloatValue.Value;
            if (val.IntValue.HasValue) return (double)val.IntValue.Value;
            if (val.UIntValue.HasValue) return (double)val.UIntValue.Value;
            if (val.SIntValue.HasValue) return (double)val.SIntValue.Value;
            if (val.BoolValue.HasValue) return val.BoolValue.Value;
            return val.ToString();
        }
    }
}
