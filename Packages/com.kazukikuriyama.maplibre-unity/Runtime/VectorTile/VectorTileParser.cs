using System;
using System.Collections.Generic;

namespace MapLibre.Unity.VectorTile
{
    /// <summary>
    /// Parses Mapbox Vector Tile (MVT) binary data into VectorTileData.
    /// Implements the MVT specification v2.1.
    /// See: https://github.com/mapbox/vector-tile-spec/tree/master/2.1
    /// </summary>
    public static class VectorTileParser
    {
        // Tile field numbers
        private const int TileFieldLayers = 3;

        // Layer field numbers
        private const int LayerFieldVersion = 15;
        private const int LayerFieldName = 1;
        private const int LayerFieldFeatures = 2;
        private const int LayerFieldKeys = 3;
        private const int LayerFieldValues = 4;
        private const int LayerFieldExtent = 5;

        // Feature field numbers
        private const int FeatureFieldId = 1;
        private const int FeatureFieldTags = 2;
        private const int FeatureFieldType = 3;
        private const int FeatureFieldGeometry = 4;

        // Value field numbers
        private const int ValueFieldString = 1;
        private const int ValueFieldFloat = 2;
        private const int ValueFieldDouble = 3;
        private const int ValueFieldInt = 4;
        private const int ValueFieldUInt = 5;
        private const int ValueFieldSInt = 6;
        private const int ValueFieldBool = 7;

        /// <summary>
        /// Parse raw MVT binary data into a VectorTileData structure. Eager:
        /// reads every layer up-front before returning, suitable for use
        /// inside <see cref="System.Threading.Tasks.Task.Run"/> on threaded
        /// platforms where the calling coroutine is happy to wait.
        /// </summary>
        public static VectorTileData Parse(byte[] data)
        {
            var tile = new VectorTileData();
            foreach (var layer in ParseLazy(data))
                tile._layers.Add(layer);
            return tile;
        }

        /// <summary>
        /// Parse raw MVT binary data into a sequence of <see cref="VectorTileLayer"/>
        /// values, yielded one at a time. Lets callers (e.g. the WebGL
        /// single-thread tile pipeline) consume layers with a per-frame time
        /// budget and yield back to the main loop between layers, avoiding
        /// the freeze that an eager <see cref="Parse(byte[])"/> would cause
        /// when there is no real worker thread to run the parse on.
        ///
        /// The yielded layers are appended to a fresh <see cref="VectorTileData"/>
        /// by the synchronous wrapper above, so the two APIs return
        /// equivalent data -- same set of layers, same internal state, just
        /// produced incrementally.
        /// </summary>
        public static IEnumerable<VectorTileLayer> ParseLazy(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Vector tile data is null or empty");

            var reader = new PbfReader(data);
            while (reader.HasMore)
            {
                var (fieldNumber, wireType) = reader.ReadTag();
                if (fieldNumber == TileFieldLayers && wireType == 2)
                {
                    var layer = ParseLayer(reader.ReadMessage());
                    if (layer != null)
                        yield return layer;
                }
                else
                {
                    reader.Skip(wireType);
                }
            }
        }

        private static VectorTileLayer ParseLayer(PbfReader reader)
        {
            var layer = new VectorTileLayer();

            while (reader.HasMore)
            {
                var (fieldNumber, wireType) = reader.ReadTag();

                switch (fieldNumber)
                {
                    case LayerFieldVersion:
                        layer.Version = reader.ReadVarintInt32();
                        break;

                    case LayerFieldName:
                        layer.Name = reader.ReadString();
                        break;

                    case LayerFieldFeatures:
                        var feature = ParseFeature(reader.ReadMessage(), layer);
                        if (feature != null)
                            layer._features.Add(feature);
                        break;

                    case LayerFieldKeys:
                        layer._keys.Add(reader.ReadString());
                        break;

                    case LayerFieldValues:
                        layer._values.Add(ParseValue(reader.ReadMessage()));
                        break;

                    case LayerFieldExtent:
                        layer.Extent = reader.ReadVarintInt32();
                        break;

                    default:
                        reader.Skip(wireType);
                        break;
                }
            }

            return layer;
        }

        private static VectorTileFeature ParseFeature(PbfReader reader, VectorTileLayer layer)
        {
            var feature = new VectorTileFeature { Layer = layer };

            while (reader.HasMore)
            {
                var (fieldNumber, wireType) = reader.ReadTag();

                switch (fieldNumber)
                {
                    case FeatureFieldId:
                        feature.Id = reader.ReadVarintUInt64();
                        break;

                    case FeatureFieldTags:
                        feature.Tags = reader.ReadPackedUInt32();
                        break;

                    case FeatureFieldType:
                        feature.Type = (GeometryType)reader.ReadVarintInt32();
                        break;

                    case FeatureFieldGeometry:
                        feature.RawGeometry = reader.ReadPackedUInt32();
                        break;

                    default:
                        reader.Skip(wireType);
                        break;
                }
            }

            return feature;
        }

        private static VectorTileValue ParseValue(PbfReader reader)
        {
            var value = new VectorTileValue();

            while (reader.HasMore)
            {
                var (fieldNumber, wireType) = reader.ReadTag();

                switch (fieldNumber)
                {
                    case ValueFieldString:
                        value.StringValue = reader.ReadString();
                        break;
                    case ValueFieldFloat:
                        value.FloatValue = reader.ReadFloat();
                        break;
                    case ValueFieldDouble:
                        value.DoubleValue = reader.ReadDouble();
                        break;
                    case ValueFieldInt:
                        value.IntValue = reader.ReadVarintInt64();
                        break;
                    case ValueFieldUInt:
                        value.UIntValue = reader.ReadVarintUInt64();
                        break;
                    case ValueFieldSInt:
                        value.SIntValue = reader.ReadSVarintInt64();
                        break;
                    case ValueFieldBool:
                        value.BoolValue = reader.ReadVarintInt32() != 0;
                        break;
                    default:
                        reader.Skip(wireType);
                        break;
                }
            }

            return value;
        }
    }
}
