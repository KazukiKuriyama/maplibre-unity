// This file is a C# port of mapbox/earcut (https://github.com/mapbox/earcut),
// distributed under the ISC License reproduced below.
//
// ISC License
//
// Copyright (c) 2016, Mapbox
//
// Permission to use, copy, modify, and/or distribute this software for any purpose
// with or without fee is hereby granted, provided that the above copyright notice
// and this permission notice appear in all copies.
//
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
// REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
// INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS
// OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER
// TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF
// THIS SOFTWARE.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.VectorTile
{
    /// <summary>
    /// Polygon triangulation using the earcut algorithm.
    /// C# port of mapbox/earcut (ISC License) -- see header comment for full notice.
    /// Handles complex polygons with holes, robust for real-world geographic data
    /// including complex coastlines and peninsulas.
    /// </summary>
    public static class EarClipTriangulator
    {
        private class Node
        {
            public int I;      // vertex index in the output vertex array
            public float X, Y; // vertex coordinates

            public Node Prev, Next;         // doubly-linked list for polygon ring
            public int ZOrder;              // z-order curve value for spatial hashing
            public Node PrevZ, NextZ;       // z-order linked list
            public bool Steiner;            // bridge point marker

            public Node(int i, float x, float y)
            {
                I = i;
                X = x;
                Y = y;
            }
        }

        /// <summary>
        /// Triangulate a polygon with optional holes.
        /// Exterior ring and holes can be in any winding order (auto-detected).
        /// Returns triangle indices referencing the output vertices list.
        /// </summary>
        public static (List<Vector2> vertices, List<int> indices) Triangulate(
            List<Vector2> exteriorRing,
            List<List<Vector2>> holes = null)
        {
            var outer = RemoveClosingPoint(exteriorRing);
            var data = new List<float>(outer.Count * 2 + (holes != null ? holes.Count * 20 : 0));
            var holeIndices = new List<int>();

            foreach (var v in outer)
            {
                data.Add(v.x);
                data.Add(v.y);
            }

            if (holes != null)
            {
                foreach (var hole in holes)
                {
                    holeIndices.Add(data.Count / 2);
                    var h = RemoveClosingPoint(hole);
                    foreach (var v in h)
                    {
                        data.Add(v.x);
                        data.Add(v.y);
                    }
                }
            }

            var triangleIndices = EarcutCore(data, holeIndices, 2);

            var vertices = new List<Vector2>(data.Count / 2);
            for (int i = 0; i < data.Count; i += 2)
                vertices.Add(new Vector2(data[i], data[i + 1]));

            return (vertices, triangleIndices);
        }

        private static List<int> EarcutCore(List<float> data, List<int> holeIndices, int dim)
        {
            bool hasHoles = holeIndices.Count > 0;
            int outerLen = hasHoles ? holeIndices[0] * dim : data.Count;
            var outerNode = CreateLinkedList(data, 0, outerLen, dim, true);

            var triangles = new List<int>();
            if (outerNode == null || outerNode.Next == outerNode.Prev)
                return triangles;

            if (hasHoles)
                outerNode = EliminateHoles(data, holeIndices, outerNode, dim);

            float minX = 0, minY = 0, maxX = 0, maxY = 0, invSize = 0;
            bool useZOrder = data.Count > 80 * dim;

            if (useZOrder)
            {
                minX = maxX = data[0];
                minY = maxY = data[1];
                for (int i = dim; i < outerLen; i += dim)
                {
                    float x = data[i], y = data[i + 1];
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
                invSize = Math.Max(maxX - minX, maxY - minY);
                invSize = invSize != 0 ? 32767f / invSize : 0;
            }

            EarcutLinked(outerNode, triangles, dim, minX, minY, invSize, 0);

            return triangles;
        }

        /// <summary>
        /// Create a doubly-linked list from polygon coordinates, ensuring consistent winding.
        /// clockwise=true normalizes to CW; clockwise=false normalizes to CCW.
        /// </summary>
        private static Node CreateLinkedList(List<float> data, int start, int end, int dim, bool clockwise)
        {
            Node last = null;

            if (clockwise == (SignedAreaFlat(data, start, end, dim) > 0))
            {
                for (int i = start; i < end; i += dim)
                    last = InsertNode(i / dim, data[i], data[i + 1], last);
            }
            else
            {
                for (int i = end - dim; i >= start; i -= dim)
                    last = InsertNode(i / dim, data[i], data[i + 1], last);
            }

            if (last != null && NodeEquals(last, last.Next))
            {
                RemoveNode(last);
                last = last.Next;
            }

            if (last == null) return null;

            last.Next.Prev = last;
            last.Prev.Next = last;

            return last.Next;
        }

        /// <summary>
        /// Main ear clipping loop with z-ordering optimization and multi-pass fallback.
        /// Pass 0: normal ear clipping with z-order optimization.
        /// Pass 1: filter collinear/duplicate points and retry.
        /// Pass 2: cure local self-intersections and retry.
        /// Pass 3: split polygon at valid diagonal and triangulate halves.
        /// </summary>
        private static void EarcutLinked(Node ear, List<int> triangles, int dim,
            float minX, float minY, float invSize, int pass)
        {
            if (ear == null) return;

            if (pass == 0 && invSize != 0)
                IndexCurve(ear, minX, minY, invSize);

            Node stop = ear;

            while (ear.Prev != ear.Next)
            {
                Node prev = ear.Prev;
                Node next = ear.Next;

                if (invSize != 0 ? IsEarHashed(ear, minX, minY, invSize) : IsEar(ear))
                {
                    triangles.Add(prev.I);
                    triangles.Add(ear.I);
                    triangles.Add(next.I);

                    RemoveNode(ear);

                    ear = next.Next;
                    stop = next.Next;
                    continue;
                }

                ear = next;

                if (ear == stop)
                {
                    if (pass == 0)
                    {
                        EarcutLinked(FilterPoints(ear, null), triangles, dim, minX, minY, invSize, 1);
                    }
                    else if (pass == 1)
                    {
                        ear = CureLocalIntersections(FilterPoints(ear, null), triangles);
                        EarcutLinked(ear, triangles, dim, minX, minY, invSize, 2);
                    }
                    else if (pass == 2)
                    {
                        SplitEarcut(ear, triangles, dim, minX, minY, invSize);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Check if vertex is an ear (triangle contains no other polygon points).
        /// </summary>
        private static bool IsEar(Node ear)
        {
            var a = ear.Prev;
            var b = ear;
            var c = ear.Next;

            if (Area(a, b, c) >= 0) return false; // Reflex or collinear

            var p = ear.Next.Next;
            while (p != ear.Prev)
            {
                if (PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y) &&
                    Area(p.Prev, p, p.Next) >= 0)
                    return false;
                p = p.Next;
            }

            return true;
        }

        /// <summary>
        /// Ear check using z-order spatial index for faster point-in-triangle queries.
        /// </summary>
        private static bool IsEarHashed(Node ear, float minX, float minY, float invSize)
        {
            var a = ear.Prev;
            var b = ear;
            var c = ear.Next;

            if (Area(a, b, c) >= 0) return false;

            float minTX = a.X < b.X ? (a.X < c.X ? a.X : c.X) : (b.X < c.X ? b.X : c.X);
            float minTY = a.Y < b.Y ? (a.Y < c.Y ? a.Y : c.Y) : (b.Y < c.Y ? b.Y : c.Y);
            float maxTX = a.X > b.X ? (a.X > c.X ? a.X : c.X) : (b.X > c.X ? b.X : c.X);
            float maxTY = a.Y > b.Y ? (a.Y > c.Y ? a.Y : c.Y) : (b.Y > c.Y ? b.Y : c.Y);

            int minZ = ComputeZOrder(minTX, minTY, minX, minY, invSize);
            int maxZ = ComputeZOrder(maxTX, maxTY, minX, minY, invSize);

            var p = ear.PrevZ;
            var n = ear.NextZ;

            while (p != null && p.ZOrder >= minZ && n != null && n.ZOrder <= maxZ)
            {
                if (p.I != ear.Prev.I && p.I != ear.Next.I &&
                    PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y) &&
                    Area(p.Prev, p, p.Next) >= 0)
                    return false;
                p = p.PrevZ;

                if (n.I != ear.Prev.I && n.I != ear.Next.I &&
                    PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, n.X, n.Y) &&
                    Area(n.Prev, n, n.Next) >= 0)
                    return false;
                n = n.NextZ;
            }

            while (p != null && p.ZOrder >= minZ)
            {
                if (p.I != ear.Prev.I && p.I != ear.Next.I &&
                    PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y) &&
                    Area(p.Prev, p, p.Next) >= 0)
                    return false;
                p = p.PrevZ;
            }

            while (n != null && n.ZOrder <= maxZ)
            {
                if (n.I != ear.Prev.I && n.I != ear.Next.I &&
                    PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, n.X, n.Y) &&
                    Area(n.Prev, n, n.Next) >= 0)
                    return false;
                n = n.NextZ;
            }

            return true;
        }

        /// <summary>
        /// Fix local self-intersections by outputting triangles at intersection points.
        /// </summary>
        private static Node CureLocalIntersections(Node start, List<int> triangles)
        {
            if (start == null) return null;

            var p = start;
            do
            {
                var a = p.Prev;
                var b = p.Next.Next;

                if (!NodeEquals(a, b) && Intersects(a, p, p.Next, b) &&
                    LocallyInside(a, b) && LocallyInside(b, a))
                {
                    triangles.Add(a.I);
                    triangles.Add(p.I);
                    triangles.Add(b.I);

                    RemoveNode(p);
                    RemoveNode(p.Next);

                    p = start = b;
                }

                p = p.Next;
            } while (p != start);

            return FilterPoints(p, null);
        }

        /// <summary>
        /// Split polygon at a valid diagonal and triangulate each half independently.
        /// Last resort for polygons that can't be triangulated by simple ear clipping.
        /// </summary>
        private static void SplitEarcut(Node start, List<int> triangles, int dim,
            float minX, float minY, float invSize)
        {
            var a = start;
            do
            {
                var b = a.Next.Next;
                while (b != a.Prev)
                {
                    if (a.I != b.I && IsValidDiagonal(a, b))
                    {
                        var c = SplitPolygon(a, b);

                        a = FilterPoints(a, a.Next);
                        c = FilterPoints(c, c.Next);

                        EarcutLinked(a, triangles, dim, minX, minY, invSize, 0);
                        EarcutLinked(c, triangles, dim, minX, minY, invSize, 0);
                        return;
                    }

                    b = b.Next;
                }

                a = a.Next;
            } while (a != start);
        }

        #region Hole Elimination

        /// <summary>
        /// Merge all holes into the outer ring by finding bridge edges.
        /// Holes are sorted by their leftmost X coordinate for optimal bridge finding.
        /// </summary>
        private static Node EliminateHoles(List<float> data, List<int> holeIndices,
            Node outerNode, int dim)
        {
            var queue = new List<Node>();

            for (int i = 0; i < holeIndices.Count; i++)
            {
                int start = holeIndices[i] * dim;
                int end = i < holeIndices.Count - 1 ? holeIndices[i + 1] * dim : data.Count;
                var list = CreateLinkedList(data, start, end, dim, false);
                if (list != null)
                {
                    if (list == list.Next) list.Steiner = true;
                    queue.Add(GetLeftmost(list));
                }
            }

            queue.Sort((a, b) => a.X.CompareTo(b.X));

            foreach (var q in queue)
            {
                outerNode = EliminateHole(q, outerNode);
            }

            return outerNode;
        }

        private static Node EliminateHole(Node hole, Node outerNode)
        {
            var bridge = FindHoleBridge(hole, outerNode);
            if (bridge == null) return outerNode;

            var bridgeReverse = SplitPolygon(bridge, hole);

            FilterPoints(bridgeReverse, bridgeReverse.Next);
            return FilterPoints(bridge, bridge.Next);
        }

        /// <summary>
        /// David Eberly's algorithm for finding a bridge between hole and outer polygon.
        /// Casts a horizontal ray from the hole's leftmost point to find the optimal
        /// bridge vertex on the outer ring.
        /// </summary>
        private static Node FindHoleBridge(Node hole, Node outerNode)
        {
            var p = outerNode;
            float hx = hole.X;
            float hy = hole.Y;
            float qx = float.NegativeInfinity;
            Node m = null;

            // Find the rightmost intersected edge to the left of the hole point
            do
            {
                if (hy <= p.Y && hy >= p.Next.Y && p.Next.Y != p.Y)
                {
                    float x = p.X + (hy - p.Y) * (p.Next.X - p.X) / (p.Next.Y - p.Y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.X < p.Next.X ? p : p.Next;
                        if (x == hx) return m;
                    }
                }

                p = p.Next;
            } while (p != outerNode);

            if (m == null) return null;

            // Look for points inside the triangle of (hole, intersection, m) that
            // are closer to the hole point -- these are better bridge candidates
            var stop = m;
            float mx = m.X;
            float my = m.Y;
            float tanMin = float.MaxValue;

            p = m;
            do
            {
                if (hx >= p.X && p.X >= mx && hx != p.X &&
                    PointInTriangle(
                        hy < my ? hx : qx, hy,
                        mx, my,
                        hy < my ? qx : hx, hy,
                        p.X, p.Y))
                {
                    float tan = Math.Abs(hy - p.Y) / (hx - p.X);

                    if (LocallyInside(p, hole) &&
                        (tan < tanMin ||
                         (tan == tanMin && (p.X > m.X || (p.X == m.X && SectorContainsSector(m, p))))))
                    {
                        m = p;
                        tanMin = tan;
                    }
                }

                p = p.Next;
            } while (p != stop);

            return m;
        }

        private static bool SectorContainsSector(Node m, Node p)
        {
            return Area(m.Prev, m, p.Prev) < 0 && Area(p.Next, m, m.Next) < 0;
        }

        #endregion

        #region Z-Order Indexing

        /// <summary>
        /// Interlink polygon nodes in z-order for fast spatial queries.
        /// </summary>
        private static void IndexCurve(Node start, float minX, float minY, float invSize)
        {
            var p = start;
            do
            {
                if (p.ZOrder == 0)
                    p.ZOrder = ComputeZOrder(p.X, p.Y, minX, minY, invSize);
                p.PrevZ = p.Prev;
                p.NextZ = p.Next;
                p = p.Next;
            } while (p != start);

            p.Prev.NextZ = null;
            p.PrevZ = null;

            SortLinked(p);
        }

        /// <summary>
        /// Simon Tatham's linked list merge sort.
        /// </summary>
        private static Node SortLinked(Node list)
        {
            int inSize = 1;
            int numMerges;

            do
            {
                var p = list;
                list = null;
                Node tail = null;
                numMerges = 0;

                while (p != null)
                {
                    numMerges++;
                    var q = p;
                    int pSize = 0;
                    for (int i = 0; i < inSize; i++)
                    {
                        pSize++;
                        q = q.NextZ;
                        if (q == null) break;
                    }

                    int qSize = inSize;

                    while (pSize > 0 || (qSize > 0 && q != null))
                    {
                        Node e;
                        if (pSize != 0 && (qSize == 0 || q == null || p.ZOrder <= q.ZOrder))
                        {
                            e = p;
                            p = p.NextZ;
                            pSize--;
                        }
                        else
                        {
                            e = q;
                            q = q.NextZ;
                            qSize--;
                        }

                        if (tail != null) tail.NextZ = e;
                        else list = e;

                        e.PrevZ = tail;
                        tail = e;
                    }

                    p = q;
                }

                if (tail != null) tail.NextZ = null;
                inSize *= 2;
            } while (numMerges > 1);

            return list;
        }

        /// <summary>
        /// Compute z-order (Morton code) value for a point, mapping coordinates
        /// to a 32767x32767 integer grid for efficient spatial hashing.
        /// </summary>
        private static int ComputeZOrder(float x, float y, float minX, float minY, float invSize)
        {
            // invSize already includes the 32767 factor (= 32767 / maxDim),
            // so we only multiply by invSize to map to [0, 32767].
            int ix = (int)((x - minX) * invSize);
            int iy = (int)((y - minY) * invSize);

            ix = (ix | (ix << 8)) & 0x00FF00FF;
            ix = (ix | (ix << 4)) & 0x0F0F0F0F;
            ix = (ix | (ix << 2)) & 0x33333333;
            ix = (ix | (ix << 1)) & 0x55555555;

            iy = (iy | (iy << 8)) & 0x00FF00FF;
            iy = (iy | (iy << 4)) & 0x0F0F0F0F;
            iy = (iy | (iy << 2)) & 0x33333333;
            iy = (iy | (iy << 1)) & 0x55555555;

            return ix | (iy << 1);
        }

        #endregion

        #region Geometry Utilities

        /// <summary>
        /// Signed area of triangle (p, q, r).
        /// Negative = CW in Y-down (exterior in MVT), Positive = CCW.
        /// </summary>
        private static float Area(Node p, Node q, Node r)
        {
            return (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);
        }

        private static bool NodeEquals(Node p1, Node p2)
        {
            return p1.X == p2.X && p1.Y == p2.Y;
        }

        private static bool PointInTriangle(float ax, float ay, float bx, float by,
            float cx, float cy, float px, float py)
        {
            return (cx - px) * (ay - py) - (ax - px) * (cy - py) >= 0 &&
                   (ax - px) * (by - py) - (bx - px) * (ay - py) >= 0 &&
                   (bx - px) * (cy - py) - (cx - px) * (by - py) >= 0;
        }

        private static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            int o1 = Sign(Area(p1, q1, p2));
            int o2 = Sign(Area(p1, q1, q2));
            int o3 = Sign(Area(p2, q2, p1));
            int o4 = Sign(Area(p2, q2, q1));

            if (o1 != o2 && o3 != o4) return true;

            if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
            if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
            if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
            if (o4 == 0 && OnSegment(p2, q1, q2)) return true;

            return false;
        }

        private static bool OnSegment(Node p, Node q, Node r)
        {
            return q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) &&
                   q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);
        }

        private static int Sign(float num)
        {
            return num > 0 ? 1 : (num < 0 ? -1 : 0);
        }

        private static bool LocallyInside(Node a, Node b)
        {
            return Area(a.Prev, a, a.Next) < 0
                ? Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0
                : Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;
        }

        private static bool IsValidDiagonal(Node a, Node b)
        {
            return a.Next.I != b.I && a.Prev.I != b.I &&
                   !IntersectsPolygon(a, b) &&
                   ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) &&
                     (Area(a.Prev, a, b) != 0 || Area(a, b, b.Next) != 0)) ||
                    (NodeEquals(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0));
        }

        private static bool IntersectsPolygon(Node a, Node b)
        {
            var p = a;
            do
            {
                if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I &&
                    Intersects(p, p.Next, a, b))
                    return true;
                p = p.Next;
            } while (p != a);

            return false;
        }

        private static bool MiddleInside(Node a, Node b)
        {
            var p = a;
            bool inside = false;
            float px = (a.X + b.X) / 2;
            float py = (a.Y + b.Y) / 2;

            do
            {
                if ((p.Y > py) != (p.Next.Y > py) &&
                    p.Next.Y != p.Y &&
                    px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X)
                    inside = !inside;
                p = p.Next;
            } while (p != a);

            return inside;
        }

        /// <summary>
        /// Get the leftmost node of a polygon ring.
        /// </summary>
        private static Node GetLeftmost(Node start)
        {
            var p = start;
            var leftmost = start;
            do
            {
                if (p.X < leftmost.X || (p.X == leftmost.X && p.Y < leftmost.Y))
                    leftmost = p;
                p = p.Next;
            } while (p != start);

            return leftmost;
        }

        #endregion

        #region Linked List Operations

        private static Node InsertNode(int i, float x, float y, Node last)
        {
            var p = new Node(i, x, y);

            if (last == null)
            {
                p.Prev = p;
                p.Next = p;
            }
            else
            {
                p.Next = last.Next;
                p.Prev = last;
                last.Next.Prev = p;
                last.Next = p;
            }

            return p;
        }

        private static void RemoveNode(Node p)
        {
            p.Next.Prev = p.Prev;
            p.Prev.Next = p.Next;

            if (p.PrevZ != null) p.PrevZ.NextZ = p.NextZ;
            if (p.NextZ != null) p.NextZ.PrevZ = p.PrevZ;
        }

        /// <summary>
        /// Remove duplicate and collinear vertices.
        /// </summary>
        private static Node FilterPoints(Node start, Node end)
        {
            if (start == null) return null;
            if (end == null) end = start;

            var p = start;
            bool again;
            do
            {
                again = false;

                if (!p.Steiner && (NodeEquals(p, p.Next) || Area(p.Prev, p, p.Next) == 0))
                {
                    RemoveNode(p);
                    p = end = p.Prev;
                    if (p == p.Next) break;
                    again = true;
                }
                else
                {
                    p = p.Next;
                }
            } while (again || p != end);

            return end;
        }

        /// <summary>
        /// Split polygon ring at two vertices, creating two separate linked lists.
        /// </summary>
        private static Node SplitPolygon(Node a, Node b)
        {
            var a2 = new Node(a.I, a.X, a.Y);
            var b2 = new Node(b.I, b.X, b.Y);
            var an = a.Next;
            var bp = b.Prev;

            a.Next = b;
            b.Prev = a;

            a2.Next = an;
            an.Prev = a2;

            b2.Next = a2;
            a2.Prev = b2;

            bp.Next = b2;
            b2.Prev = bp;

            return b2;
        }

        #endregion

        /// <summary>
        /// Signed area of a polygon ring from flat coordinate array.
        /// Positive = CW in Y-down coordinates (MVT exterior ring).
        /// </summary>
        private static float SignedAreaFlat(List<float> data, int start, int end, int dim)
        {
            float sum = 0;
            for (int i = start, j = end - dim; i < end; j = i, i += dim)
            {
                sum += (data[j] - data[i]) * (data[i + 1] + data[j + 1]);
            }
            return sum;
        }

        private static List<Vector2> RemoveClosingPoint(List<Vector2> ring)
        {
            if (ring.Count > 1 && ring[0] == ring[ring.Count - 1])
            {
                var result = new List<Vector2>(ring);
                result.RemoveAt(result.Count - 1);
                return result;
            }
            return new List<Vector2>(ring);
        }
    }
}
