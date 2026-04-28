using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates <c>line-gradient</c>. A GeoJSON LineString is added at
    /// runtime with <c>lineMetrics: true</c>, and a line layer interpolates
    /// six rainbow colors over <c>["line-progress"]</c> so the route fades
    /// red → violet from start to end.
    ///
    /// Requires a sprite-free vector basemap (openfreemap planet) supplied by
    /// LineGradientDemoStyle.json.
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class LineGradientDemo : MonoBehaviour
    {
        private MapLibreMap _map;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[LineGradientDemo] MapLibreMap component not found");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            // One frame for the basemap parser to settle so the runtime layer
            // lands above the basemap layers in render order.
            yield return null;

            _map.AddSource("yamanote-route", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                LineMetrics = true,
                Data = JToken.Parse(@"{
                    ""type"": ""Feature"",
                    ""geometry"": {
                        ""type"": ""LineString"",
                        ""coordinates"": [
                            [139.7671, 35.6812],
                            [139.7584, 35.6753],
                            [139.7456, 35.6586],
                            [139.7016, 35.6580],
                            [139.7003, 35.6896],
                            [139.7101, 35.7296],
                            [139.7771, 35.7141],
                            [139.7671, 35.6812]
                        ]
                    },
                    ""properties"": { ""name"": ""Yamanote loop"" }
                }")
            });

            // Wait two frames so GeoJsonSource finishes its LoadData coroutine
            // before AddLayer inspects the source.
            yield return null;
            yield return null;

            _map.AddLayer(new LayerDefinition
            {
                Id = "yamanote-gradient",
                Type = LayerType.Line,
                Source = "yamanote-route",
                SourceLayer = "yamanote-route",
                Paint = new Dictionary<string, object>
                {
                    { "line-width", 8 },
                    {
                        "line-gradient",
                        new object[]
                        {
                            "interpolate", new object[] { "linear" }, new object[] { "line-progress" },
                            0.0,  "#ff0000",
                            0.2,  "#ff8c00",
                            0.4,  "#ffd700",
                            0.6,  "#00c853",
                            0.8,  "#1976d2",
                            1.0,  "#9c27b0"
                        }
                    }
                }
            });
        }
    }
}
