using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates the difference between <c>interpolate</c>,
    /// <c>interpolate-hcl</c>, and <c>interpolate-lab</c> color interpolation.
    /// Three side-by-side polygons each interpolate from red→blue across the
    /// camera's zoom range, but in different color spaces. Zoom in / out and
    /// watch the midpoint colour:
    /// <list type="bullet">
    ///   <item>RGB drifts through a desaturated grey-purple at midzoom.</item>
    ///   <item>HCL stays vivid, sweeping the hue circle through magenta.</item>
    ///   <item>Lab stays bright but takes a slightly different perceptual path.</item>
    /// </list>
    /// Attach to the same GameObject as MapLibreMap.
    /// </summary>
    public class ColorInterpolationDemo : MonoBehaviour
    {
        private MapLibreMap _map;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[ColorInterpolationDemo] MapLibreMap not found");
                yield break;
            }

            while (!_map.IsInitialized) yield return null;
            yield return null;

            AddSource();
            AddLayer("rgb-fill",      "rgb",          "interpolate");
            AddLayer("hcl-fill",      "hcl",          "interpolate-hcl");
            AddLayer("lab-fill",      "lab",          "interpolate-lab");
        }

        private void AddSource()
        {
            // Three side-by-side rectangles around Tokyo, each tagged with the
            // colour-space we want to demonstrate. The fill layers below pick
            // their feature with a filter on `space`.
            _map.AddSource("interp-zones", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        { ""type"": ""Feature"", ""id"": 1,
                          ""geometry"": { ""type"": ""Polygon"",
                            ""coordinates"": [[[139.700,35.660],[139.700,35.700],[139.730,35.700],[139.730,35.660],[139.700,35.660]]] },
                          ""properties"": { ""space"": ""rgb"" } },
                        { ""type"": ""Feature"", ""id"": 2,
                          ""geometry"": { ""type"": ""Polygon"",
                            ""coordinates"": [[[139.735,35.660],[139.735,35.700],[139.765,35.700],[139.765,35.660],[139.735,35.660]]] },
                          ""properties"": { ""space"": ""hcl"" } },
                        { ""type"": ""Feature"", ""id"": 3,
                          ""geometry"": { ""type"": ""Polygon"",
                            ""coordinates"": [[[139.770,35.660],[139.770,35.700],[139.800,35.700],[139.800,35.660],[139.770,35.660]]] },
                          ""properties"": { ""space"": ""lab"" } }
                    ]
                }")
            });
        }

        private void AddLayer(string layerId, string spaceFilter, string interpolateOp)
        {
            // Each layer scopes itself to the matching polygon via filter, then
            // applies the same red→blue zoom ramp through one of the three
            // interpolate flavours.
            _map.AddLayer(new LayerDefinition
            {
                Id = layerId,
                Type = LayerType.Fill,
                Source = "interp-zones",
                SourceLayer = "interp-zones",
                Filter = MapLibre.Unity.Expressions.ExpressionParser.ParseFilter(JToken.FromObject(
                    new object[] { "==", new object[] { "get", "space" }, spaceFilter })),
                Paint = new Dictionary<string, object>
                {
                    {
                        "fill-color",
                        new object[]
                        {
                            interpolateOp,
                            new object[] { "linear" },
                            new object[] { "zoom" },
                            8,  "#ff0000",
                            12, "#0000ff"
                        }
                    },
                    { "fill-opacity", 0.85 }
                }
            }, triggerRefresh: false);
        }
    }
}
