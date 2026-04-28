using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.VectorTile
{
    /// <summary>
    /// Pre-computed mesh data that can be built on a background thread.
    /// Only Unity Mesh creation must happen on the main thread.
    /// </summary>
    public class MeshData
    {
        public Vector3[] Vertices;
        public int[] Indices;
        public Vector2[] UVs;

        /// <summary>
        /// Secondary UV channel. For line meshes, stores cumulative distance
        /// along the line in tile-local units (x component).
        /// </summary>
        public Vector2[] UV2s;

        /// <summary>
        /// Tertiary UV channel. For line meshes, stores per-feature
        /// line-width (x, in CSS pixels) and line-offset (y, in CSS pixels).
        /// </summary>
        public Vector2[] UV3s;

        /// <summary>
        /// Quaternary UV channel (TEXCOORD3). For line meshes built with
        /// <c>line-progress</c> enabled, stores normalised distance along the
        /// feature in <c>x</c> (0..1). Used by the line-gradient shader to
        /// sample its 1D ramp texture per vertex.
        /// </summary>
        public Vector2[] UV4s;

        /// <summary>
        /// Per-vertex normals. Used by fill-extrusion for lighting.
        /// When set, ToMesh() skips RecalculateNormals().
        /// </summary>
        public Vector3[] Normals;

        /// <summary>
        /// Per-vertex color. Used by circle, fill and line layers for per-feature fill color.
        /// </summary>
        public Color32[] Colors;

        public Mesh ToMesh()
        {
            if (Vertices == null || Vertices.Length == 0) return null;

            var mesh = new Mesh();
            if (Vertices.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = Vertices;
            mesh.triangles = Indices;
            if (UVs != null && UVs.Length > 0)
            {
                mesh.uv = UVs;
                // For line meshes, vertices are at center positions and the shader expands them.
                // Use a generous bounding box to prevent culling.
                mesh.bounds = new Bounds(Vector3.zero, new Vector3(1.5f, 0.1f, 1.5f));
            }
            else
            {
                mesh.RecalculateBounds();
            }
            if (UV2s != null && UV2s.Length > 0)
                mesh.uv2 = UV2s;
            if (UV3s != null && UV3s.Length > 0)
                mesh.uv3 = UV3s;
            if (UV4s != null && UV4s.Length > 0)
                mesh.uv4 = UV4s;
            if (Colors != null && Colors.Length > 0)
                mesh.colors32 = Colors;
            if (Normals != null && Normals.Length > 0)
                mesh.normals = Normals;
            else
                mesh.RecalculateNormals();
            return mesh;
        }
    }

    /// <summary>
    /// Builds mesh data from decoded vector tile geometry.
    /// All methods are thread-safe (no Unity API calls) and return MeshData.
    /// </summary>
    public static class VectorTileMeshBuilder
    {
        /// <summary>
        /// Build mesh data for fill (polygon) features on any thread.
        /// Per-feature fill color (with opacity) is baked into vertex colors so that
        /// data-driven styling (e.g. fill-color via ["get", ...]) works.
        /// </summary>
        public static MeshData BuildFillMeshData(List<VectorTileFeature> features, int extent,
            Expressions.Expression colorExpr, Expressions.Expression opacityExpr, float zoom,
            string sourceId = null, string sourceLayer = null,
            Expressions.IFeatureStateStore featureStateStore = null)
        {
            var allVertices = new List<Vector3>(256);
            var allIndices = new List<int>(512);
            var allColors = new List<Color32>(256);

            float invExtent = 1f / extent;

            var defaultColor = new Color(0f, 0f, 0f, 1f);
            const float defaultOpacity = 1f;

            for (int fi = 0; fi < features.Count; fi++)
            {
                var feature = features[fi];
                if (feature.Type != GeometryType.Polygon) continue;

                var ctx = new Expressions.EvaluationContext(zoom, feature)
                {
                    SourceId = sourceId,
                    SourceLayer = sourceLayer,
                    FeatureStateStore = featureStateStore,
                };
                Color color = colorExpr != null
                    ? colorExpr.EvaluateColor(ctx, defaultColor) : defaultColor;
                float opacity = opacityExpr != null
                    ? opacityExpr.EvaluateFloat(ctx, defaultOpacity) : defaultOpacity;
                color.a *= opacity;
                var color32 = (Color32)color;

                var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);
                var polygons = GeometryDecoder.ClassifyPolygonRings(rings);

                for (int pi = 0; pi < polygons.Count; pi++)
                {
                    var polygon = polygons[pi];
                    if (polygon.Count == 0) continue;

                    var exterior = polygon[0];
                    var holes = polygon.Count > 1 ? polygon.GetRange(1, polygon.Count - 1) : null;

                    var (vertices2D, indices) = EarClipTriangulator.Triangulate(exterior, holes);

                    int baseIndex = allVertices.Count;

                    for (int vi = 0; vi < vertices2D.Count; vi++)
                    {
                        var v = vertices2D[vi];
                        float x = v.x * invExtent - 0.5f;
                        float z = -(v.y * invExtent - 0.5f);
                        allVertices.Add(new Vector3(x, 0f, z));
                        allColors.Add(color32);
                    }

                    for (int ii = 0; ii < indices.Count; ii++)
                    {
                        allIndices.Add(baseIndex + indices[ii]);
                    }
                }
            }

            if (allVertices.Count == 0) return null;

            return new MeshData
            {
                Vertices = allVertices.ToArray(),
                Indices = allIndices.ToArray(),
                Colors = allColors.ToArray()
            };
        }

        /// <summary>
        /// Build mesh data for circle (point) features on any thread.
        /// Each point becomes a quad with UVs in [-1, 1] for SDF circle rendering.
        /// Per-feature radius (CSS pixels) is stored in UV2.x.
        /// Per-feature fill color (with opacity) is stored in vertex colors.
        /// The shader converts CSS pixels to tile-local units via a _CSSToLocal uniform.
        /// </summary>
        public static MeshData BuildCircleMeshData(List<VectorTileFeature> features, int extent,
            Expressions.Expression radiusExpr, Expressions.Expression colorExpr,
            Expressions.Expression opacityExpr, float zoom,
            string sourceId = null, string sourceLayer = null,
            Expressions.IFeatureStateStore featureStateStore = null)
        {
            var allVertices = new List<Vector3>(features.Count * 4);
            var allIndices = new List<int>(features.Count * 6);
            var allUVs = new List<Vector2>(features.Count * 4);
            var allUV2s = new List<Vector2>(features.Count * 4);
            var allColors = new List<Color32>(features.Count * 4);

            float invExtent = 1f / extent;

            // Default values matching MapLibre spec
            const float defaultRadius = 5f;
            var defaultColor = new Color(0f, 0f, 0f, 1f);
            const float defaultOpacity = 1f;

            for (int fi = 0; fi < features.Count; fi++)
            {
                var feature = features[fi];
                if (feature.Type != GeometryType.Point) continue;

                // Evaluate per-feature expressions
                var ctx = new Expressions.EvaluationContext(zoom, feature)
                {
                    SourceId = sourceId,
                    SourceLayer = sourceLayer,
                    FeatureStateStore = featureStateStore,
                };
                float radius = radiusExpr != null
                    ? radiusExpr.EvaluateFloat(ctx, defaultRadius) : defaultRadius;
                Color color = colorExpr != null
                    ? colorExpr.EvaluateColor(ctx, defaultColor) : defaultColor;
                float opacity = opacityExpr != null
                    ? opacityExpr.EvaluateFloat(ctx, defaultOpacity) : defaultOpacity;

                color.a *= opacity;
                var color32 = (Color32)color;

                var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);

                for (int ri = 0; ri < rings.Count; ri++)
                {
                    var ring = rings[ri];
                    for (int pi = 0; pi < ring.Count; pi++)
                    {
                        var pt = ring[pi];
                        float x = pt.x * invExtent - 0.5f;
                        float z = -(pt.y * invExtent - 0.5f);

                        int baseIdx = allVertices.Count;

                        // Quad vertices at the point position (shader will expand by radius)
                        allVertices.Add(new Vector3(x, 0f, z));
                        allVertices.Add(new Vector3(x, 0f, z));
                        allVertices.Add(new Vector3(x, 0f, z));
                        allVertices.Add(new Vector3(x, 0f, z));

                        // UVs encode the quad corner direction for shader expansion
                        allUVs.Add(new Vector2(-1f, -1f));
                        allUVs.Add(new Vector2(1f, -1f));
                        allUVs.Add(new Vector2(1f, 1f));
                        allUVs.Add(new Vector2(-1f, 1f));

                        // UV2.x = CSS pixel radius per feature
                        var radiusUV = new Vector2(radius, 0f);
                        allUV2s.Add(radiusUV);
                        allUV2s.Add(radiusUV);
                        allUV2s.Add(radiusUV);
                        allUV2s.Add(radiusUV);

                        // Vertex color = fill color with opacity
                        allColors.Add(color32);
                        allColors.Add(color32);
                        allColors.Add(color32);
                        allColors.Add(color32);

                        allIndices.Add(baseIdx);
                        allIndices.Add(baseIdx + 2);
                        allIndices.Add(baseIdx + 1);
                        allIndices.Add(baseIdx);
                        allIndices.Add(baseIdx + 3);
                        allIndices.Add(baseIdx + 2);
                    }
                }
            }

            if (allVertices.Count == 0) return null;

            return new MeshData
            {
                Vertices = allVertices.ToArray(),
                Indices = allIndices.ToArray(),
                UVs = allUVs.ToArray(),
                UV2s = allUV2s.ToArray(),
                Colors = allColors.ToArray()
            };
        }

        /// <summary>
        /// Build mesh data for heatmap point features on any thread.
        /// Each point becomes a quad. UV2.x stores per-feature weight (0..1+).
        /// The shader uses Gaussian falloff and samples a color ramp texture.
        /// </summary>
        public static MeshData BuildHeatmapMeshData(List<VectorTileFeature> features, int extent,
            Expressions.Expression weightExpr, float zoom,
            string sourceId = null, string sourceLayer = null,
            Expressions.IFeatureStateStore featureStateStore = null)
        {
            var allVertices = new List<Vector3>(features.Count * 4);
            var allIndices = new List<int>(features.Count * 6);
            var allUVs = new List<Vector2>(features.Count * 4);
            var allUV2s = new List<Vector2>(features.Count * 4);

            float invExtent = 1f / extent;
            const float defaultWeight = 1f;

            for (int fi = 0; fi < features.Count; fi++)
            {
                var feature = features[fi];
                if (feature.Type != GeometryType.Point) continue;

                var ctx = new Expressions.EvaluationContext(zoom, feature)
                {
                    SourceId = sourceId,
                    SourceLayer = sourceLayer,
                    FeatureStateStore = featureStateStore,
                };
                float weight = weightExpr != null
                    ? weightExpr.EvaluateFloat(ctx, defaultWeight) : defaultWeight;

                var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);

                for (int ri = 0; ri < rings.Count; ri++)
                {
                    var ring = rings[ri];
                    for (int pi = 0; pi < ring.Count; pi++)
                    {
                        var pt = ring[pi];
                        float x = pt.x * invExtent - 0.5f;
                        float z = -(pt.y * invExtent - 0.5f);

                        int baseIdx = allVertices.Count;

                        allVertices.Add(new Vector3(x, 0f, z));
                        allVertices.Add(new Vector3(x, 0f, z));
                        allVertices.Add(new Vector3(x, 0f, z));
                        allVertices.Add(new Vector3(x, 0f, z));

                        allUVs.Add(new Vector2(-1f, -1f));
                        allUVs.Add(new Vector2(1f, -1f));
                        allUVs.Add(new Vector2(1f, 1f));
                        allUVs.Add(new Vector2(-1f, 1f));

                        // UV2.x = weight per feature
                        var weightUV = new Vector2(weight, 0f);
                        allUV2s.Add(weightUV);
                        allUV2s.Add(weightUV);
                        allUV2s.Add(weightUV);
                        allUV2s.Add(weightUV);

                        allIndices.Add(baseIdx);
                        allIndices.Add(baseIdx + 2);
                        allIndices.Add(baseIdx + 1);
                        allIndices.Add(baseIdx);
                        allIndices.Add(baseIdx + 3);
                        allIndices.Add(baseIdx + 2);
                    }
                }
            }

            if (allVertices.Count == 0) return null;

            return new MeshData
            {
                Vertices = allVertices.ToArray(),
                Indices = allIndices.ToArray(),
                UVs = allUVs.ToArray(),
                UV2s = allUV2s.ToArray()
            };
        }

        /// <summary>
        /// Build mesh data for line features on any thread.
        /// Vertices store the center-line position; UVs store the perpendicular offset direction (unit normal).
        /// Per-feature line-color, line-width and line-offset are baked into vertex data so that
        /// data-driven styling (e.g. line-color via ["match", ["get", "class"], ...]) works.
        /// Shader expands the half-width at render time using a _CSSToLocal uniform.
        /// </summary>
        public static MeshData BuildLineMeshData(List<VectorTileFeature> features, int extent,
            Expressions.Expression colorExpr, Expressions.Expression opacityExpr,
            Expressions.Expression widthExpr, Expressions.Expression offsetExpr, float zoom,
            string sourceId = null, string sourceLayer = null,
            Expressions.IFeatureStateStore featureStateStore = null,
            bool emitLineProgress = false)
        {
            var allVertices = new List<Vector3>(512);
            var allIndices = new List<int>(768);
            var allUVs = new List<Vector2>(512);
            var allUV2s = new List<Vector2>(512);
            var allUV3s = new List<Vector2>(512);
            var allUV4s = emitLineProgress ? new List<Vector2>(512) : null;
            var allColors = new List<Color32>(512);

            float invExtent = 1f / extent;

            var defaultColor = new Color(0f, 0f, 0f, 1f);
            const float defaultOpacity = 1f;
            const float defaultWidth = 1f;
            const float defaultOffset = 0f;

            for (int fi = 0; fi < features.Count; fi++)
            {
                var feature = features[fi];
                // MapLibre GL JS renders line layers for both LineString and Polygon boundaries
                if (feature.Type != GeometryType.LineString &&
                    feature.Type != GeometryType.Polygon) continue;

                var ctx = new Expressions.EvaluationContext(zoom, feature)
                {
                    SourceId = sourceId,
                    SourceLayer = sourceLayer,
                    FeatureStateStore = featureStateStore,
                };
                Color color = colorExpr != null
                    ? colorExpr.EvaluateColor(ctx, defaultColor) : defaultColor;
                float opacity = opacityExpr != null
                    ? opacityExpr.EvaluateFloat(ctx, defaultOpacity) : defaultOpacity;
                float widthCSS = widthExpr != null
                    ? widthExpr.EvaluateFloat(ctx, defaultWidth) : defaultWidth;
                float offsetCSS = offsetExpr != null
                    ? offsetExpr.EvaluateFloat(ctx, defaultOffset) : defaultOffset;

                color.a *= opacity;
                var color32 = (Color32)color;

                var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);

                for (int ri = 0; ri < rings.Count; ri++)
                {
                    var line = rings[ri];
                    if (line.Count < 2) continue;

                    int progressStart = allUV4s?.Count ?? 0;
                    float ringTotalLen = BuildLineStrip(line, invExtent, color32, widthCSS, offsetCSS,
                        allVertices, allIndices, allUVs, allUV2s, allUV3s, allColors,
                        emitLineProgress, allUV4s);

                    // Convert per-vertex cumulative length (stashed unnormalised in
                    // UV4.x by BuildLineStrip when emitLineProgress is on) to a
                    // 0..1 ratio for the ring. line-gradient is per-feature in the
                    // spec, so each ring resets to 0 → 1.
                    if (emitLineProgress && ringTotalLen > 0f && allUV4s != null)
                    {
                        float inv = 1f / ringTotalLen;
                        for (int v = progressStart; v < allUV4s.Count; v++)
                        {
                            var u = allUV4s[v];
                            allUV4s[v] = new Vector2(u.x * inv, u.y);
                        }
                    }
                }
            }

            if (allVertices.Count == 0) return null;

            return new MeshData
            {
                Vertices = allVertices.ToArray(),
                Indices = allIndices.ToArray(),
                UVs = allUVs.ToArray(),
                UV2s = allUV2s.ToArray(),
                UV3s = allUV3s.ToArray(),
                UV4s = allUV4s?.ToArray(),
                Colors = allColors.ToArray()
            };
        }

        /// <summary>
        /// Build 3D extrusion mesh for polygon features.
        /// Each polygon is extruded from base height to top height with roof and side walls.
        /// Heights are in meters and converted to tile-local Y via metersToLocal.
        /// Per-feature color is baked into vertex colors.
        /// </summary>
        public static MeshData BuildFillExtrusionMeshData(
            List<VectorTileFeature> features, int extent,
            Expressions.Expression heightExpr, Expressions.Expression baseExpr,
            Expressions.Expression colorExpr, Expressions.Expression opacityExpr,
            float zoom, float metersToLocal,
            string sourceId = null, string sourceLayer = null,
            Expressions.IFeatureStateStore featureStateStore = null)
        {
            var allVertices = new List<Vector3>(1024);
            var allNormals = new List<Vector3>(1024);
            var allIndices = new List<int>(2048);
            var allColors = new List<Color32>(1024);

            float invExtent = 1f / extent;
            var defaultColor = new Color(0f, 0f, 0f, 1f);

            for (int fi = 0; fi < features.Count; fi++)
            {
                var feature = features[fi];
                if (feature.Type != GeometryType.Polygon) continue;

                var ctx = new Expressions.EvaluationContext(zoom, feature)
                {
                    SourceId = sourceId,
                    SourceLayer = sourceLayer,
                    FeatureStateStore = featureStateStore,
                };
                float height = heightExpr != null ? heightExpr.EvaluateFloat(ctx, 0f) : 0f;
                float baseHeight = baseExpr != null ? baseExpr.EvaluateFloat(ctx, 0f) : 0f;
                Color color = colorExpr != null ? colorExpr.EvaluateColor(ctx, defaultColor) : defaultColor;
                float opacity = opacityExpr != null ? opacityExpr.EvaluateFloat(ctx, 1f) : 1f;
                color.a *= opacity;
                var color32 = (Color32)color;

                float topY = height * metersToLocal;
                float bottomY = baseHeight * metersToLocal;

                var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);
                var polygons = GeometryDecoder.ClassifyPolygonRings(rings);

                for (int pi = 0; pi < polygons.Count; pi++)
                {
                    var polygon = polygons[pi];
                    if (polygon.Count == 0) continue;

                    var exterior = polygon[0];
                    var holes = polygon.Count > 1 ? polygon.GetRange(1, polygon.Count - 1) : null;

                    // --- Roof (top face) ---
                    var (vertices2D, indices) = EarClipTriangulator.Triangulate(exterior, holes);
                    int baseIndex = allVertices.Count;

                    for (int vi = 0; vi < vertices2D.Count; vi++)
                    {
                        var v = vertices2D[vi];
                        float x = v.x * invExtent - 0.5f;
                        float z = -(v.y * invExtent - 0.5f);
                        allVertices.Add(new Vector3(x, topY, z));
                        allNormals.Add(Vector3.up);
                        allColors.Add(color32);
                    }

                    // EarClipTriangulator normalizes to CW in Y-down (MVT) coords.
                    // After z = -y transform, CW maps to upward-facing (+Y) in Unity XZ.
                    // No winding reversal needed.
                    for (int ii = 0; ii < indices.Count; ii++)
                        allIndices.Add(baseIndex + indices[ii]);

                    // --- Side walls ---
                    // Build walls for exterior ring
                    BuildExtrusionWalls(exterior, invExtent, topY, bottomY, color32,
                        allVertices, allNormals, allIndices, allColors);

                    // Build walls for hole rings (wound in opposite direction)
                    if (holes != null)
                    {
                        for (int hi = 0; hi < holes.Count; hi++)
                            BuildExtrusionWalls(holes[hi], invExtent, topY, bottomY, color32,
                                allVertices, allNormals, allIndices, allColors);
                    }
                }
            }

            if (allVertices.Count == 0) return null;

            return new MeshData
            {
                Vertices = allVertices.ToArray(),
                Normals = allNormals.ToArray(),
                Indices = allIndices.ToArray(),
                Colors = allColors.ToArray()
            };
        }

        /// <summary>
        /// Build side wall quads for an extrusion ring.
        /// Each edge of the ring becomes a quad (two triangles) with outward-facing normal.
        /// </summary>
        private static void BuildExtrusionWalls(List<Vector2> ring, float invExtent,
            float topY, float bottomY, Color32 color,
            List<Vector3> vertices, List<Vector3> normals, List<int> indices, List<Color32> colors)
        {
            for (int i = 0; i < ring.Count - 1; i++)
            {
                var p0 = ring[i];
                var p1 = ring[i + 1];

                float x0 = p0.x * invExtent - 0.5f;
                float z0 = -(p0.y * invExtent - 0.5f);
                float x1 = p1.x * invExtent - 0.5f;
                float z1 = -(p1.y * invExtent - 0.5f);

                // Wall normal: perpendicular to edge in XZ plane, pointing outward.
                // Z-axis negation flips the ring winding, so negate the normal
                // and reverse the quad winding to compensate.
                float dx = x1 - x0;
                float dz = z1 - z0;
                float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
                if (len < 1e-8f) continue;

                float nx = -dz / len;
                float nz = dx / len;
                var wallNormal = new Vector3(nx, 0f, nz);

                int baseIdx = vertices.Count;

                // Quad: top-left, top-right, bottom-right, bottom-left
                vertices.Add(new Vector3(x0, topY, z0));
                vertices.Add(new Vector3(x1, topY, z1));
                vertices.Add(new Vector3(x1, bottomY, z1));
                vertices.Add(new Vector3(x0, bottomY, z0));

                normals.Add(wallNormal);
                normals.Add(wallNormal);
                normals.Add(wallNormal);
                normals.Add(wallNormal);

                colors.Add(color);
                colors.Add(color);
                colors.Add(color);
                colors.Add(color);

                // Reversed winding (CW → CCW) to match negated normal
                indices.Add(baseIdx);
                indices.Add(baseIdx + 2);
                indices.Add(baseIdx + 1);
                indices.Add(baseIdx);
                indices.Add(baseIdx + 3);
                indices.Add(baseIdx + 2);
            }
        }

        private static float BuildLineStrip(List<Vector2> line, float invExtent,
            Color32 color, float widthCSS, float offsetCSS,
            List<Vector3> vertices, List<int> indices,
            List<Vector2> uvs, List<Vector2> uv2s, List<Vector2> uv3s, List<Color32> colors,
            bool emitLineProgress, List<Vector2> uv4s)
        {
            float cumDist = 0f;
            var uv3 = new Vector2(widthCSS, offsetCSS);

            for (int i = 0; i < line.Count - 1; i++)
            {
                var p0 = line[i];
                var p1 = line[i + 1];

                float x0 = p0.x * invExtent - 0.5f;
                float z0 = -(p0.y * invExtent - 0.5f);
                float x1 = p1.x * invExtent - 0.5f;
                float z1 = -(p1.y * invExtent - 0.5f);

                float dx = x1 - x0;
                float dz = z1 - z0;
                float lenSq = dx * dx + dz * dz;
                if (lenSq < 1e-16f) continue;

                float segLen = (float)System.Math.Sqrt(lenSq);
                float invLen = 1f / segLen;
                float nx = -dz * invLen;
                float nz = dx * invLen;

                int baseIdx = vertices.Count;

                // Vertex positions = center of the line (no width offset)
                vertices.Add(new Vector3(x0, 0f, z0));
                vertices.Add(new Vector3(x0, 0f, z0));
                vertices.Add(new Vector3(x1, 0f, z1));
                vertices.Add(new Vector3(x1, 0f, z1));

                // UVs = perpendicular offset direction (unit normal with sign)
                uvs.Add(new Vector2(nx, nz));
                uvs.Add(new Vector2(-nx, -nz));
                uvs.Add(new Vector2(nx, nz));
                uvs.Add(new Vector2(-nx, -nz));

                // UV2.x = cumulative distance along the line in tile-local units
                // UV2.y = side sign: +1 for positive normal side, -1 for negative
                float distEnd = cumDist + segLen;
                uv2s.Add(new Vector2(cumDist, 1f));
                uv2s.Add(new Vector2(cumDist, -1f));
                uv2s.Add(new Vector2(distEnd, 1f));
                uv2s.Add(new Vector2(distEnd, -1f));

                // UV4.x = unnormalised line-progress (cumulative length, same as UV2.x).
                // The caller divides by the ring's total length after the loop so
                // each feature's gradient runs from 0 to 1.
                if (emitLineProgress && uv4s != null)
                {
                    uv4s.Add(new Vector2(cumDist, 0f));
                    uv4s.Add(new Vector2(cumDist, 0f));
                    uv4s.Add(new Vector2(distEnd, 0f));
                    uv4s.Add(new Vector2(distEnd, 0f));
                }

                cumDist = distEnd;

                // UV3 = per-feature line-width (x) and line-offset (y) in CSS pixels
                uv3s.Add(uv3);
                uv3s.Add(uv3);
                uv3s.Add(uv3);
                uv3s.Add(uv3);

                // Vertex color = per-feature line color with opacity
                colors.Add(color);
                colors.Add(color);
                colors.Add(color);
                colors.Add(color);

                indices.Add(baseIdx);
                indices.Add(baseIdx + 2);
                indices.Add(baseIdx + 1);
                indices.Add(baseIdx + 1);
                indices.Add(baseIdx + 2);
                indices.Add(baseIdx + 3);
            }

            return cumDist;
        }
    }
}
