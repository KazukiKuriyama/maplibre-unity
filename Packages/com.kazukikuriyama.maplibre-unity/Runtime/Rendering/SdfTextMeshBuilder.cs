using System.Collections.Generic;
using MapLibre.Unity.Source;
using UnityEngine;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Builds a single <see cref="Mesh"/> for a string of text using glyphs
    /// from a <see cref="GlyphAtlas"/>. The output is one quad per glyph; the
    /// SDF text shader (<c>MapLibre/SdfText</c>) samples the atlas and renders
    /// the body + halo from the distance field.
    ///
    /// <para>
    /// Geometry is generated in glyph-local pixels with the baseline origin
    /// at <c>(0, 0)</c>. Callers scale the resulting GameObject's transform
    /// to the desired font size; an SDF font that ships with 24-pixel cells
    /// looks correct at <c>scale = textSizeCSS / 24f</c>.
    /// </para>
    /// </summary>
    public static class SdfTextMeshBuilder
    {
        // Mapbox glyph PBFs render onto a 24-pixel SDF cell. SymbolRenderer
        // computes a single `textScale` value sized for a TMP em-square of 36
        // (TmpFontSize), so we pre-scale the SDF mesh by 36/24 = 1.5 to make
        // both pipelines hit the same on-screen size for a given text-size.
        // Without this, SDF labels render ~33% smaller than the equivalent TMP
        // labels and end up sub-pixel on typical map zooms -- the "dots and
        // stains" appearance that's reported when a glyphs URL is wired up.
        private const float SdfCellPx = 24f;
        private const float TmpEmPx = 36f;
        private const float SdfMeshScale = TmpEmPx / SdfCellPx;

        /// <summary>
        /// Returns the metrics-only result of laying out <paramref name="text"/>
        /// without producing a mesh. Useful for collision detection where the
        /// renderer only needs to know how wide the label will be.
        /// </summary>
        public struct LayoutResult
        {
            public float Width;     // total advance in glyph pixels
            public float MinY;      // bottom edge of the lowest glyph
            public float MaxY;      // top edge of the tallest glyph
            public bool AnyMissing; // at least one codepoint was not in the atlas
        }

        public static LayoutResult Measure(string text, string fontstack, GlyphAtlas atlas)
        {
            var result = new LayoutResult { MinY = 0, MaxY = 0, Width = 0 };
            if (string.IsNullOrEmpty(text) || atlas == null) return result;

            float x = 0;
            foreach (var c in text)
            {
                uint cp = c;
                if (!atlas.TryGet(fontstack, cp, out var g))
                {
                    result.AnyMissing = true;
                    continue;
                }
                if (g.Width > 0 && g.Height > 0)
                {
                    float bottom = g.Top - g.Height;
                    float top = g.Top;
                    if (bottom < result.MinY) result.MinY = bottom;
                    if (top > result.MaxY) result.MaxY = top;
                }
                x += g.Advance;
            }
            // Match the units the mesh exposes (see SdfMeshScale comment).
            result.Width = x * SdfMeshScale;
            result.MinY *= SdfMeshScale;
            result.MaxY *= SdfMeshScale;
            return result;
        }

        /// <summary>
        /// Build a mesh with one quad per glyph in <paramref name="text"/>.
        /// Returns null when <paramref name="text"/> is empty or every glyph is
        /// missing from the atlas. The boolean output flags whether any
        /// codepoint was missing -- the caller can then trigger a glyph fetch
        /// and rebuild later.
        /// </summary>
        public static Mesh Build(string text, string fontstack, GlyphAtlas atlas,
            out bool anyMissing)
        {
            anyMissing = false;
            if (string.IsNullOrEmpty(text) || atlas == null) return null;

            var verts = new List<Vector3>(text.Length * 4);
            var uvs = new List<Vector2>(text.Length * 4);
            var tris = new List<int>(text.Length * 6);

            float pen = 0;
            foreach (var c in text)
            {
                uint cp = c;
                if (!atlas.TryGet(fontstack, cp, out var g))
                {
                    anyMissing = true;
                    continue;
                }
                if (g.Width <= 0 || g.Height <= 0)
                {
                    // Whitespace / non-printing -- advance only.
                    pen += g.Advance;
                    continue;
                }

                int v0 = verts.Count;
                // Mapbox glyph bitmaps include a 3-pixel SDF buffer around the
                // metrics width/height. The atlas slot covers the full buffered
                // bitmap, so the quad must extend by the buffer on every side
                // and the anchor (left/top) has to step back by the buffer too.
                // Match the maplibre-gl-js shaping.ts convention so labels are
                // positioned identically across implementations.
                const int buf = GlyphAtlas.SdfBuffer;
                float qLeft = (pen + g.Left - buf) * SdfMeshScale;
                float qRight = qLeft + (g.Width + 2 * buf) * SdfMeshScale;
                float qTop = (g.Top + buf) * SdfMeshScale;
                float qBottom = qTop - (g.Height + 2 * buf) * SdfMeshScale;

                verts.Add(new Vector3(qLeft,  qBottom, 0));
                verts.Add(new Vector3(qRight, qBottom, 0));
                verts.Add(new Vector3(qRight, qTop,    0));
                verts.Add(new Vector3(qLeft,  qTop,    0));

                // Atlas Y grows downward (Texture2D.SetPixels32(0,0,...) puts
                // pixel (0,0) at the bottom-left for SetPixels -- but our
                // shelf-packing uses raw integer coordinates that match the
                // texture layout used by Texture2D itself). We treat
                // UVRect.y as the "top of the bitmap in atlas space" and pair
                // it with the glyph's top vertex to keep the SDF upright.
                Rect uv = g.UVRect;
                uvs.Add(new Vector2(uv.x,            uv.y));
                uvs.Add(new Vector2(uv.x + uv.width, uv.y));
                uvs.Add(new Vector2(uv.x + uv.width, uv.y + uv.height));
                uvs.Add(new Vector2(uv.x,            uv.y + uv.height));

                tris.Add(v0);
                tris.Add(v0 + 1);
                tris.Add(v0 + 2);
                tris.Add(v0);
                tris.Add(v0 + 2);
                tris.Add(v0 + 3);

                pen += g.Advance;
            }

            if (verts.Count == 0) return null;

            var mesh = new Mesh { name = "MapLibre/SdfText" };
            // 16-bit indices are enough for 16k glyphs per label, which is way
            // beyond any realistic map symbol.
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
