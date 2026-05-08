using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates the runtime addSource / addLayer API matching MapLibre GL JS.
    /// Attach this MonoBehaviour to the same GameObject as MapLibreMap.
    ///
    /// Usage:
    ///   map.addSource('id', { type: 'geojson', data: { ... } })
    ///   map.addLayer({ id: 'layer', type: 'circle', source: 'id', paint: { ... } })
    /// </summary>
    public class RuntimeGeoJsonDemo : MonoBehaviour
    {
        [Header("Timing")]
        [SerializeField] private float _initialDelay = 2f;
        [SerializeField] private float _addRouteDelay = 4f;
        [SerializeField] private float _updateDataDelay = 8f;

        private MapLibreMap _map;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[RuntimeGeoJsonDemo] MapLibreMap component not found on this GameObject");
                yield break;
            }

            // Wait for map initialization
            while (!_map.IsInitialized)
                yield return null;

            // === Step 1: addSource + addLayer (batch) ===
            yield return new WaitForSeconds(_initialDelay);
            Debug.Log("[RuntimeGeoJsonDemo] Adding GeoJSON source and layers...");

            // Add source
            _map.AddSource("tokyo-stations", new SourceDefinition
            {
                Type = SourceType.GeoJson,
                Data = JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7671, 35.6812] },
                            ""properties"": { ""name"": ""Tokyo"", ""passengers"": 462000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7016, 35.6580] },
                            ""properties"": { ""name"": ""Shibuya"", ""passengers"": 366000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7003, 35.6896] },
                            ""properties"": { ""name"": ""Shinjuku"", ""passengers"": 775000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7101, 35.7296] },
                            ""properties"": { ""name"": ""Ikebukuro"", ""passengers"": 558000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7771, 35.7141] },
                            ""properties"": { ""name"": ""Ueno"", ""passengers"": 187000 }
                        }
                    ]
                }")
            });

            // Wait a frame for GeoJSON data to load (LoadData coroutine)
            yield return null;
            yield return null;

            // Add circle + symbol layers in batch (triggerRefresh: false),
            // then call RefreshTiles once at the end.
            _map.AddLayer(new LayerDefinition
            {
                Id = "station-circles",
                Type = LayerType.Circle,
                Source = "tokyo-stations",
                SourceLayer = "tokyo-stations",
                Paint = new Dictionary<string, object>
                {
                    { "circle-radius", 10 },
                    { "circle-color", "#E91E63" },
                    { "circle-opacity", 0.85 },
                    { "circle-stroke-width", 2 },
                    { "circle-stroke-color", "#ffffff" }
                }
            }, triggerRefresh: false);

            _map.AddLayer(new LayerDefinition
            {
                Id = "station-labels",
                Type = LayerType.Symbol,
                Source = "tokyo-stations",
                SourceLayer = "tokyo-stations",
                Layout = new Dictionary<string, object>
                {
                    { "text-field", "{name}" },
                    { "text-size", 14 }
                },
                Paint = new Dictionary<string, object>
                {
                    { "text-color", "#1A237E" },
                    { "text-halo-color", "#ffffff" },
                    { "text-halo-width", 1.5 }
                }
            }, triggerRefresh: false);

            // Single refresh for both layers
            _map.RefreshTiles();
            Debug.Log("[RuntimeGeoJsonDemo] Stations (circles + labels) added");

            // === Step 2: addSource + addLayer (route line) ===
            yield return new WaitForSeconds(_addRouteDelay - _initialDelay);
            Debug.Log("[RuntimeGeoJsonDemo] Adding route source and line layer...");

            _map.AddSource("yamanote-route", new SourceDefinition
            {
                Type = SourceType.GeoJson,
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
                    ""properties"": { ""name"": ""Yamanote Line"" }
                }")
            });

            yield return null;
            yield return null;

            _map.AddLayer(new LayerDefinition
            {
                Id = "yamanote-line",
                Type = LayerType.Line,
                Source = "yamanote-route",
                SourceLayer = "yamanote-route",
                Paint = new Dictionary<string, object>
                {
                    { "line-color", "#4CAF50" },
                    { "line-width", 5 },
                    { "line-opacity", 0.8 }
                }
            });
            Debug.Log("[RuntimeGeoJsonDemo] Yamanote line added");

            // === Step 3: setData (update existing source) ===
            yield return new WaitForSeconds(_updateDataDelay - _addRouteDelay);
            Debug.Log("[RuntimeGeoJsonDemo] Updating GeoJSON data with setData()...");

            var source = _map.GetGeoJsonSource("tokyo-stations");
            if (source != null)
            {
                source.SetData(JToken.Parse(@"{
                    ""type"": ""FeatureCollection"",
                    ""features"": [
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7671, 35.6812] },
                            ""properties"": { ""name"": ""Tokyo"", ""passengers"": 462000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7016, 35.6580] },
                            ""properties"": { ""name"": ""Shibuya"", ""passengers"": 366000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7003, 35.6896] },
                            ""properties"": { ""name"": ""Shinjuku"", ""passengers"": 775000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7101, 35.7296] },
                            ""properties"": { ""name"": ""Ikebukuro"", ""passengers"": 558000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7771, 35.7141] },
                            ""properties"": { ""name"": ""Ueno"", ""passengers"": 187000 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7454, 35.6586] },
                            ""properties"": { ""name"": ""Tokyo Tower"", ""passengers"": 0 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.8107, 35.7101] },
                            ""properties"": { ""name"": ""Skytree"", ""passengers"": 0 }
                        },
                        {
                            ""type"": ""Feature"",
                            ""geometry"": { ""type"": ""Point"", ""coordinates"": [139.7454, 35.6939] },
                            ""properties"": { ""name"": ""Akihabara"", ""passengers"": 245000 }
                        }
                    ]
                }"));

                _map.RefreshTiles();
                Debug.Log("[RuntimeGeoJsonDemo] Data updated -- added 3 new points");
            }
        }
    }
}
