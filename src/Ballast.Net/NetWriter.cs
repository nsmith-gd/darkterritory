using System.Buffers.Binary;

namespace Ballast.Net;

/// <summary>Little-endian binary writer for packets. Reused across ticks to avoid allocation.</summary>
public sealed class NetWriter
{
    byte[] _buffer = new byte[1500];

    public int Length { get; private set; }
    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, Length);

    public void Reset() => Length = 0;

    Span<byte> Take(int n)
    {
        if (Length + n > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + n));
        var span = _buffer.AsSpan(Length, n);
        Length += n;
        return span;
    }

    public void U8(byte v) => Take(1)[0] = v;
    public void I8(sbyte v) => Take(1)[0] = (byte)v;
    public void Bool(bool v) => U8(v ? (byte)1 : (byte)0);
    public void U16(ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(Take(2), v);
    public void U32(uint v) => BinaryPrimitives.WriteUInt32LittleEndian(Take(4), v);
    public void I32(int v) => BinaryPrimitives.WriteInt32LittleEndian(Take(4), v);
    public void U64(ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(Take(8), v);
    public void F32(float v) => BinaryPrimitives.WriteSingleLittleEndian(Take(4), v);
    public void F64(double v) => BinaryPrimitives.WriteDoubleLittleEndian(Take(8), v);

    /// <summary>Unsigned LEB128: small numbers (counts, deltas) cost one byte.</summary>
    public void VarU(ulong v)
    {
        while (v >= 0x80)
        {
            U8((byte)(v | 0x80));
            v >>= 7;
        }
        U8((byte)v);
    }

    public void Double3(Double3 v)
    {
        F64(v.X);
        F64(v.Y);
        F64(v.Z);
    }

    /// <summary>Float precision is plenty for velocities and car-local offsets.</summary>
    public void Float3(Double3 v)
    {
        F32((float)v.X);
        F32((float)v.Y);
        F32((float)v.Z);
    }
}

/// <summary>Reader matching <see cref="NetWriter"/>. Throws <see cref="EndOfStreamException"/> on truncated packets.</summary>
public ref struct NetReader(ReadOnlySpan<byte> data)
{
    readonly ReadOnlySpan<byte> _data = data;
    int _pos;

    public readonly int Remaining => _data.Length - _pos;

    ReadOnlySpan<byte> Take(int n)
    {
        if (_pos + n > _data.Length)
            throw new EndOfStreamException("packet truncated");
        var s = _data.Slice(_pos, n);
        _pos += n;
        return s;
    }

    public byte U8() => Take(1)[0];
    public sbyte I8() => (sbyte)Take(1)[0];
    public bool Bool() => U8() != 0;
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    public ulong VarU()
    {
        ulong v = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            byte b = U8();
            v |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return v;
        }
        throw new InvalidDataException("varint too long");
    }

    public Double3 Double3() => new(F64(), F64(), F64());
    public Double3 Float3() => new(F32(), F32(), F32());
}
