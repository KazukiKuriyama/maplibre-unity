using MapLibre.Unity.VectorTile;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Decodes MapLibre / Mapbox glyph PBFs into <see cref="GlyphPbf"/>.
    /// Schema reference: https://github.com/mapbox/glyph-pbf-composite/blob/master/proto/glyphs.proto
    ///
    /// <para>
    /// We reuse <see cref="PbfReader"/> from the vector-tile path because the
    /// wire format is identical (proto2 / proto3 wire compatibility); only the
    /// field numbers differ. No allocations beyond the produced data classes.
    /// </para>
    /// </summary>
    public static class GlyphPbfParser
    {
        // Tag wire type constants from the protobuf spec.
        private const int WireVarint = 0;
        private const int WireFixed64 = 1;
        private const int WireLengthDelimited = 2;
        private const int WireFixed32 = 5;

        /// <summary>
        /// Parse the supplied bytes (NOT gzip-wrapped -- caller decompresses if
        /// needed, same as <see cref="VectorTileSource"/> does for vector tiles).
        /// </summary>
        public static GlyphPbf Parse(byte[] data)
        {
            var pbf = new GlyphPbf();
            if (data == null || data.Length == 0) return pbf;

            var reader = new PbfReader(data);
            while (reader.HasMore)
            {
                var (field, wireType) = reader.ReadTag();
                if (field == 1 && wireType == WireLengthDelimited)
                {
                    var stack = ParseFontstack(reader.ReadMessage());
                    if (stack != null) pbf.Stacks.Add(stack);
                }
                else
                {
                    reader.Skip(wireType);
                }
            }
            return pbf;
        }

        private static GlyphFontstack ParseFontstack(PbfReader reader)
        {
            var stack = new GlyphFontstack();
            while (reader.HasMore)
            {
                var (field, wireType) = reader.ReadTag();
                switch (field)
                {
                    case 1 when wireType == WireLengthDelimited:
                        stack.Name = reader.ReadString();
                        break;
                    case 2 when wireType == WireLengthDelimited:
                        stack.Range = reader.ReadString();
                        break;
                    case 3 when wireType == WireLengthDelimited:
                        var glyph = ParseGlyph(reader.ReadMessage());
                        if (glyph != null) stack.Glyphs.Add(glyph);
                        break;
                    default:
                        reader.Skip(wireType);
                        break;
                }
            }
            return stack;
        }

        private static Glyph ParseGlyph(PbfReader reader)
        {
            var glyph = new Glyph();
            while (reader.HasMore)
            {
                var (field, wireType) = reader.ReadTag();
                switch (field)
                {
                    case 1 when wireType == WireVarint:
                        glyph.Id = reader.ReadVarintUInt32();
                        break;
                    case 2 when wireType == WireLengthDelimited:
                        glyph.Bitmap = reader.ReadBytes();
                        break;
                    case 3 when wireType == WireVarint:
                        glyph.Width = (int)reader.ReadVarintUInt32();
                        break;
                    case 4 when wireType == WireVarint:
                        glyph.Height = (int)reader.ReadVarintUInt32();
                        break;
                    case 5 when wireType == WireVarint:
                        glyph.Left = reader.ReadSVarintInt32();
                        break;
                    case 6 when wireType == WireVarint:
                        glyph.Top = reader.ReadSVarintInt32();
                        break;
                    case 7 when wireType == WireVarint:
                        glyph.Advance = (int)reader.ReadVarintUInt32();
                        break;
                    default:
                        reader.Skip(wireType);
                        break;
                }
            }
            return glyph;
        }
    }
}
