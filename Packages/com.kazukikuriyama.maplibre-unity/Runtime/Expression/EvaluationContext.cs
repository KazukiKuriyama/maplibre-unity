using MapLibre.Unity.VectorTile;

namespace MapLibre.Unity.Expressions
{
    /// <summary>
    /// Lookup interface for runtime feature-state values. Implemented by MapLibreMap so
    /// expressions can resolve <c>["feature-state", "key"]</c> against the live state
    /// store without a hard dependency on the Core layer.
    /// </summary>
    public interface IFeatureStateStore
    {
        /// <summary>
        /// Returns the state value for (sourceId, sourceLayer, featureId, key) or null
        /// if no state has been set. sourceLayer is empty for non-vector sources.
        /// </summary>
        object GetFeatureState(string sourceId, string sourceLayer, long featureId, string key);
    }

    public struct EvaluationContext
    {
        public float Zoom;
        public VectorTileFeature Feature;

        /// <summary>
        /// Heatmap density value (0..1) used by the ["heatmap-density"] expression.
        /// Set during heatmap color ramp baking.
        /// </summary>
        public float HeatmapDensity;

        /// <summary>
        /// Normalised progress (0..1) along the current line feature, used by the
        /// <c>["line-progress"]</c> expression while baking the line-gradient ramp.
        /// </summary>
        public float LineProgress;

        /// <summary>
        /// Source id the feature belongs to. Required for ["feature-state", ...]
        /// lookups; the same feature id can exist in multiple sources.
        /// </summary>
        public string SourceId;

        /// <summary>
        /// source-layer name from the vector tile (the layer under
        /// <c>source-layer</c> in the style). Empty for GeoJSON / non-vector sources.
        /// </summary>
        public string SourceLayer;

        /// <summary>
        /// Lookup hook for runtime feature-state. Null when no state has been
        /// registered (the expression then resolves to null and downstream
        /// coalesce / case branches take over).
        /// </summary>
        public IFeatureStateStore FeatureStateStore;

        public EvaluationContext(float zoom)
        {
            Zoom = zoom;
            Feature = null;
            HeatmapDensity = 0f;
            LineProgress = 0f;
            SourceId = null;
            SourceLayer = null;
            FeatureStateStore = null;
        }

        public EvaluationContext(float zoom, VectorTileFeature feature)
        {
            Zoom = zoom;
            Feature = feature;
            HeatmapDensity = 0f;
            LineProgress = 0f;
            SourceId = null;
            SourceLayer = null;
            FeatureStateStore = null;
        }
    }
}
