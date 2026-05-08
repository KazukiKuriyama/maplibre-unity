using System.Collections.Generic;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// One glyph from a MapLibre / Mapbox glyph PBF. Geometry is in glyph-local
    /// pixels with a 3-pixel padding on every side that the SDF distance field
    /// uses; <see cref="Width"/> / <see cref="Height"/> include that padding.
    /// </summary>
    /// <remarks>
    /// Field shape mirrors the protobuf schema:
    /// <code>
    /// message glyph {
    ///   required uint32 id      = 1;
    ///   optional bytes  bitmap  = 2;  // alpha8 SDF, may be empty for empty cells
    ///   required uint32 width   = 3;  // bitmap width  in px (incl. 3px padding)
    ///   required uint32 height  = 4;  // bitmap height in px (incl. 3px padding)
    ///   required sint32 left    = 5;  // x offset of the bitmap from the baseline origin
    ///   required sint32 top     = 6;  // y offset of the bitmap from the baseline origin
    ///   required uint32 advance = 7;  // x distance to advance to the next glyph
    /// }
    /// </code>
    /// </remarks>
    public class Glyph
    {
        /// <summary>Unicode codepoint this glyph renders.</summary>
        public uint Id;

        /// <summary>Single-channel SDF bitmap (Alpha8). Null/empty for whitespace.</summary>
        public byte[] Bitmap;

        /// <summary>Bitmap width in pixels -- includes the 3px SDF padding.</summary>
        public int Width;

        /// <summary>Bitmap height in pixels -- includes the 3px SDF padding.</summary>
        public int Height;

        /// <summary>X offset of the bitmap top-left from the baseline origin.</summary>
        public int Left;

        /// <summary>Y offset of the bitmap top-left from the baseline origin (positive = up).</summary>
        public int Top;

        /// <summary>X distance to the next glyph's origin, in pixels.</summary>
        public int Advance;
    }

    /// <summary>
    /// One named font's worth of glyphs in a single fixed Unicode range
    /// (typically 256 codepoints, e.g. 0-255 / 256-511 / ...). Multiple
    /// fontstacks can sit in the same PBF when the URL template requests
    /// a comma-joined fontstack name.
    /// </summary>
    public class GlyphFontstack
    {
        /// <summary>Font stack name as supplied to the URL template (e.g. "Noto Sans Regular").</summary>
        public string Name;

        /// <summary>Range string in the form "start-end" (e.g. "0-255").</summary>
        public string Range;

        /// <summary>Glyphs in this range. Order is not guaranteed; index by Id.</summary>
        public List<Glyph> Glyphs = new();
    }

    /// <summary>
    /// Top-level decode result of one glyph PBF. A single PBF can carry
    /// multiple fontstacks but the common case is exactly one.
    /// </summary>
    public class GlyphPbf
    {
        public List<GlyphFontstack> Stacks = new();
    }
}
