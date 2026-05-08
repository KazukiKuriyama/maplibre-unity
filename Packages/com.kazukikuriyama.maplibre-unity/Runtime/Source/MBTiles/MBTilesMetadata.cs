using System;
using System.Collections.Generic;

namespace MapLibre.Unity.Source.MBTiles
{
    /// <summary>
    /// Strongly-typed view over an MBTiles <c>metadata</c> table. Spec keys
    /// the renderer actually consumes -- name / format / bounds / min/max
    /// zoom / attribution. Other rows are still preserved on the underlying
    /// dictionary so callers that need them can read them directly.
    /// </summary>
    public sealed class MBTilesMetadata
    {
        public string Name;
        /// <summary>"png", "jpg", "webp", or "pbf" (vector). MBTiles spec key.</summary>
        public string Format;
        public string Attribution;
        public int MinZoom;
        public int MaxZoom = 22;
        /// <summary>[west, south, east, north] in degrees, parsed from the comma-string.</summary>
        public double[] Bounds;
        /// <summary>Raw rows so callers can read non-standard metadata.</summary>
        public Dictionary<string, string> Raw = new();

        public bool IsVector => Format == "pbf";

        public static MBTilesMetadata Parse(Dictionary<string, string> raw)
        {
            var m = new MBTilesMetadata { Raw = raw ?? new Dictionary<string, string>() };
            if (raw == null) return m;

            if (raw.TryGetValue("name", out var name))   m.Name = name;
            if (raw.TryGetValue("format", out var fmt))  m.Format = fmt;
            if (raw.TryGetValue("attribution", out var a)) m.Attribution = a;
            if (raw.TryGetValue("minzoom", out var minz) && int.TryParse(minz, out int mz))
                m.MinZoom = mz;
            if (raw.TryGetValue("maxzoom", out var maxz) && int.TryParse(maxz, out int xz))
                m.MaxZoom = xz;
            if (raw.TryGetValue("bounds", out var b))
            {
                // Spec format: "west,south,east,north".
                var parts = b.Split(',');
                if (parts.Length == 4
                    && double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double w)
                    && double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double s)
                    && double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double e)
                    && double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double n))
                {
                    m.Bounds = new[] { w, s, e, n };
                }
            }
            return m;
        }
    }
}
