using MapLibre.Unity.Expressions;

namespace MapLibre.Unity.Style
{
    /// <summary>
    /// Root-level "terrain" property of a MapLibre style.
    /// References a raster-dem source and applies vertex displacement to map content.
    /// See: https://maplibre.org/maplibre-style-spec/terrain/
    /// </summary>
    [System.Serializable]
    public class TerrainDefinition
    {
        /// <summary>ID of the raster-dem source providing elevation data. Required.</summary>
        public string Source;

        /// <summary>
        /// Multiplier applied to the elevation. 0 = flat, 1 = real-world scale (default per spec).
        /// Spec range: 0 to 1000. Supports zoom expressions.
        /// </summary>
        public Expression Exaggeration;

        public float ResolveExaggeration(EvaluationContext ctx)
        {
            if (Exaggeration == null) return 1f;
            return Exaggeration.EvaluateFloat(ctx, 1f);
        }
    }
}
