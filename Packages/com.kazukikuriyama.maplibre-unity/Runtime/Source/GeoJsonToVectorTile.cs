using System;
using System.Collections.Generic;
using MapLibre.Unity.VectorTile;
using Newtonsoft.Json.Linq;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Converts GeoJSON features into VectorTileData for a specific tile.
    /// Coordinates are projected from WGS84 to Mercator, then to tile-local MVT space [0, extent].
    /// </summary>
    public static class GeoJsonToVectorTile
    {
        private const int DefaultExtent = 4096;

        /// <summary>
        /// Parsed GeoJSON feature stored in memory for spatial queries.
        /// </summary>
        public class GeoJsonFeature
        {
            public ulong Id;
            public GeometryType Type;
            public string GeometryTypeName;
            /// <summary>
            /// For Point: [[x,y]]
            /// For MultiPoint: [[x,y], [x,y], ...]
            /// For LineString: [[x,y], [x,y], ...]
            /// For MultiLineString: [[[x,y], ...], [[x,y], ...]]
            /// For Polygon: [[[x,y], ...], [[x,y], ...]] (first=exterior, rest=holes)
            /// For MultiPolygon: [[[[x,y], ...], ...], ...]
            /// All coordinates stored as Mercator [0,1].
            /// </summary>
            public List<List<double[]>> Rings;
            public Dictionary<string, object> Properties;
            /// <summary>Mercator bounding box: minX, minY, maxX, maxY</summary>
            public double MinX, MinY, MaxX, MaxY;
        }

        /// <summary>
        /// Parse a GeoJSON JToken (FeatureCollection, Feature, or bare Geometry)
        /// into a list of GeoJsonFeature with Mercator coordinates.
        /// </summary>
        public static List<GeoJsonFeature> ParseGeoJson(JToken token)
        {
            var features = new List<GeoJsonFeature>();
            if (token == null) return features;

            string type = token["type"]?.ToString();
            switch (type)
            {
                case "FeatureCollection":
                    if (token["features"] is JArray arr)
                    {
                        ulong autoId = 1;
                        foreach (var ft in arr)
                        {
                            var parsed = ParseFeature(ft, autoId);
                            if (parsed != null)
                            {
                                features.Add(parsed);
                                autoId++;
                            }
                        }
                    }
                    break;

                case "Feature":
                    var f = ParseFeature(token, 1);
                    if (f != null) features.Add(f);
                    break;

                default:
                    // Bare geometry
                    var geom = ParseFeatureFromGeometry(token, 1);
                    if (geom != null) features.Add(geom);
                    break;
            }

            return features;
        }

        private static GeoJsonFeature ParseFeature(JToken token, ulong autoId)
        {
            var geomToken = token["geometry"];
            if (geomToken == null || geomToken.Type == JTokenType.Null) return null;

            var feature = ParseFeatureFromGeometry(geomToken, autoId);
            if (feature == null) return null;

            // ID
            var idToken = token["id"];
            if (idToken != null)
            {
                if (idToken.Type == JTokenType.Integer)
                    feature.Id = (ulong)idToken.ToObject<long>();
                else
                    feature.Id = autoId;
            }

            // Properties
            if (token["properties"] is JObject propsObj)
            {
                feature.Properties = new Dictionary<string, object>();
                foreach (var kvp in propsObj)
                {
                    feature.Properties[kvp.Key] = ConvertJValue(kvp.Value);
                }
            }

            return feature;
        }

        private static GeoJsonFeature ParseFeatureFromGeometry(JToken geomToken, ulong autoId)
        {
            string geomType = geomToken["type"]?.ToString();
            var coordsToken = geomToken["coordinates"];
            if (coordsToken == null && geomType != "GeometryCollection") return null;

            var feature = new GeoJsonFeature
            {
                Id = autoId,
                GeometryTypeName = geomType,
                Properties = new Dictionary<string, object>(),
                Rings = new List<List<double[]>>(),
                MinX = double.MaxValue,
                MinY = double.MaxValue,
                MaxX = double.MinValue,
                MaxY = double.MinValue
            };

            switch (geomType)
            {
                case "Point":
                    feature.Type = GeometryType.Point;
                    var pt = ParseCoordinate(coordsToken);
                    feature.Rings.Add(new List<double[]> { pt });
                    ExpandBounds(feature, pt);
                    break;

                case "MultiPoint":
                    feature.Type = GeometryType.Point;
                    foreach (var c in coordsToken)
                    {
                        var mpt = ParseCoordinate(c);
                        feature.Rings.Add(new List<double[]> { mpt });
                        ExpandBounds(feature, mpt);
                    }
                    break;

                case "LineString":
                    feature.Type = GeometryType.LineString;
                    feature.Rings.Add(ParseCoordinateArray(coordsToken, feature));
                    break;

                case "MultiLineString":
                    feature.Type = GeometryType.LineString;
                    foreach (var line in coordsToken)
                        feature.Rings.Add(ParseCoordinateArray(line, feature));
                    break;

                case "Polygon":
                    feature.Type = GeometryType.Polygon;
                    foreach (var ring in coordsToken)
                        feature.Rings.Add(ParseCoordinateArray(ring, feature));
                    break;

                case "MultiPolygon":
                    // Flatten multi-polygon into separate polygon features would be complex.
                    // For simplicity, treat each polygon's rings as part of a single feature.
                    feature.Type = GeometryType.Polygon;
                    foreach (var polygon in coordsToken)
                    {
                        foreach (var ring in polygon)
                            feature.Rings.Add(ParseCoordinateArray(ring, feature));
                    }
                    break;

                default:
                    return null;
            }

            return feature;
        }

        private static double[] ParseCoordinate(JToken token)
        {
            double lng = token[0].ToObject<double>();
            double lat = token[1].ToObject<double>();
            double mx = (lng + 180.0) / 360.0;
            double latRad = lat * Math.PI / 180.0;
            double my = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0;
            return new[] { mx, my };
        }

        private static List<double[]> ParseCoordinateArray(JToken token, GeoJsonFeature feature)
        {
            var coords = new List<double[]>();
            foreach (var c in token)
            {
                var pt = ParseCoordinate(c);
                coords.Add(pt);
                ExpandBounds(feature, pt);
            }
            return coords;
        }

        private static void ExpandBounds(GeoJsonFeature feature, double[] pt)
        {
            if (pt[0] < feature.MinX) feature.MinX = pt[0];
            if (pt[1] < feature.MinY) feature.MinY = pt[1];
            if (pt[0] > feature.MaxX) feature.MaxX = pt[0];
            if (pt[1] > feature.MaxY) feature.MaxY = pt[1];
        }

        private static object ConvertJValue(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.String: return token.ToString();
                case JTokenType.Integer: return token.ToObject<long>();
                case JTokenType.Float: return token.ToObject<double>();
                case JTokenType.Boolean: return token.ToObject<bool>();
                case JTokenType.Null: return null;
                default: return token.ToString();
            }
        }

        /// <summary>
        /// Generate VectorTileData for a specific tile from pre-parsed GeoJSON features.
        /// Features are filtered by tile bounds and coordinates are projected to tile-local space.
        /// </summary>
        /// <param name="features">Pre-parsed GeoJSON features with Mercator coordinates.</param>
        /// <param name="tileZ">Tile zoom level.</param>
        /// <param name="tileX">Tile X coordinate.</param>
        /// <param name="tileY">Tile Y coordinate.</param>
        /// <param name="layerName">Name for the output MVT layer (used as source-layer).</param>
        /// <param name="buffer">Buffer size in tile coordinates (default 64 = ~1.5% of 4096).</param>
        public static VectorTileData GenerateTile(List<GeoJsonFeature> features,
            int tileZ, int tileX, int tileY, string layerName, int buffer = 64)
        {
            var tileData = new VectorTileData();
            var layer = new VectorTileLayer
            {
                Name = layerName,
                Extent = DefaultExtent
            };

            int n = 1 << tileZ;
            double tileMinX = (double)tileX / n;
            double tileMinY = (double)tileY / n;
            double tileSize = 1.0 / n;
            double tileMaxX = tileMinX + tileSize;
            double tileMaxY = tileMinY + tileSize;

            // Buffer in Mercator units
            double bufferMerc = tileSize * buffer / DefaultExtent;
            double bMinX = tileMinX - bufferMerc;
            double bMinY = tileMinY - bufferMerc;
            double bMaxX = tileMaxX + bufferMerc;
            double bMaxY = tileMaxY + bufferMerc;

            foreach (var feature in features)
            {
                // Bounding box check
                if (feature.MaxX < bMinX || feature.MinX > bMaxX ||
                    feature.MaxY < bMinY || feature.MinY > bMaxY)
                    continue;

                var mvtFeature = ConvertFeature(feature, layer,
                    tileMinX, tileMinY, tileSize);
                if (mvtFeature != null)
                    layer._features.Add(mvtFeature);
            }

            if (layer.Features.Count > 0)
                tileData._layers.Add(layer);

            return tileData;
        }

        private static VectorTileFeature ConvertFeature(GeoJsonFeature src, VectorTileLayer layer,
            double tileMinX, double tileMinY, double tileSize)
        {
            // Build MVT geometry commands
            var commands = new List<uint>();
            double scale = DefaultExtent / tileSize;

            switch (src.Type)
            {
                case GeometryType.Point:
                    EncodePoints(src.Rings, commands, tileMinX, tileMinY, scale);
                    break;
                case GeometryType.LineString:
                    EncodeLines(src.Rings, commands, tileMinX, tileMinY, scale);
                    break;
                case GeometryType.Polygon:
                    EncodePolygons(src.Rings, commands, tileMinX, tileMinY, scale);
                    break;
            }

            if (commands.Count == 0) return null;

            // Build tags
            var tags = BuildTags(src.Properties, layer);

            return new VectorTileFeature
            {
                Id = src.Id,
                Type = src.Type,
                RawGeometry = commands.ToArray(),
                Tags = tags,
                Layer = layer
            };
        }

        private static void EncodePoints(List<List<double[]>> rings, List<uint> commands,
            double tileMinX, double tileMinY, double scale)
        {
            int cursorX = 0, cursorY = 0;
            int count = 0;
            var moveTos = new List<uint>();

            foreach (var ring in rings)
            {
                foreach (var pt in ring)
                {
                    int tx = (int)Math.Round((pt[0] - tileMinX) * scale);
                    int ty = (int)Math.Round((pt[1] - tileMinY) * scale);
                    int dx = tx - cursorX;
                    int dy = ty - cursorY;
                    moveTos.Add(ZigZagEncode(dx));
                    moveTos.Add(ZigZagEncode(dy));
                    cursorX = tx;
                    cursorY = ty;
                    count++;
                }
            }

            if (count == 0) return;
            // MoveTo command header
            commands.Add((uint)((count << 3) | 1));
            commands.AddRange(moveTos);
        }

        private static void EncodeLines(List<List<double[]>> rings, List<uint> commands,
            double tileMinX, double tileMinY, double scale)
        {
            int cursorX = 0, cursorY = 0;

            foreach (var ring in rings)
            {
                if (ring.Count < 2) continue;

                // MoveTo first point
                int tx = (int)Math.Round((ring[0][0] - tileMinX) * scale);
                int ty = (int)Math.Round((ring[0][1] - tileMinY) * scale);
                commands.Add((uint)((1 << 3) | 1)); // MoveTo count=1
                commands.Add(ZigZagEncode(tx - cursorX));
                commands.Add(ZigZagEncode(ty - cursorY));
                cursorX = tx;
                cursorY = ty;

                // LineTo remaining points
                int lineToCount = ring.Count - 1;
                commands.Add((uint)((lineToCount << 3) | 2)); // LineTo
                for (int i = 1; i < ring.Count; i++)
                {
                    tx = (int)Math.Round((ring[i][0] - tileMinX) * scale);
                    ty = (int)Math.Round((ring[i][1] - tileMinY) * scale);
                    commands.Add(ZigZagEncode(tx - cursorX));
                    commands.Add(ZigZagEncode(ty - cursorY));
                    cursorX = tx;
                    cursorY = ty;
                }
            }
        }

        private static void EncodePolygons(List<List<double[]>> rings, List<uint> commands,
            double tileMinX, double tileMinY, double scale)
        {
            int cursorX = 0, cursorY = 0;

            foreach (var ring in rings)
            {
                if (ring.Count < 4) continue; // Need at least 3 unique points + closing point

                // MoveTo first point
                int tx = (int)Math.Round((ring[0][0] - tileMinX) * scale);
                int ty = (int)Math.Round((ring[0][1] - tileMinY) * scale);
                commands.Add((uint)((1 << 3) | 1)); // MoveTo count=1
                commands.Add(ZigZagEncode(tx - cursorX));
                commands.Add(ZigZagEncode(ty - cursorY));
                cursorX = tx;
                cursorY = ty;

                // LineTo middle points (skip last point which is closing duplicate)
                int lineToCount = ring.Count - 2;
                if (lineToCount > 0)
                {
                    commands.Add((uint)((lineToCount << 3) | 2)); // LineTo
                    for (int i = 1; i <= lineToCount; i++)
                    {
                        tx = (int)Math.Round((ring[i][0] - tileMinX) * scale);
                        ty = (int)Math.Round((ring[i][1] - tileMinY) * scale);
                        commands.Add(ZigZagEncode(tx - cursorX));
                        commands.Add(ZigZagEncode(ty - cursorY));
                        cursorX = tx;
                        cursorY = ty;
                    }
                }

                // ClosePath
                commands.Add((uint)((1 << 3) | 7));
            }
        }

        private static uint ZigZagEncode(int n)
        {
            return (uint)((n << 1) ^ (n >> 31));
        }

        private static uint[] BuildTags(Dictionary<string, object> properties, VectorTileLayer layer)
        {
            if (properties == null || properties.Count == 0)
                return Array.Empty<uint>();

            var tags = new List<uint>();
            foreach (var kvp in properties)
            {
                if (kvp.Value == null) continue;

                int keyIdx = layer._keys.IndexOf(kvp.Key);
                if (keyIdx < 0)
                {
                    keyIdx = layer._keys.Count;
                    layer._keys.Add(kvp.Key);
                }

                var vtValue = CreateVectorTileValue(kvp.Value);
                int valIdx = FindOrAddValue(layer._values, vtValue);

                tags.Add((uint)keyIdx);
                tags.Add((uint)valIdx);
            }
            return tags.ToArray();
        }

        private static VectorTileValue CreateVectorTileValue(object value)
        {
            var vtv = new VectorTileValue();
            switch (value)
            {
                case string s:
                    vtv.StringValue = s;
                    break;
                case long l:
                    vtv.IntValue = l;
                    break;
                case int i:
                    vtv.IntValue = i;
                    break;
                case double d:
                    vtv.DoubleValue = d;
                    break;
                case float f:
                    vtv.FloatValue = f;
                    break;
                case bool b:
                    vtv.BoolValue = b;
                    break;
                default:
                    vtv.StringValue = value.ToString();
                    break;
            }
            return vtv;
        }

        private static int FindOrAddValue(List<VectorTileValue> values, VectorTileValue vtv)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (ValuesEqual(values[i], vtv)) return i;
            }
            values.Add(vtv);
            return values.Count - 1;
        }

        private static bool ValuesEqual(VectorTileValue a, VectorTileValue b)
        {
            if (a.StringValue != null) return a.StringValue == b.StringValue;
            if (a.IntValue.HasValue) return a.IntValue == b.IntValue;
            if (a.DoubleValue.HasValue) return a.DoubleValue == b.DoubleValue;
            if (a.FloatValue.HasValue) return a.FloatValue == b.FloatValue;
            if (a.BoolValue.HasValue) return a.BoolValue == b.BoolValue;
            return false;
        }
    }
}
