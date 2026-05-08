using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Visualises the four combinations of <c>circle-pitch-alignment</c> ×
    /// <c>circle-pitch-scale</c>:
    ///
    /// <list type="bullet">
    /// <item>map / map        -- flat on the ground; size shrinks with pitch.</item>
    /// <item>map / viewport   -- flat on the ground; size stays in screen px.</item>
    /// <item>viewport / map   -- billboards facing camera; size shrinks with depth (spec default).</item>
    /// <item>viewport / viewport -- billboards facing camera; size stays in screen px.</item>
    /// </list>
    ///
    /// Four columns of GeoJSON points (one column per combination) all use
    /// <c>circle-radius: 18</c>. The scene starts at pitch=55° so the
    /// differences are obvious immediately. Middle-drag to change pitch and
    /// watch how each column reacts.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class CirclePitchDemo : MonoBehaviour
    {
        // Four columns spread across longitude, each with a vertical strip of
        // points so the depth-scaling effect is easy to read.
        private static readonly (string SourceId, double Longitude, string Color)[] Columns =
        {
            ("col-map-map",          139.700, "#e53935"),
            ("col-map-viewport",     139.730, "#fb8c00"),
            ("col-viewport-map",     139.760, "#43a047"),
            ("col-viewport-viewport",139.790, "#1e88e5"),
        };

        private MapLibreMap _map;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[CirclePitchDemo] MapLibreMap component not found");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            yield return null;

            for (int i = 0; i < Columns.Length; i++)
            {
                var (id, lng, _) = Columns[i];
                _map.AddSource(id, new SourceDefinition
                {
                    Type = SourceType.GeoJson,
                    Data = JToken.Parse(BuildColumnGeoJson(lng))
                });
            }

            yield return null;
            yield return null;

            // Each layer fixes one (alignment, scale) pairing so the visual
            // difference between columns is purely the paint property combo.
            AddCircleLayer("layer-map-map",
                Columns[0].SourceId, Columns[0].Color, "map", "map");
            AddCircleLayer("layer-map-viewport",
                Columns[1].SourceId, Columns[1].Color, "map", "viewport");
            AddCircleLayer("layer-viewport-map",
                Columns[2].SourceId, Columns[2].Color, "viewport", "map");
            AddCircleLayer("layer-viewport-viewport",
                Columns[3].SourceId, Columns[3].Color, "viewport", "viewport");
        }

        private void AddCircleLayer(string id, string source, string color,
            string pitchAlign, string pitchScale)
        {
            _map.AddLayer(new LayerDefinition
            {
                Id = id,
                Type = LayerType.Circle,
                Source = source,
                SourceLayer = source,
                Paint = new Dictionary<string, object>
                {
                    { "circle-radius", 18 },
                    { "circle-color", color },
                    { "circle-opacity", 0.85 },
                    { "circle-stroke-width", 2 },
                    { "circle-stroke-color", "#ffffff" },
                    { "circle-pitch-alignment", pitchAlign },
                    { "circle-pitch-scale", pitchScale }
                }
            }, triggerRefresh: false);
        }

        // Six points down a vertical line at the given longitude. Spreading
        // them in latitude creates the visible depth gradient when the camera
        // is pitched.
        private static string BuildColumnGeoJson(double lng)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(@"{ ""type"": ""FeatureCollection"", ""features"": [");
            for (int i = 0; i < 6; i++)
            {
                if (i > 0) sb.Append(',');
                double lat = 35.640 + i * 0.012;
                sb.Append("{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[");
                sb.Append(lng.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(lat.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append("]},\"properties\":{}}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
