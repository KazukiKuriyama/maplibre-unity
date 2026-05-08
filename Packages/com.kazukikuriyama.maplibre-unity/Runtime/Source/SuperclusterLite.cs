using System.Collections.Generic;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Minimal point clusterer modelled after MapLibre's Supercluster.
    /// For each zoom level from <see cref="MaxZoom"/> down to <see cref="MinZoom"/>,
    /// nearby points within <see cref="Radius"/> CSS pixels are merged into a single
    /// cluster. The algorithm is O(N²) per level (no KD-tree), which is adequate for
    /// up to a few thousand points; large datasets should use a server-side cluster.
    /// Coordinates are mercator [0,1].
    ///
    /// Each ClusterPoint carries a stable <see cref="ClusterPoint.ClusterId"/> that callers
    /// can use with <see cref="GetCluster"/>, <see cref="GetClusterChildren"/>,
    /// <see cref="GetClusterLeaves"/> and <see cref="GetClusterExpansionZoom"/>, matching
    /// the MapLibre GL JS GeoJSONSource cluster query API.
    /// </summary>
    public class SuperclusterLite
    {
        public int MinZoom { get; }
        public int MaxZoom { get; }
        public double Radius { get; } // CSS pixels
        public int MinPoints { get; }
        private const int ExtentForPxToMerc = 512;

        /// <summary>
        /// One point or aggregated cluster at a particular zoom level.
        /// </summary>
        public class ClusterPoint
        {
            /// <summary>Mercator X in [0,1].</summary>
            public double X;
            /// <summary>Mercator Y in [0,1].</summary>
            public double Y;
            /// <summary>Original source feature id (passed through for singletons).</summary>
            public ulong Id;
            /// <summary>Number of source points represented (1 = singleton).</summary>
            public int NumPoints;
            /// <summary>Source point properties; null when this is an aggregated cluster.</summary>
            public Dictionary<string, object> Properties;

            /// <summary>
            /// Stable cluster identifier used by the cluster query API. Persists across
            /// zoom levels -- the same singleton has one ClusterId regardless of which
            /// level it appears at.
            /// </summary>
            public long ClusterId;

            /// <summary>
            /// The zoom level at which this cluster was first created. For singletons
            /// this is <see cref="MaxZoom"/> + 1 (they exist at every level).
            /// </summary>
            public int OriginZoom;

            /// <summary>
            /// Direct children (clusters or singletons) at the next-finer zoom level.
            /// Null/empty for singletons.
            /// </summary>
            public List<long> ChildClusterIds;
        }

        // _trees[i] = clusters at zoom (MinZoom + i). _trees[lastIndex] is the unclustered points.
        private List<ClusterPoint>[] _trees;

        // Lookup by ClusterId. Populated for both singletons and aggregated clusters.
        private readonly Dictionary<long, ClusterPoint> _byId = new();

        private long _nextId = 1;

        public SuperclusterLite(int minZoom, int maxZoom, double radius, int minPoints)
        {
            MinZoom = minZoom;
            MaxZoom = maxZoom;
            Radius = radius;
            MinPoints = minPoints;
        }

        /// <summary>
        /// Build the cluster pyramid from the input points. Each input point is assigned
        /// a fresh <see cref="ClusterPoint.ClusterId"/> and registered for lookup.
        /// </summary>
        public void Load(List<ClusterPoint> points)
        {
            _byId.Clear();
            _nextId = 1;

            // Stamp every leaf point with a stable ID and register it. A leaf is shared
            // across all zoom levels (we reuse the same instance), so its ClusterId is
            // identical no matter which level you find it in.
            foreach (var p in points)
            {
                p.ClusterId = _nextId++;
                p.OriginZoom = MaxZoom + 1;
                p.ChildClusterIds = null;
                _byId[p.ClusterId] = p;
            }

            int levels = MaxZoom - MinZoom + 2;
            _trees = new List<ClusterPoint>[levels];
            // Topmost level = original unclustered points (used at zooms > MaxZoom).
            _trees[levels - 1] = points;

            // Build coarser levels by clustering the level above.
            for (int z = MaxZoom; z >= MinZoom; z--)
            {
                int idx = z - MinZoom;
                _trees[idx] = ClusterLevel(_trees[idx + 1], z);
            }
        }

        /// <summary>
        /// Return all clusters/points overlapping the given tile, including a small buffer
        /// around the tile boundary so clusters straddling the edge are visible.
        /// </summary>
        public List<ClusterPoint> GetTile(int tileZ, int tileX, int tileY,
            double bufferFraction = 64.0 / 4096.0)
        {
            if (_trees == null) return new List<ClusterPoint>();

            int zoom = tileZ;
            if (zoom < MinZoom) zoom = MinZoom;
            if (zoom > MaxZoom + 1) zoom = MaxZoom + 1;
            int idx = zoom - MinZoom;
            var pts = _trees[idx];

            double n = 1 << tileZ;
            double minX = tileX / n;
            double maxX = (tileX + 1) / n;
            double minY = tileY / n;
            double maxY = (tileY + 1) / n;
            double buf = bufferFraction / n;

            var result = new List<ClusterPoint>();
            foreach (var p in pts)
            {
                if (p.X >= minX - buf && p.X <= maxX + buf &&
                    p.Y >= minY - buf && p.Y <= maxY + buf)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>Look up any cluster or singleton point by its stable ID.</summary>
        public ClusterPoint GetCluster(long clusterId)
            => _byId.TryGetValue(clusterId, out var cp) ? cp : null;

        /// <summary>
        /// The recommended zoom to fly to in order to break this cluster apart.
        /// Returns -1 when the ID is unknown. Singletons return their OriginZoom.
        /// </summary>
        public int GetClusterExpansionZoom(long clusterId)
        {
            if (!_byId.TryGetValue(clusterId, out var cp)) return -1;
            if (cp.NumPoints <= 1) return cp.OriginZoom;
            // Cluster splits at the next-finer zoom relative to where it was created.
            int next = cp.OriginZoom + 1;
            return next > MaxZoom + 1 ? MaxZoom + 1 : next;
        }

        /// <summary>
        /// Direct children (clusters or singletons) one zoom level finer than the cluster.
        /// Returns an empty list for singletons or unknown IDs.
        /// </summary>
        public List<ClusterPoint> GetClusterChildren(long clusterId)
        {
            var result = new List<ClusterPoint>();
            if (!_byId.TryGetValue(clusterId, out var cp)) return result;
            if (cp.ChildClusterIds == null) return result;
            foreach (var childId in cp.ChildClusterIds)
            {
                if (_byId.TryGetValue(childId, out var child))
                    result.Add(child);
            }
            return result;
        }

        /// <summary>
        /// All leaf (singleton) points contained by the cluster, walked recursively.
        /// Honours offset/limit pagination identical to MapLibre GL JS' getClusterLeaves.
        /// </summary>
        public List<ClusterPoint> GetClusterLeaves(long clusterId,
            int limit = 10, int offset = 0)
        {
            var leaves = new List<ClusterPoint>();
            if (!_byId.TryGetValue(clusterId, out var cp)) return leaves;

            // DFS -- leftmost (insertion order) first for deterministic results.
            var stack = new Stack<ClusterPoint>();
            stack.Push(cp);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.NumPoints == 1)
                {
                    leaves.Add(node);
                    continue;
                }
                if (node.ChildClusterIds != null)
                {
                    for (int i = node.ChildClusterIds.Count - 1; i >= 0; i--)
                    {
                        if (_byId.TryGetValue(node.ChildClusterIds[i], out var child))
                            stack.Push(child);
                    }
                }
            }

            if (offset >= leaves.Count) return new List<ClusterPoint>();
            int count = System.Math.Min(limit, leaves.Count - offset);
            return leaves.GetRange(offset, count);
        }

        private List<ClusterPoint> ClusterLevel(List<ClusterPoint> input, int zoom)
        {
            var result = new List<ClusterPoint>(input.Count);
            var consumed = new bool[input.Count];

            // Cluster radius in mercator units at this zoom.
            double r = Radius / (ExtentForPxToMerc * (double)(1 << zoom));
            double r2 = r * r;

            for (int i = 0; i < input.Count; i++)
            {
                if (consumed[i]) continue;
                var p = input[i];

                double sumX = p.X * p.NumPoints;
                double sumY = p.Y * p.NumPoints;
                int totalCount = p.NumPoints;
                var children = new List<long> { p.ClusterId };

                for (int j = i + 1; j < input.Count; j++)
                {
                    if (consumed[j]) continue;
                    var q = input[j];
                    double dx = q.X - p.X;
                    double dy = q.Y - p.Y;
                    if (dx * dx + dy * dy < r2)
                    {
                        consumed[j] = true;
                        sumX += q.X * q.NumPoints;
                        sumY += q.Y * q.NumPoints;
                        totalCount += q.NumPoints;
                        children.Add(q.ClusterId);
                    }
                }

                if (children.Count <= 1 || totalCount < MinPoints)
                {
                    // Pass through unchanged -- same instance, same ClusterId. The point
                    // therefore appears in multiple levels with one shared identity.
                    result.Add(p);
                }
                else
                {
                    consumed[i] = true;
                    var cluster = new ClusterPoint
                    {
                        X = sumX / totalCount,
                        Y = sumY / totalCount,
                        Id = p.Id,
                        NumPoints = totalCount,
                        Properties = null,
                        ClusterId = _nextId++,
                        OriginZoom = zoom,
                        ChildClusterIds = children,
                    };
                    _byId[cluster.ClusterId] = cluster;
                    result.Add(cluster);
                }
            }
            return result;
        }
    }
}
