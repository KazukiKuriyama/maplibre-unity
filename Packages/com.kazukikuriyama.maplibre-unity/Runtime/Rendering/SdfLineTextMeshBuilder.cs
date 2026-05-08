using System.Collections.Generic;
using MapLibre.Unity.Source;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Builds an SDF text mesh whose glyph quads follow a 2D world-space
    /// path. Used by <c>SymbolRenderer</c> for <c>symbol-placement: "line"</c>
    /// (and "line-center") when a glyph URL is configured -- the TextMeshPro
    /// path remains the fallback for projects without one.
    ///
    /// <para>
    /// The mesh is generated once per dispatch in world XZ coordinates so the
    /// resulting GameObject can sit under the map root with identity scale.
    /// Camera / pitch / bearing transforms are still applied to the parent;
    /// the mesh just freezes the curve shape that was visible at dispatch
    /// time. Zoom changes thereafter slide the mesh as a whole rather than
    /// re-warping each glyph -- same trade-off as the point-placement SDF
    /// path. Re-running <see cref="Build"/> after a major zoom change
    /// regenerates the curve.
    /// </para>
    /// </summary>
    public static class SdfLineTextMeshBuilder
    {
        /// <summary>
        /// Walk <paramref name="worldPath"/> and emit one rotated quad per
        /// glyph centred on the path. Returns null when the text doesn't
        /// fit between the path endpoints (caller should hide the label).
        /// <paramref name="anyMissing"/> reports whether at least one glyph
        /// was unavailable in the atlas -- the caller can then queue a
        /// glyph fetch and rebuild later.
        /// </summary>
        /// <param name="text">Source string.</param>
        /// <param name="fontstack">Primary font stack (text-font [0]).</param>
        /// <param name="atlas">Glyph atlas to look glyphs up in.</param>
        /// <param name="worldPath">Polyline in world XZ coordinates.</param>
        /// <param name="midDist">Anchor distance along the path. Glyphs are
        /// centred so that the text midpoint lands here.</param>
        /// <param name="textScale">Scale factor mapping glyph-pixel units
        /// to world units (so a 24-pixel glyph becomes the desired CSS
        /// height after multiplying).</param>
        public static Mesh Build(string text, string fontstack, GlyphAtlas atlas,
            Vector2[] worldPath, float midDist, float textScale,
            out bool anyMissing)
        {
            anyMissing = false;
            if (string.IsNullOrEmpty(text) || atlas == null || textScale <= 0f
                || worldPath == null || worldPath.Length < 2)
                return null;

            // Total text advance (world units) -- sum of every glyph's
            // advance metric scaled to world space.
            float totalAdvance = 0f;
            foreach (var c in text)
            {
                if (atlas.TryGet(fontstack, c, out var g))
                    totalAdvance += g.Advance * textScale;
                else
                    anyMissing = true;
            }
            if (totalAdvance <= 0f) return null;

            // Pre-compute segment lengths so we can walk the path by
            // arc-length without repeating vector math per glyph.
            var segLens = new float[worldPath.Length - 1];
            float pathLen = 0f;
            for (int i = 0; i < segLens.Length; i++)
            {
                segLens[i] = Vector2.Distance(worldPath[i], worldPath[i + 1]);
                pathLen += segLens[i];
            }

            float startDist = midDist - totalAdvance * 0.5f;
            // The label has to fit fully inside the path -- if either end
            // overflows, bail out. SymbolRenderer's collision detection
            // expects null-from-build to mean "skip this label".
            if (startDist < 0f || startDist + totalAdvance > pathLen)
                return null;

            int glyphCount = text.Length;
            var verts = new List<Vector3>(glyphCount * 4);
            var uvs = new List<Vector2>(glyphCount * 4);
            var tris = new List<int>(glyphCount * 6);

            float currentDist = startDist;
            foreach (var c in text)
            {
                if (!atlas.TryGet(fontstack, c, out var g))
                    continue;

                float glyphAdvance = g.Advance * textScale;
                if (g.Width <= 0 || g.Height <= 0)
                {
                    // Whitespace / control char -- only consume advance.
                    currentDist += glyphAdvance;
                    continue;
                }

                // Place the glyph centred on currentDist + glyphAdvance/2.
                // We need both the position and the path tangent at that
                // point so the quad rotates with the curve.
                float centerDist = currentDist + glyphAdvance * 0.5f;
                int segIdx = 0;
                float acc = 0f;
                for (int i = 0; i < segLens.Length; i++)
                {
                    if (acc + segLens[i] >= centerDist) { segIdx = i; break; }
                    acc += segLens[i];
                }
                if (segIdx >= segLens.Length) segIdx = segLens.Length - 1;

                float segT = segLens[segIdx] > 0f
                    ? (centerDist - acc) / segLens[segIdx]
                    : 0f;
                Vector2 pos = Vector2.Lerp(worldPath[segIdx], worldPath[segIdx + 1], segT);
                Vector2 dir = (worldPath[segIdx + 1] - worldPath[segIdx]);
                if (dir.sqrMagnitude < 1e-12f) dir = Vector2.right;
                else dir.Normalize();
                Vector2 perp = new Vector2(-dir.y, dir.x); // 90° CCW = "up" in path space

                // Glyph rectangle in path space. Centred horizontally so
                // currentDist..currentDist+glyphAdvance brackets the quad.
                float halfW = g.Width * textScale * 0.5f;
                float top = g.Top * textScale;
                float bottom = top - g.Height * textScale;

                // 4 corners (LL, LR, UR, UL). Mesh sits on the XZ plane,
                // so Y stays at zero -- the parent transform is responsible
                // for any vertical offset.
                Vector3 ll = ToWorld(pos, dir, perp, -halfW, bottom);
                Vector3 lr = ToWorld(pos, dir, perp,  halfW, bottom);
                Vector3 ur = ToWorld(pos, dir, perp,  halfW, top);
                Vector3 ul = ToWorld(pos, dir, perp, -halfW, top);

                int v0 = verts.Count;
                verts.Add(ll); verts.Add(lr); verts.Add(ur); verts.Add(ul);

                Rect uv = g.UVRect;
                uvs.Add(new Vector2(uv.x,            uv.y));
                uvs.Add(new Vector2(uv.x + uv.width, uv.y));
                uvs.Add(new Vector2(uv.x + uv.width, uv.y + uv.height));
                uvs.Add(new Vector2(uv.x,            uv.y + uv.height));

                tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);
                tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 3);

                currentDist += glyphAdvance;
            }

            if (verts.Count == 0) return null;

            var mesh = new Mesh { name = "MapLibre/SdfLineText" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Convert a (along, across) offset in path space into a world XZ
        // position. dir / perp are 2D unit vectors so the resulting world
        // vector lies on the XZ plane with y = 0.
        private static Vector3 ToWorld(Vector2 origin, Vector2 dir, Vector2 perp,
            float along, float across)
        {
            float wx = origin.x + dir.x * along + perp.x * across;
            float wz = origin.y + dir.y * along + perp.y * across;
            return new Vector3(wx, 0f, wz);
        }
    }
}
