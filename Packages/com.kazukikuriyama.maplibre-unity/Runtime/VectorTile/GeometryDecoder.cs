using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.VectorTile
{
    /// <summary>
    /// Decodes MVT geometry commands into point, line, and polygon coordinates.
    /// MVT geometry encoding uses a cursor-based system with MoveTo, LineTo, ClosePath commands.
    /// Coordinates are in tile-local space [0, extent].
    /// </summary>
    public static class GeometryDecoder
    {
        // MVT command IDs
        private const int CommandMoveTo = 1;
        private const int CommandLineTo = 2;
        private const int CommandClosePath = 7;

        /// <summary>
        /// Decode geometry commands into a list of coordinate rings.
        /// Each ring is a list of Vector2 points in tile-local coordinates [0, extent].
        /// For polygons: first ring is exterior, subsequent rings are holes (if winding differs).
        /// For lines: each ring is a separate line string.
        /// For points: each ring contains a single point.
        /// </summary>
        public static List<List<Vector2>> Decode(uint[] geometry, GeometryType type)
        {
            if (geometry == null || geometry.Length == 0)
                return new List<List<Vector2>>();

            var rings = new List<List<Vector2>>();
            var currentRing = new List<Vector2>();

            int cursorX = 0;
            int cursorY = 0;
            int i = 0;

            while (i < geometry.Length)
            {
                uint cmdInteger = geometry[i++];
                int commandId = (int)(cmdInteger & 0x07);
                int commandCount = (int)(cmdInteger >> 3);

                switch (commandId)
                {
                    case CommandMoveTo:
                        for (int j = 0; j < commandCount; j++)
                        {
                            // Start a new ring on each MoveTo
                            if (currentRing.Count > 0)
                            {
                                rings.Add(currentRing);
                                currentRing = new List<Vector2>();
                            }

                            int dx = ZigZagDecode(geometry[i++]);
                            int dy = ZigZagDecode(geometry[i++]);
                            cursorX += dx;
                            cursorY += dy;
                            currentRing.Add(new Vector2(cursorX, cursorY));
                        }
                        break;

                    case CommandLineTo:
                        for (int j = 0; j < commandCount; j++)
                        {
                            int dx = ZigZagDecode(geometry[i++]);
                            int dy = ZigZagDecode(geometry[i++]);
                            cursorX += dx;
                            cursorY += dy;
                            currentRing.Add(new Vector2(cursorX, cursorY));
                        }
                        break;

                    case CommandClosePath:
                        // Close the polygon ring by adding the first point
                        if (currentRing.Count > 0)
                        {
                            currentRing.Add(currentRing[0]);
                            rings.Add(currentRing);
                            currentRing = new List<Vector2>();
                        }
                        break;
                }
            }

            // Add any remaining open ring (for lines/points)
            if (currentRing.Count > 0)
            {
                rings.Add(currentRing);
            }

            return rings;
        }

        /// <summary>
        /// Decode a zigzag-encoded integer parameter.
        /// </summary>
        private static int ZigZagDecode(uint n)
        {
            return (int)(n >> 1) ^ -(int)(n & 1);
        }

        /// <summary>
        /// Calculate the signed area of a ring (for determining winding order).
        /// Positive = clockwise (exterior ring in MVT), Negative = counter-clockwise (hole).
        /// </summary>
        public static float SignedArea(List<Vector2> ring)
        {
            // Accumulate in double, not float. A GeoJSON source polygon larger than one
            // tile is currently encoded whole into every tile whose bounding box it
            // overlaps (GeoJsonToVectorTile.GenerateTile does a bbox test but no
            // clipping), so ring coordinates can reach tens of thousands of tile-local
            // units instead of staying inside [0, extent]. Summing this shoelace series
            // in single precision over many closely-spaced points at that magnitude
            // loses catastrophically and can FLIP THE SIGN — after which
            // ClassifyPolygonRings treats a valid exterior ring as an orphan hole and
            // silently discards it, so the polygon never renders and nothing is logged.
            // Observed with 4 ambulance-isochrone rings of 101-1295 points spanning
            // kilometres: every ring dropped, polygonsCount=0.
            double area = 0;
            int count = ring.Count;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                area += ((double)ring[j].x - ring[i].x) * ((double)ring[i].y + ring[j].y);
            }
            return (float)(area * 0.5);
        }

        /// <summary>
        /// Classify polygon rings into exterior rings and their holes.
        /// Returns a list of polygons, each containing an exterior ring followed by zero or more holes.
        /// In MVT, exterior rings are CW (positive area), holes are CCW (negative area).
        /// </summary>
        public static List<List<List<Vector2>>> ClassifyPolygonRings(List<List<Vector2>> rings)
        {
            var polygons = new List<List<List<Vector2>>>();
            List<List<Vector2>> currentPolygon = null;

            for (int i = 0; i < rings.Count; i++)
            {
                float area = SignedArea(rings[i]);
                if (area == 0) continue; // Degenerate ring

                if (area > 0) // Exterior ring (CW in MVT coordinate system)
                {
                    currentPolygon = new List<List<Vector2>> { rings[i] };
                    polygons.Add(currentPolygon);
                }
                else // Negative area
                {
                    if (currentPolygon != null)
                    {
                        currentPolygon.Add(rings[i]); // Hole (CCW), as intended
                    }
                    else
                    {
                        // A negative ring with NO preceding exterior ring is not an
                        // orphan hole -- a lone ring cannot be a hole. It is an
                        // exterior ring wound the opposite way, which is common:
                        // GeoJSON RFC 7946 asks for counter-clockwise exteriors while
                        // MVT expects clockwise, and GeoJsonToVectorTile does not
                        // normalise winding when it converts. Silently discarding it
                        // loses real geometry with no warning.
                        //
                        // Measured on one real dataset: 4 of 4 ambulance-isochrone
                        // rings dropped (consistently wound, so 100%), and 3,210 of
                        // 6,475 OSM building footprints dropped (inconsistently wound,
                        // so ~50%) -- none of it visible as an error anywhere.
                        var exterior = new List<Vector2>(rings[i]);
                        exterior.Reverse();
                        currentPolygon = new List<List<Vector2>> { exterior };
                        polygons.Add(currentPolygon);
                    }
                }
            }

            return polygons;
        }
    }
}
