using System;
using System.Text;

namespace MapLibre.Unity.VectorTile
{
    /// <summary>
    /// Minimal Protocol Buffers reader for decoding Mapbox Vector Tiles.
    /// Supports varint, fixed32/64, length-delimited, and zigzag encoding.
    /// </summary>
    public class PbfReader
    {
        private readonly byte[] _data;
        private int _pos;
        private readonly int _end;

        public int Position => _pos;
        public bool HasMore => _pos < _end;

        public PbfReader(byte[] data) : this(data, 0, data.Length) { }

        public PbfReader(byte[] data, int offset, int length)
        {
            _data = data;
            _pos = offset;
            _end = offset + length;
        }

        /// <summary>
        /// Read a field tag (field number + wire type).
        /// </summary>
        public (int fieldNumber, int wireType) ReadTag()
        {
            uint tag = ReadVarintUInt32();
            return ((int)(tag >> 3), (int)(tag & 0x07));
        }

        public uint ReadVarintUInt32()
        {
            uint result = 0;
            int shift = 0;
            while (_pos < _end)
            {
                byte b = _data[_pos++];
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            throw new InvalidOperationException("Unexpected end of data while reading varint");
        }

        public ulong ReadVarintUInt64()
        {
            ulong result = 0;
            int shift = 0;
            while (_pos < _end)
            {
                byte b = _data[_pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            throw new InvalidOperationException("Unexpected end of data while reading varint");
        }

        public int ReadVarintInt32() => (int)ReadVarintUInt32();

        public long ReadVarintInt64() => (long)ReadVarintUInt64();

        /// <summary>
        /// Decode a zigzag-encoded signed integer.
        /// </summary>
        public int ReadSVarintInt32()
        {
            uint n = ReadVarintUInt32();
            return (int)(n >> 1) ^ -(int)(n & 1);
        }

        public long ReadSVarintInt64()
        {
            ulong n = ReadVarintUInt64();
            return (long)(n >> 1) ^ -(long)(n & 1);
        }

        public float ReadFloat()
        {
            if (_pos + 4 > _end) throw new InvalidOperationException("Unexpected end of data");
            float val = BitConverter.ToSingle(_data, _pos);
            _pos += 4;
            return val;
        }

        public double ReadDouble()
        {
            if (_pos + 8 > _end) throw new InvalidOperationException("Unexpected end of data");
            double val = BitConverter.ToDouble(_data, _pos);
            _pos += 8;
            return val;
        }

        public uint ReadFixed32()
        {
            if (_pos + 4 > _end) throw new InvalidOperationException("Unexpected end of data");
            uint val = BitConverter.ToUInt32(_data, _pos);
            _pos += 4;
            return val;
        }

        public ulong ReadFixed64()
        {
            if (_pos + 8 > _end) throw new InvalidOperationException("Unexpected end of data");
            ulong val = BitConverter.ToUInt64(_data, _pos);
            _pos += 8;
            return val;
        }

        public string ReadString()
        {
            int length = ReadVarintInt32();
            if (_pos + length > _end) throw new InvalidOperationException("Unexpected end of data");
            string val = Encoding.UTF8.GetString(_data, _pos, length);
            _pos += length;
            return val;
        }

        public byte[] ReadBytes()
        {
            int length = ReadVarintInt32();
            if (_pos + length > _end) throw new InvalidOperationException("Unexpected end of data");
            byte[] val = new byte[length];
            Buffer.BlockCopy(_data, _pos, val, 0, length);
            _pos += length;
            return val;
        }

        /// <summary>
        /// Read a length-delimited field and return a sub-reader for its contents.
        /// </summary>
        public PbfReader ReadMessage()
        {
            int length = ReadVarintInt32();
            if (_pos + length > _end) throw new InvalidOperationException("Unexpected end of data");
            var sub = new PbfReader(_data, _pos, length);
            _pos += length;
            return sub;
        }

        /// <summary>
        /// Read a packed repeated field of varints.
        /// </summary>
        public uint[] ReadPackedUInt32()
        {
            int length = ReadVarintInt32();
            int endPos = _pos + length;
            if (endPos > _end) throw new InvalidOperationException("Unexpected end of data");

            // Estimate capacity
            var list = new System.Collections.Generic.List<uint>(length / 2 + 1);
            while (_pos < endPos)
            {
                list.Add(ReadVarintUInt32());
            }
            return list.ToArray();
        }

        /// <summary>
        /// Read a packed repeated field of zigzag-encoded signed varints.
        /// </summary>
        public int[] ReadPackedSInt32()
        {
            int length = ReadVarintInt32();
            int endPos = _pos + length;
            if (endPos > _end) throw new InvalidOperationException("Unexpected end of data");

            var list = new System.Collections.Generic.List<int>(length / 2 + 1);
            while (_pos < endPos)
            {
                list.Add(ReadSVarintInt32());
            }
            return list.ToArray();
        }

        /// <summary>
        /// Skip a field based on its wire type.
        /// </summary>
        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case 0: // Varint
                    ReadVarintUInt64();
                    break;
                case 1: // 64-bit
                    _pos += 8;
                    break;
                case 2: // Length-delimited
                    int len = ReadVarintInt32();
                    _pos += len;
                    break;
                case 5: // 32-bit
                    _pos += 4;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown wire type: {wireType}");
            }
        }
    }
}
