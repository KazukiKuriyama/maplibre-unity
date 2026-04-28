using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Per-glyph metrics + atlas UV. All units are texel-space; the renderer
    /// converts to world-space using the desired font size.
    /// </summary>
    public struct AtlasGlyph
    {
        public int Width;       // bitmap width incl. SDF padding
        public int Height;      // bitmap height incl. SDF padding
        public int Left;        // glyph-local x offset from baseline origin
        public int Top;         // glyph-local y offset from baseline origin
        public int Advance;     // x distance to next glyph
        public Rect UVRect;     // sub-rect inside the atlas texture, normalised [0..1]
    }

    /// <summary>
    /// CPU-side glyph atlas. Each glyph's SDF bitmap is packed into a single
    /// <see cref="Texture2D"/> using a simple shelf-packing algorithm; the
    /// resulting UV rectangles are looked up by (fontstack, codepoint).
    ///
    /// <para>
    /// The atlas grows by relocating to a larger texture when the current
    /// shelf row no longer fits an incoming glyph and the texture itself is
    /// full. Memory usage is bounded by <see cref="MaxAtlasSize"/>.
    /// </para>
    /// </summary>
    public class GlyphAtlas
    {
        // Mapbox / MapLibre SDF glyphs are 24x24 with 3px padding per side.
        // Atlas starts at 256x256 (≈ 100 glyphs), doubles on overflow.
        private const int InitialAtlasSize = 256;
        private const int MaxAtlasSize = 4096;

        private Texture2D _texture;
        private int _shelfX;
        private int _shelfY;
        private int _shelfHeight;

        // Two-level lookup: fontstack name → codepoint → glyph metadata.
        private readonly Dictionary<string, Dictionary<uint, AtlasGlyph>> _glyphs = new();

        public Texture2D Texture => _texture;
        public int Width => _texture != null ? _texture.width : 0;
        public int Height => _texture != null ? _texture.height : 0;

        public GlyphAtlas()
        {
            _texture = CreateAtlas(InitialAtlasSize);
        }

        private static Texture2D CreateAtlas(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.Alpha8, mipChain: false)
            {
                name = "MapLibre/GlyphAtlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            // Zero-fill so SDF lookups outside packed glyphs return 0 (= "fully outside").
            var clear = new Color32[size * size];
            tex.SetPixels32(clear);
            tex.Apply(updateMipmaps: false);
            return tex;
        }

        /// <summary>
        /// Look up a previously-added glyph. Returns false when the
        /// (fontstack, codepoint) pair has not been added yet — the caller can
        /// then queue a fetch for the appropriate glyph range.
        /// </summary>
        public bool TryGet(string fontstack, uint codepoint, out AtlasGlyph glyph)
        {
            if (_glyphs.TryGetValue(fontstack, out var perFont)
                && perFont.TryGetValue(codepoint, out glyph))
            {
                return true;
            }
            glyph = default;
            return false;
        }

        /// <summary>
        /// Pack the supplied glyph into the atlas. Re-adding the same
        /// (fontstack, codepoint) is a no-op — first-write wins. Returns the
        /// stored entry (whether new or pre-existing) so callers can use it
        /// immediately.
        /// </summary>
        public AtlasGlyph Add(string fontstack, Glyph g)
        {
            if (!_glyphs.TryGetValue(fontstack, out var perFont))
            {
                perFont = new Dictionary<uint, AtlasGlyph>();
                _glyphs[fontstack] = perFont;
            }
            if (perFont.TryGetValue(g.Id, out var existing)) return existing;

            int w = g.Width;
            int h = g.Height;

            // Empty glyph (whitespace) — store metrics with a zero rect; the
            // mesh builder still needs Advance to lay text out correctly.
            if (g.Bitmap == null || g.Bitmap.Length == 0 || w == 0 || h == 0)
            {
                var emptyEntry = new AtlasGlyph
                {
                    Width = w,
                    Height = h,
                    Left = g.Left,
                    Top = g.Top,
                    Advance = g.Advance,
                    UVRect = new Rect(0, 0, 0, 0),
                };
                perFont[g.Id] = emptyEntry;
                return emptyEntry;
            }

            // Find a packing slot, growing the texture if necessary.
            EnsureRoom(w, h);

            _texture.SetPixels32(_shelfX, _shelfY, w, h, BitmapToColor32(g.Bitmap, w, h));
            _texture.Apply(updateMipmaps: false);

            var entry = new AtlasGlyph
            {
                Width = w,
                Height = h,
                Left = g.Left,
                Top = g.Top,
                Advance = g.Advance,
                UVRect = new Rect(
                    (float)_shelfX / _texture.width,
                    (float)_shelfY / _texture.height,
                    (float)w / _texture.width,
                    (float)h / _texture.height),
            };
            perFont[g.Id] = entry;

            _shelfX += w;
            if (h > _shelfHeight) _shelfHeight = h;
            return entry;
        }

        private void EnsureRoom(int w, int h)
        {
            // Wrap to the next shelf when the current row overflows horizontally.
            if (_shelfX + w > _texture.width)
            {
                _shelfY += _shelfHeight;
                _shelfX = 0;
                _shelfHeight = 0;
            }

            // Grow the atlas if we run out of vertical space. Doubling keeps
            // amortised packing cost O(n).
            while (_shelfY + h > _texture.height)
            {
                int newSize = _texture.width * 2;
                if (newSize > MaxAtlasSize)
                {
                    Debug.LogWarning(
                        $"[GlyphAtlas] Atlas size capped at {MaxAtlasSize}px — " +
                        "glyph dropped. Consider trimming the fontstack range.");
                    return;
                }
                Resize(newSize);
            }
        }

        private void Resize(int newSize)
        {
            // Copy the existing pixels into a larger zero-filled texture and
            // rebuild every UV rect in the lookup. SetPixels32 destinations
            // align to (0,0), so the shelf cursor stays valid as-is.
            var oldTex = _texture;
            var newTex = CreateAtlas(newSize);

            var oldPixels = oldTex.GetPixels32();
            // Place existing pixels in the bottom-left of the new texture.
            newTex.SetPixels32(0, 0, oldTex.width, oldTex.height, oldPixels);
            newTex.Apply(updateMipmaps: false);

            float scale = (float)oldTex.width / newSize;
            foreach (var perFont in _glyphs.Values)
            {
                var keys = new List<uint>(perFont.Keys);
                foreach (var k in keys)
                {
                    var g = perFont[k];
                    g.UVRect = new Rect(
                        g.UVRect.x * scale,
                        g.UVRect.y * scale,
                        g.UVRect.width * scale,
                        g.UVRect.height * scale);
                    perFont[k] = g;
                }
            }

            Object.Destroy(oldTex);
            _texture = newTex;
        }

        private static Color32[] BitmapToColor32(byte[] alpha, int w, int h)
        {
            // PBF glyphs ship a single-channel SDF bitmap; expand into RGBA32
            // so SetPixels32 can take it. Only the alpha channel is read by
            // the SDF text shader.
            var pixels = new Color32[w * h];
            int n = w * h;
            int len = System.Math.Min(alpha.Length, n);
            for (int i = 0; i < len; i++)
            {
                byte v = alpha[i];
                pixels[i] = new Color32(v, v, v, v);
            }
            return pixels;
        }
    }
}
