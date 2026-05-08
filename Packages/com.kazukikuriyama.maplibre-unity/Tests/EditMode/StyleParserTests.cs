using MapLibre.Unity.Style;
using NUnit.Framework;
using UnityEngine;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Regression coverage of <see cref="StyleParser.Parse"/>. We feed in JSON
    /// snippets shaped like real MapLibre styles and assert that the parsed
    /// <see cref="MapLibreStyle"/> tree comes out the way the renderer
    /// expects. The goal is to lock in the parse contract for fields the
    /// rest of the runtime depends on (sources, layer types, paint props,
    /// glyphs, sprite, light, sky), so silent regressions in the parser
    /// surface as test failures rather than blank tiles.
    /// </summary>
    public class StyleParserTests
    {
        // === Top-level metadata ===

        [Test]
        public void Parse_VersionAndCenter()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""name"": ""TestStyle"",
                ""center"": [139.7, 35.6],
                ""zoom"": 12.5,
                ""bearing"": 45,
                ""pitch"": 30,
                ""sources"": {},
                ""layers"": []
            }");

            Assert.AreEqual(8, style.Version);
            Assert.AreEqual("TestStyle", style.Name);
            Assert.IsNotNull(style.Center);
            Assert.AreEqual(139.7, style.Center.Value.Longitude, 0.001);
            Assert.AreEqual(35.6, style.Center.Value.Latitude, 0.001);
            Assert.AreEqual(12.5f, style.Zoom);
            Assert.AreEqual(45f, style.Bearing);
            Assert.AreEqual(30f, style.Pitch);
        }

        [Test]
        public void Parse_GlyphsAndSpriteUrls()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""glyphs"": ""https://example.com/{fontstack}/{range}.pbf"",
                ""sprite"": ""https://example.com/sprite"",
                ""sources"": {},
                ""layers"": []
            }");

            Assert.AreEqual("https://example.com/{fontstack}/{range}.pbf", style.Glyphs);
            Assert.AreEqual("https://example.com/sprite", style.Sprite);
        }

        // === Sources ===

        [Test]
        public void Parse_RasterSource()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sources"": {
                    ""osm"": {
                        ""type"": ""raster"",
                        ""tiles"": [""https://tile.openstreetmap.org/{z}/{x}/{y}.png""],
                        ""tileSize"": 256,
                        ""minzoom"": 0,
                        ""maxzoom"": 19,
                        ""attribution"": ""© OpenStreetMap contributors""
                    }
                },
                ""layers"": []
            }");

            Assert.IsTrue(style.Sources.ContainsKey("osm"));
            var src = style.Sources["osm"];
            Assert.AreEqual(SourceType.Raster, src.Type);
            Assert.AreEqual(1, src.Tiles.Count);
            Assert.AreEqual("https://tile.openstreetmap.org/{z}/{x}/{y}.png", src.Tiles[0]);
            Assert.AreEqual(256, src.TileSize);
            Assert.AreEqual(0, src.MinZoom);
            Assert.AreEqual(19, src.MaxZoom);
            Assert.AreEqual("© OpenStreetMap contributors", src.Attribution);
        }

        [Test]
        public void Parse_VectorSourceWithUrl()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sources"": {
                    ""maplibre"": {
                        ""type"": ""vector"",
                        ""url"": ""https://demotiles.maplibre.org/tiles/tiles.json""
                    }
                },
                ""layers"": []
            }");

            var src = style.Sources["maplibre"];
            Assert.AreEqual(SourceType.Vector, src.Type);
            Assert.AreEqual("https://demotiles.maplibre.org/tiles/tiles.json", src.Url);
        }

        [Test]
        public void Parse_RasterDemSource_DefaultsToMapboxEncoding()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sources"": {
                    ""dem"": {
                        ""type"": ""raster-dem"",
                        ""tiles"": [""https://example.com/{z}/{x}/{y}.png""],
                        ""tileSize"": 256
                    }
                },
                ""layers"": []
            }");

            var src = style.Sources["dem"];
            Assert.AreEqual(SourceType.RasterDem, src.Type);
            Assert.AreEqual("mapbox", src.Encoding);
        }

        [Test]
        public void Parse_RasterDemSource_HonoursTerrariumEncoding()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sources"": {
                    ""dem"": {
                        ""type"": ""raster-dem"",
                        ""tiles"": [""https://example.com/{z}/{x}/{y}.png""],
                        ""encoding"": ""terrarium""
                    }
                },
                ""layers"": []
            }");

            Assert.AreEqual("terrarium", style.Sources["dem"].Encoding);
        }

        [Test]
        public void Parse_GeoJsonSource_WithInlineData()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sources"": {
                    ""points"": {
                        ""type"": ""geojson"",
                        ""data"": {
                            ""type"": ""FeatureCollection"",
                            ""features"": []
                        },
                        ""cluster"": true,
                        ""clusterRadius"": 80,
                        ""clusterMaxZoom"": 14
                    }
                },
                ""layers"": []
            }");

            var src = style.Sources["points"];
            Assert.AreEqual(SourceType.GeoJson, src.Type);
            Assert.IsNotNull(src.Data);
            Assert.IsTrue(src.Cluster);
            Assert.AreEqual(80, src.ClusterRadius);
            Assert.AreEqual(14, src.ClusterMaxZoom);
        }

        // === Layers ===

        [Test]
        public void Parse_BackgroundLayer_PreservesPaintDictionary()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sources"": {},
                ""layers"": [
                    { ""id"": ""bg"", ""type"": ""background"",
                      ""paint"": { ""background-color"": ""#ffffff"" } }
                ]
            }");

            Assert.AreEqual(1, style.Layers.Count);
            var layer = style.Layers[0];
            Assert.AreEqual("bg", layer.Id);
            Assert.AreEqual(LayerType.Background, layer.Type);
            Assert.IsTrue(layer.Paint.ContainsKey("background-color"));
        }

        [TestCase("fill", LayerType.Fill)]
        [TestCase("line", LayerType.Line)]
        [TestCase("symbol", LayerType.Symbol)]
        [TestCase("raster", LayerType.Raster)]
        [TestCase("circle", LayerType.Circle)]
        [TestCase("fill-extrusion", LayerType.FillExtrusion)]
        [TestCase("heatmap", LayerType.Heatmap)]
        [TestCase("hillshade", LayerType.Hillshade)]
        public void Parse_LayerTypes(string typeStr, LayerType expected)
        {
            string json = "{" +
                "\"version\":8," +
                "\"sources\":{}," +
                "\"layers\":[{\"id\":\"x\",\"type\":\"" + typeStr + "\",\"source\":\"s\"}]}";
            var style = StyleParser.Parse(json);
            Assert.AreEqual(expected, style.Layers[0].Type);
        }

        [Test]
        public void Parse_Layer_MinZoomMaxZoomVisibility()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sources"": {},
                ""layers"": [
                    {
                        ""id"": ""hidden"",
                        ""type"": ""fill"",
                        ""source"": ""src"",
                        ""minzoom"": 10,
                        ""maxzoom"": 16,
                        ""layout"": { ""visibility"": ""none"" }
                    }
                ]
            }");

            var layer = style.Layers[0];
            Assert.AreEqual(10f, layer.MinZoom);
            Assert.AreEqual(16f, layer.MaxZoom);
            Assert.AreEqual("none", layer.Visibility);
            Assert.IsFalse(layer.IsVisibleAtZoom(12f),
                "visibility:none should always hide the layer");
        }

        [Test]
        public void Parse_Layer_FilterExpression()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sources"": {},
                ""layers"": [
                    {
                        ""id"": ""roads"",
                        ""type"": ""line"",
                        ""source"": ""src"",
                        ""filter"": [""=="", [""get"", ""class""], ""primary""]
                    }
                ]
            }");

            Assert.IsNotNull(style.Layers[0].Filter,
                "Filter expression should be parsed into the Filter property");
        }

        // === Light ===

        [Test]
        public void Parse_LightDefinition()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""light"": {
                    ""anchor"": ""map"",
                    ""position"": [1.0, 90, 60],
                    ""color"": ""#ffeecc"",
                    ""intensity"": 0.7
                },
                ""sources"": {},
                ""layers"": []
            }");

            Assert.IsNotNull(style.Light);
            Assert.AreEqual("map", style.Light.Anchor);
            Assert.AreEqual(3, style.Light.Position.Length);
            Assert.AreEqual(1.0f, style.Light.Position[0]);
            Assert.AreEqual(90f, style.Light.Position[1]);
            Assert.AreEqual(60f, style.Light.Position[2]);
            Assert.AreEqual(0.7f, style.Light.Intensity);
        }

        // === Sky ===

        [Test]
        public void Parse_SkyDefinition()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sky"": {
                    ""sky-color"": ""#3366cc"",
                    ""horizon-color"": ""#aaccee"",
                    ""sky-horizon-blend"": 0.5
                },
                ""sources"": {},
                ""layers"": []
            }");

            Assert.IsNotNull(style.Sky);
            Assert.AreEqual(0.5f, style.Sky.SkyHorizonBlend, 0.001f);
        }

        // === Symbol layout ===

        [Test]
        public void Parse_SymbolLayout_IconTextFit()
        {
            var style = StyleParser.Parse(@"{
                ""version"": 8,
                ""sources"": {},
                ""layers"": [
                    {
                        ""id"": ""shield"",
                        ""type"": ""symbol"",
                        ""source"": ""src"",
                        ""layout"": {
                            ""icon-image"": ""highway-shield"",
                            ""text-field"": ""I-90"",
                            ""icon-text-fit"": ""both"",
                            ""icon-text-fit-padding"": [4, 8, 4, 8]
                        }
                    }
                ]
            }");

            var layer = style.Layers[0];
            Assert.IsNotNull(layer.Layout);
            // The renderer reads SymbolLayoutProperties at draw-time so the
            // raw layout dict is enough -- Parse just needs to keep the values.
            Assert.AreEqual("both", layer.Layout["icon-text-fit"]);
            // The padding ends up as a List<object> at this layer (parsed by
            // Newtonsoft.Json and stored in the generic dict).
            Assert.IsInstanceOf<System.Collections.IList>(layer.Layout["icon-text-fit-padding"]);
        }

        [Test]
        public void Parse_SymbolLayoutProperties_IconTextFitPadding()
        {
            // Drive the typed accessor through StyleParser.ParseSymbolLayout so
            // we cover the [top,right,bottom,left] expansion path.
            var layout = new System.Collections.Generic.Dictionary<string, object>
            {
                ["icon-text-fit"] = "width",
                ["icon-text-fit-padding"] = new System.Collections.Generic.List<object>
                {
                    2.0, 4.0, 2.0, 4.0
                },
            };
            var props = StyleParser.ParseSymbolLayout(layout);
            Assert.AreEqual("width", props.IconTextFit);
            Assert.AreEqual(2f, props.IconTextFitPadding[0]);
            Assert.AreEqual(4f, props.IconTextFitPadding[1]);
            Assert.AreEqual(2f, props.IconTextFitPadding[2]);
            Assert.AreEqual(4f, props.IconTextFitPadding[3]);
        }

        // === Smoke / round-trip ===

        [Test]
        public void Parse_FullStyleSnippet_DoesNotThrow()
        {
            // A representative style touching every supported subsystem so the
            // parser is exercised end-to-end. Equivalent to what a real style
            // from a tile provider might look like.
            string json = @"{
                ""version"": 8,
                ""name"": ""Smoke"",
                ""center"": [0, 0], ""zoom"": 1,
                ""glyphs"": ""https://example.com/{fontstack}/{range}.pbf"",
                ""sprite"": ""https://example.com/sprite"",
                ""terrain"": { ""source"": ""dem"", ""exaggeration"": 1.5 },
                ""light"": { ""anchor"": ""viewport"" },
                ""sky"":   { ""sky-color"": ""#0066cc"" },
                ""sources"": {
                    ""dem"":     { ""type"": ""raster-dem"", ""tiles"": [""https://x/{z}/{x}/{y}.png""] },
                    ""basemap"": { ""type"": ""raster"",     ""tiles"": [""https://x/{z}/{x}/{y}.png""] },
                    ""mvt"":     { ""type"": ""vector"",     ""url"":   ""https://x/tiles.json"" },
                    ""points"":  { ""type"": ""geojson"",    ""data"":  { ""type"":""FeatureCollection"", ""features"":[] } }
                },
                ""layers"": [
                    { ""id"": ""bg"",  ""type"": ""background"", ""paint"": {""background-color"":""#fff""} },
                    { ""id"": ""r"",   ""type"": ""raster"",     ""source"": ""basemap"" },
                    { ""id"": ""hs"",  ""type"": ""hillshade"",  ""source"": ""dem"" },
                    { ""id"": ""f"",   ""type"": ""fill"",       ""source"": ""mvt"", ""source-layer"": ""landuse"" },
                    { ""id"": ""ln"",  ""type"": ""line"",       ""source"": ""mvt"", ""source-layer"": ""roads"" },
                    { ""id"": ""sym"", ""type"": ""symbol"",     ""source"": ""mvt"", ""source-layer"": ""places"" },
                    { ""id"": ""c"",   ""type"": ""circle"",     ""source"": ""points"" },
                    { ""id"": ""ext"", ""type"": ""fill-extrusion"", ""source"": ""mvt"", ""source-layer"": ""buildings"" },
                    { ""id"": ""ht"",  ""type"": ""heatmap"",    ""source"": ""points"" }
                ]
            }";

            // We don't assert per-field -- the per-feature tests above cover
            // that. This case is the integration smoke: every subsystem
            // listed parses without throwing, and the result has the
            // expected layer count.
            var style = StyleParser.Parse(json);
            Assert.AreEqual(9, style.Layers.Count);
            Assert.AreEqual(4, style.Sources.Count);
            Assert.IsNotNull(style.Glyphs);
            Assert.IsNotNull(style.Sprite);
            Assert.IsNotNull(style.Light);
            Assert.IsNotNull(style.Sky);
        }
    }
}
