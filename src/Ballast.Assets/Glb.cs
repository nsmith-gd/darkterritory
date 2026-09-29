using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Ballast.Assets;

/// <summary>
/// The binary glTF container (glTF 2.0 §4.4): the JSON document and its one binary chunk, with typed reads of accessors.
/// Just what the Blender exporter writes: embedded buffers, no sparse accessors, no external files.
/// </summary>
public sealed class Glb
{
    public JsonElement Root { get; }
    readonly byte[] _bin;

    Glb(JsonDocument doc, byte[] bin)
    {
        // The element keeps its document alive; nothing here is pooled, so it needn't be disposed.
        Root = doc.RootElement;
        _bin = bin;
    }

    public static Glb Read(byte[] data)
    {
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x46546C67)
            throw new InvalidDataException("not a .glb (no glTF magic)");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != 2)
            throw new InvalidDataException("only glTF 2.0 .glb is supported");
        int at = 12;
        JsonDocument? json = null;
        byte[] bin = [];
        while (at + 8 <= data.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4));
            var chunk = data.AsSpan(at + 8, length);
            if (type == 0x4E4F534A) // JSON
                json = JsonDocument.Parse(Encoding.UTF8.GetString(chunk));
            else if (type == 0x004E4942) // BIN
                bin = chunk.ToArray();
            at += 8 + length;
        }
        return new Glb(json ?? throw new InvalidDataException(".glb has no JSON chunk"), bin);
    }

    public JsonElement Array(string name) => Root.TryGetProperty(name, out var a) ? a : default;

    public int Count(string name) => Root.TryGetProperty(name, out var a) ? a.GetArrayLength() : 0;

    /// <summary>Components per element for an accessor type.</summary>
    static int Width(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        "MAT4" => 16,
        _ => throw new InvalidDataException($"accessor type {type} not supported"),
    };

    /// <summary>Reads an accessor as floats (integers converted, normalized ones scaled to 0..1), element-major.</summary>
    public float[] Floats(int accessor, out int width)
    {
        var a = Root.GetProperty("accessors")[accessor];
        if (a.TryGetProperty("sparse", out _))
            throw new InvalidDataException("sparse accessors are not supported");
        int count = a.GetProperty("count").GetInt32();
        width = Width(a.GetProperty("type").GetString()!);
        int component = a.GetProperty("componentType").GetInt32();
        bool normalized = a.TryGetProperty("normalized", out var n) && n.GetBoolean();
        var result = new float[count * width];
        if (!a.TryGetProperty("bufferView", out var bvIndex))
            return result; // all zeros, per spec
        var bv = Root.GetProperty("bufferViews")[bvIndex.GetInt32()];
        int offset = (bv.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0) + (a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
        int size = component switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw new InvalidDataException($"component type {component}") };
        int stride = bv.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : size * width;
        var span = _bin.AsSpan();
        for (int i = 0; i < count; i++)
            for (int c = 0; c < width; c++)
            {
                int p = offset + i * stride + c * size;
                result[i * width + c] = component switch
                {
                    5126 => BinaryPrimitives.ReadSingleLittleEndian(span[p..]),
                    5121 => normalized ? span[p] / 255f : span[p],
                    5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(span[p..]) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(span[p..]),
                    5125 => BinaryPrimitives.ReadUInt32LittleEndian(span[p..]),
                    5120 => normalized ? Math.Max(-1f, (sbyte)span[p] / 127f) : (sbyte)span[p],
                    5122 => normalized ? Math.Max(-1f, BinaryPrimitives.ReadInt16LittleEndian(span[p..]) / 32767f) : BinaryPrimitives.ReadInt16LittleEndian(span[p..]),
                    _ => 0,
                };
            }
        return result;
    }

    /// <summary>Reads an integer accessor (indices, joints) exactly.</summary>
    public int[] Ints(int accessor)
    {
        var f = Floats(accessor, out _);
        var r = new int[f.Length];
        for (int i = 0; i < f.Length; i++)
            r[i] = (int)f[i];
        return r;
    }

    public static Matrix4x4 Mat4(float[] m, int at) => new(
        m[at], m[at + 1], m[at + 2], m[at + 3],
        m[at + 4], m[at + 5], m[at + 6], m[at + 7],
        m[at + 8], m[at + 9], m[at + 10], m[at + 11],
        m[at + 12], m[at + 13], m[at + 14], m[at + 15]);
}
