using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity
{
    /// <summary>
    /// Geometry hit-testing utilities for queryRenderedFeatures.
    /// All operations work in tile-local coordinate space [0, extent].
    /// </summary>
    internal static class GeometryHitTest
    {
        /// <summary>
        /// Test if a point is inside a polygon (exterior ring minus holes).
        /// Uses the ray-casting algorithm.
        /// </summary>
        public static bool PointInPolygon(Vector2 point, List<List<Vector2>> polygonRings)
        {
            if (polygonRings == null || polygonRings.Count == 0) return false;

            // First ring must be exterior and contain the point
            if (!PointInRing(point, polygonRings[0])) return false;

            // Subsequent rings are holes -- point must NOT be inside any hole
            for (int i = 1; i < polygonRings.Count; i++)
            {
                if (PointInRing(point, polygonRings[i])) return false;
            }

            return true;
        }

        /// <summary>
        /// Ray-casting point-in-ring test.
        /// </summary>
        private static bool PointInRing(Vector2 point, List<Vector2> ring)
        {
            if (ring == null || ring.Count < 3) return false;

            bool inside = false;
            int count = ring.Count;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                float yi = ring[i].y, yj = ring[j].y;
                float xi = ring[i].x, xj = ring[j].x;

                if ((yi > point.y) != (yj > point.y) &&
                    point.x < (xj - xi) * (point.y - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// Test if a point is within tolerance of any segment in a polyline.
        /// </summary>
        public static bool PointNearLine(Vector2 point, List<Vector2> line, float tolerance)
        {
            if (line == null || line.Count < 2) return false;

            float toleranceSq = tolerance * tolerance;
            for (int i = 0; i < line.Count - 1; i++)
            {
                if (PointToSegmentDistanceSq(point, line[i], line[i + 1]) <= toleranceSq)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Test if a point is within tolerance of a target point.
        /// </summary>
        public static bool PointNearPoint(Vector2 point, Vector2 target, float tolerance)
        {
            float dx = point.x - target.x;
            float dy = point.y - target.y;
            return dx * dx + dy * dy <= tolerance * tolerance;
        }

        /// <summary>
        /// Squared distance from a point to a line segment.
        /// </summary>
        private static float PointToSegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float lenSq = dx * dx + dy * dy;

            if (lenSq == 0f) // Degenerate segment
            {
                dx = p.x - a.x;
                dy = p.y - a.y;
                return dx * dx + dy * dy;
            }

            float t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / lenSq;
            t = Mathf.Clamp01(t);

            float projX = a.x + t * dx;
            float projY = a.y + t * dy;
            float ex = p.x - projX;
            float ey = p.y - projY;
            return ex * ex + ey * ey;
        }
    }
}
