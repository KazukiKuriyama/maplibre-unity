using System.Collections.Generic;

namespace MapLibre.Unity.VectorTile
{
    /// <summary>
    /// Parsed Mapbox Vector Tile containing multiple layers.
    /// <para>
    /// Once a tile has been built by <see cref="VectorTileParser"/> /
    /// <see cref="MapLibre.Unity.Source.GeoJsonToVectorTile"/>, all of its
    /// fields are immutable from external code: setters and list mutators
    /// are <c>internal</c> so that downstream renderers -- which may run on
    /// a background thread for mesh-build -- can read tile data without
    /// races. The shipping parser/transcoder is the only writer, and it
    /// completes the tile on its own background thread before publishing
    /// the instance.
    /// </para>
    /// </summary>
    public class VectorTileData
    {
        // Backing storage is an internal mutable list so that the parser /
        // GeoJSON transcoder can populate it incrementally. External callers
        // see only the <see cref="IReadOnlyList{T}"/> view.
        internal readonly List<VectorTileLayer> _layers = new();
        public IReadOnlyList<VectorTileLayer> Layers => _layers;

        public VectorTileLayer GetLayer(string name)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Name == name) return _layers[i];
            }
            return null;
        }
    }

    /// <summary>
    /// A single layer within a vector tile. See <see cref="VectorTileData"/>
    /// for the immutability contract.
    /// </summary>
    public class VectorTileLayer
    {
        public string Name { get; internal set; }
        public int Version { get; internal set; } = 2;
        public int Extent { get; internal set; } = 4096;

        internal readonly List<VectorTileFeature> _features = new();
        public IReadOnlyList<VectorTileFeature> Features => _features;

        internal readonly List<string> _keys = new();
        public IReadOnlyList<string> Keys => _keys;

        internal readonly List<VectorTileValue> _values = new();
        public IReadOnlyList<VectorTileValue> Values => _values;
    }

    /// <summary>
    /// A single feature within a vector tile layer. See
    /// <see cref="VectorTileData"/> for the immutability contract.
    /// </summary>
    public class VectorTileFeature
    {
        public ulong Id { get; internal set; }
        public GeometryType Type { get; internal set; }

        /// <summary>
        /// Raw geometry commands (MVT encoded).
        /// </summary>
        public uint[] RawGeometry { get; internal set; }

        /// <summary>
        /// Tag indices (alternating key_index, value_index).
        /// </summary>
        public uint[] Tags { get; internal set; }

        /// <summary>
        /// Reference to parent layer for resolving tags.
        /// </summary>
        public VectorTileLayer Layer { get; internal set; }

        /// <summary>
        /// Get a property value by key name.
        /// </summary>
        public VectorTileValue GetProperty(string key)
        {
            if (Tags == null || Layer == null) return null;
            for (int i = 0; i + 1 < Tags.Length; i += 2)
            {
                int keyIdx = (int)Tags[i];
                int valIdx = (int)Tags[i + 1];
                if (keyIdx < Layer.Keys.Count && Layer.Keys[keyIdx] == key)
                {
                    return valIdx < Layer.Values.Count ? Layer.Values[valIdx] : null;
                }
            }
            return null;
        }

        /// <summary>
        /// Get all properties as a dictionary.
        /// </summary>
        public Dictionary<string, VectorTileValue> GetProperties()
        {
            var props = new Dictionary<string, VectorTileValue>();
            if (Tags == null || Layer == null) return props;
            for (int i = 0; i + 1 < Tags.Length; i += 2)
            {
                int keyIdx = (int)Tags[i];
                int valIdx = (int)Tags[i + 1];
                if (keyIdx < Layer.Keys.Count && valIdx < Layer.Values.Count)
                {
                    props[Layer.Keys[keyIdx]] = Layer.Values[valIdx];
                }
            }
            return props;
        }
    }

    /// <summary>
    /// MVT geometry types.
    /// </summary>
    public enum GeometryType
    {
        Unknown = 0,
        Point = 1,
        LineString = 2,
        Polygon = 3
    }

    /// <summary>
    /// A value in the MVT value table. See <see cref="VectorTileData"/> for
    /// the immutability contract.
    /// </summary>
    public class VectorTileValue
    {
        public string StringValue { get; internal set; }
        public float? FloatValue { get; internal set; }
        public double? DoubleValue { get; internal set; }
        public long? IntValue { get; internal set; }
        public ulong? UIntValue { get; internal set; }
        public long? SIntValue { get; internal set; }
        public bool? BoolValue { get; internal set; }

        public override string ToString()
        {
            if (StringValue != null) return StringValue;
            if (FloatValue.HasValue) return FloatValue.Value.ToString();
            if (DoubleValue.HasValue) return DoubleValue.Value.ToString();
            if (IntValue.HasValue) return IntValue.Value.ToString();
            if (UIntValue.HasValue) return UIntValue.Value.ToString();
            if (SIntValue.HasValue) return SIntValue.Value.ToString();
            if (BoolValue.HasValue) return BoolValue.Value.ToString();
            return "";
        }

        public double ToDouble()
        {
            if (DoubleValue.HasValue) return DoubleValue.Value;
            if (FloatValue.HasValue) return FloatValue.Value;
            if (IntValue.HasValue) return IntValue.Value;
            if (UIntValue.HasValue) return UIntValue.Value;
            if (SIntValue.HasValue) return SIntValue.Value;
            return 0;
        }
    }
}
