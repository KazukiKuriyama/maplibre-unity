using System.Collections.Generic;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;

namespace MapLibre.Unity
{
    /// <summary>
    /// Result of queryRenderedFeatures() / querySourceFeatures().
    /// Matches the GeoJSON Feature object returned by MapLibre GL JS.
    /// </summary>
    public class QueryFeature
    {
        /// <summary>Feature ID (0 if not set in the source data).</summary>
        public ulong Id;

        /// <summary>Geometry type: "Point", "LineString", or "Polygon".</summary>
        public string GeometryType;

        /// <summary>
        /// Feature properties as key-value pairs.
        /// Values are string, double, or bool matching the MVT encoding.
        /// </summary>
        public Dictionary<string, object> Properties;

        /// <summary>Style layer that matched this feature.</summary>
        public LayerDefinition Layer;

        /// <summary>Source ID.</summary>
        public string Source;

        /// <summary>Source layer name within the vector tile.</summary>
        public string SourceLayer;

        /// <summary>
        /// Decoded geometry as rings of LngLat coordinates.
        /// For Point: single ring with single coordinate.
        /// For LineString: one ring per line.
        /// For Polygon: first ring is exterior, subsequent are holes.
        /// </summary>
        public List<List<LngLat>> Geometry;
    }

    /// <summary>
    /// Options for queryRenderedFeatures(). Matches MapLibre GL JS options parameter.
    /// </summary>
    public class QueryOptions
    {
        /// <summary>
        /// Only return features from these layer IDs. null means all layers.
        /// </summary>
        public string[] Layers;

        /// <summary>
        /// Additional filter expression applied on top of the layer's own filter.
        /// </summary>
        public Expression Filter;

        /// <summary>
        /// Hit-test tolerance in pixels for point and line features.
        /// Default is 3 pixels, matching MapLibre GL JS default.
        /// </summary>
        public float Tolerance = 3f;
    }

    /// <summary>
    /// Options for querySourceFeatures(). Matches MapLibre GL JS options parameter.
    /// </summary>
    public class QuerySourceOptions
    {
        /// <summary>
        /// Source layer to query. Required for vector tile sources.
        /// </summary>
        public string SourceLayer;

        /// <summary>
        /// Filter expression to apply.
        /// </summary>
        public Expression Filter;
    }
}
