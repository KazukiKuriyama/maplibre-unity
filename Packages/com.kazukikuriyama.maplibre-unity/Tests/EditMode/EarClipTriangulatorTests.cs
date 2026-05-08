using System;
using System.Collections.Generic;
using MapLibre.Unity.VectorTile;
using NUnit.Framework;
using UnityEngine;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Regression tests for the earcut polygon triangulator. The algorithm
    /// itself is non-trivial (multi-pass with z-order indexing), so the
    /// strategy is invariant-based:
    ///
    ///   * triangle count is divisible by 3,
    ///   * every index is in range,
    ///   * sum of triangle areas equals the polygon's signed area
    ///     (outer ring minus holes), within float epsilon.
    ///
    /// Asserting exact triangulations would couple the tests to ear-pick order,
    /// which is an implementation detail and changes between earcut releases.
    /// </summary>
    public class EarClipTriangulatorTests
    {
        // === Empty / degenerate input ===

        [Test]
        public void Triangulate_EmptyRing_ReturnsNoTriangles()
        {
            var (verts, idx) = EarClipTriangulator.Triangulate(new List<Vector2>());
            Assert.AreEqual(0, idx.Count);
            Assert.AreEqual(0, verts.Count);
        }

        [Test]
        public void Triangulate_TwoVertexRing_ReturnsNoTriangles()
        {
            var (_, idx) = EarClipTriangulator.Triangulate(new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(1, 1)
            });
            Assert.AreEqual(0, idx.Count);
        }

        [Test]
        public void Triangulate_CollinearPoints_ProducesNoTriangles()
        {
            // Three collinear points have zero area -- the FilterPoints pass
            // collapses them and the output should be empty.
            var (_, idx) = EarClipTriangulator.Triangulate(new List<Vector2>
            {
                new Vector2(0, 0),
                new Vector2(1, 1),
                new Vector2(2, 2),
            });
            Assert.AreEqual(0, idx.Count);
        }

        // === Trivial polygons ===

        [Test]
        public void Triangulate_Triangle_ProducesOneTriangle()
        {
            var ring = new List<Vector2>
            {
                new Vector2(0, 0),
                new Vector2(2, 0),
                new Vector2(1, 2),
            };
            var (verts, idx) = EarClipTriangulator.Triangulate(ring);
            AssertWellFormedTriangulation(verts, idx);
            Assert.AreEqual(3, idx.Count);
            Assert.AreEqual(PolygonAbsArea(ring), SumTriangleArea(verts, idx), 1e-4f);
        }

        [Test]
        public void Triangulate_AcceptsClosedRing_SameAsOpen()
        {
            // Many GIS sources emit a closing point (last == first). The
            // triangulator must yield the same total area either way.
            var open = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(4, 0),
                new Vector2(4, 3), new Vector2(0, 3),
            };
            var closed = new List<Vector2>(open) { new Vector2(0, 0) };

            var (vO, iO) = EarClipTriangulator.Triangulate(open);
            var (vC, iC) = EarClipTriangulator.Triangulate(closed);

            AssertWellFormedTriangulation(vO, iO);
            AssertWellFormedTriangulation(vC, iC);
            Assert.AreEqual(SumTriangleArea(vO, iO), SumTriangleArea(vC, iC), 1e-4f);
        }

        [Test]
        public void Triangulate_Square_ProducesTwoTriangles()
        {
            var ring = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(4, 0),
                new Vector2(4, 4), new Vector2(0, 4),
            };
            var (verts, idx) = EarClipTriangulator.Triangulate(ring);
            AssertWellFormedTriangulation(verts, idx);
            Assert.AreEqual(6, idx.Count); // 2 triangles
            Assert.AreEqual(16f, SumTriangleArea(verts, idx), 1e-4f);
        }

        [Test]
        public void Triangulate_Square_WindingAgnostic()
        {
            // CW and CCW exterior rings must triangulate to the same area --
            // the triangulator auto-detects winding via signed-area check.
            var cw = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(0, 4),
                new Vector2(4, 4), new Vector2(4, 0),
            };
            var ccw = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(4, 0),
                new Vector2(4, 4), new Vector2(0, 4),
            };
            var (vCw, iCw) = EarClipTriangulator.Triangulate(cw);
            var (vCcw, iCcw) = EarClipTriangulator.Triangulate(ccw);
            AssertWellFormedTriangulation(vCw, iCw);
            AssertWellFormedTriangulation(vCcw, iCcw);
            Assert.AreEqual(SumTriangleArea(vCw, iCw),
                            SumTriangleArea(vCcw, iCcw), 1e-4f);
        }

        // === Concave polygons (hits the ear-clipping core) ===

        [Test]
        public void Triangulate_LShape_TilesPolygonExactly()
        {
            // L-shape: 6 vertices, area = 12 (4x4 outer minus 2x2 cut).
            var ring = new List<Vector2>
            {
                new Vector2(0, 0),
                new Vector2(4, 0),
                new Vector2(4, 2),
                new Vector2(2, 2),
                new Vector2(2, 4),
                new Vector2(0, 4),
            };
            var (verts, idx) = EarClipTriangulator.Triangulate(ring);
            AssertWellFormedTriangulation(verts, idx);
            // n-gon → (n - 2) triangles for a simple polygon
            Assert.AreEqual((6 - 2) * 3, idx.Count);
            Assert.AreEqual(12f, SumTriangleArea(verts, idx), 1e-4f);
        }

        [Test]
        public void Triangulate_PentagonIsSimplePolygonCount()
        {
            // Regular-ish pentagon -- vertex count 5 → 3 triangles.
            var ring = new List<Vector2>
            {
                new Vector2(0, 0),
                new Vector2(2, 0),
                new Vector2(3, 1.5f),
                new Vector2(1, 2.5f),
                new Vector2(-1, 1.5f),
            };
            var (verts, idx) = EarClipTriangulator.Triangulate(ring);
            AssertWellFormedTriangulation(verts, idx);
            Assert.AreEqual((5 - 2) * 3, idx.Count);
            Assert.AreEqual(PolygonAbsArea(ring), SumTriangleArea(verts, idx), 1e-4f);
        }

        // === Polygons with holes ===

        [Test]
        public void Triangulate_SquareWithSquareHole_AreaMatchesOuterMinusHole()
        {
            var outer = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(10, 0),
                new Vector2(10, 10), new Vector2(0, 10),
            };
            var hole = new List<Vector2>
            {
                // Hole winding opposite to outer -- earcut auto-detects.
                new Vector2(3, 3), new Vector2(3, 7),
                new Vector2(7, 7), new Vector2(7, 3),
            };
            var (verts, idx) = EarClipTriangulator.Triangulate(
                outer, new List<List<Vector2>> { hole });

            AssertWellFormedTriangulation(verts, idx);

            float expected = PolygonAbsArea(outer) - PolygonAbsArea(hole);
            Assert.AreEqual(expected, SumTriangleArea(verts, idx), 1e-3f);
        }

        [Test]
        public void Triangulate_SquareWithTwoHoles_AreaMatchesOuterMinusHoles()
        {
            var outer = new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(10, 0),
                new Vector2(10, 10), new Vector2(0, 10),
            };
            var holeA = new List<Vector2>
            {
                new Vector2(1, 1), new Vector2(1, 3),
                new Vector2(3, 3), new Vector2(3, 1),
            };
            var holeB = new List<Vector2>
            {
                new Vector2(5, 5), new Vector2(5, 8),
                new Vector2(8, 8), new Vector2(8, 5),
            };
            var (verts, idx) = EarClipTriangulator.Triangulate(
                outer, new List<List<Vector2>> { holeA, holeB });

            AssertWellFormedTriangulation(verts, idx);
            float expected = PolygonAbsArea(outer) - PolygonAbsArea(holeA) - PolygonAbsArea(holeB);
            Assert.AreEqual(expected, SumTriangleArea(verts, idx), 1e-3f);
        }

        // === Stress: large convex polygon to hit the z-order code path ===

        [Test]
        public void Triangulate_LargeCircleApproximation_AreaMatchesAnalytic()
        {
            // 200 verts forces the z-order indexing branch (threshold = 80*dim).
            const int n = 200;
            const float radius = 10f;
            var ring = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float a = (float)(i * 2.0 * Math.PI / n);
                ring.Add(new Vector2((float)Math.Cos(a) * radius,
                                     (float)Math.Sin(a) * radius));
            }

            var (verts, idx) = EarClipTriangulator.Triangulate(ring);
            AssertWellFormedTriangulation(verts, idx);
            // Expected area = n-gon inscribed in circle: 0.5 * n * r^2 * sin(2π/n)
            float expected = 0.5f * n * radius * radius
                             * (float)Math.Sin(2.0 * Math.PI / n);
            Assert.AreEqual(expected, SumTriangleArea(verts, idx), 1e-2f);
        }

        // === Helpers ===

        private static void AssertWellFormedTriangulation(List<Vector2> verts, List<int> indices)
        {
            Assert.AreEqual(0, indices.Count % 3,
                "Index count must be divisible by 3");
            for (int i = 0; i < indices.Count; i++)
            {
                int v = indices[i];
                Assert.GreaterOrEqual(v, 0, $"Index[{i}] is negative");
                Assert.Less(v, verts.Count,
                    $"Index[{i}]={v} is out of range for {verts.Count} verts");
            }
        }

        private static float SumTriangleArea(List<Vector2> verts, List<int> indices)
        {
            float total = 0f;
            for (int i = 0; i < indices.Count; i += 3)
            {
                Vector2 a = verts[indices[i]];
                Vector2 b = verts[indices[i + 1]];
                Vector2 c = verts[indices[i + 2]];
                total += Math.Abs((b.x - a.x) * (c.y - a.y)
                                  - (c.x - a.x) * (b.y - a.y)) * 0.5f;
            }
            return total;
        }

        private static float PolygonAbsArea(List<Vector2> ring)
        {
            float a = 0f;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                a += (ring[j].x + ring[i].x) * (ring[j].y - ring[i].y);
            return Math.Abs(a) * 0.5f;
        }
    }
}
