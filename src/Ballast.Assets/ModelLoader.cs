using System.Numerics;
using System.Text.Json;

namespace Ballast.Assets;

/// <summary>
/// Reads a .glb into a <see cref="Model"/>: every mesh node skinned to the one skin (or, unskinned under a bone, bound
/// rigidly to it), the skin's joints as the skeleton, and each animation resampled to <see cref="Fps"/> frames.
/// Deterministic: the same file gives the same model, bit for bit.
/// </summary>
public static class ModelLoader
{
    public const float Fps = 30;

    public static Model Load(string path) =>
        Load(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));

    public static Model Load(byte[] glb, string name)
    {
        var g = Glb.Read(glb);
        var nodes = g.Array("nodes");
        int nodeCount = g.Count("nodes");
        var parentOf = Enumerable.Repeat(-1, nodeCount).ToArray();
        for (int i = 0; i < nodeCount; i++)
            if (nodes[i].TryGetProperty("children", out var ch))
                foreach (var c in ch.EnumerateArray())
                    parentOf[c.GetInt32()] = i;
        var local = new Matrix4x4[nodeCount];
        var trs = new (Vector3 T, Quaternion R, Vector3 S)[nodeCount];
        for (int i = 0; i < nodeCount; i++)
            (local[i], trs[i]) = NodeTransform(nodes[i]);
        Matrix4x4 World(int n)
        {
            var m = Matrix4x4.Identity;
            for (; n >= 0; n = parentOf[n])
                m *= local[n];
            return m;
        }

        // The skeleton: the (first) skin's joints, ordered parents first.
        if (g.Count("skins") == 0)
            throw new InvalidDataException($"{name}: no skin (every model is skinned, even the props on it)");
        var skin = g.Array("skins")[0];
        var jointNodes = skin.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
        var ibmRaw = skin.TryGetProperty("inverseBindMatrices", out var ibmAcc) ? g.Floats(ibmAcc.GetInt32(), out _) : null;
        int Depth(int n)
        {
            int d = 0;
            for (; parentOf[n] >= 0; n = parentOf[n])
                d++;
            return d;
        }
        var order = Enumerable.Range(0, jointNodes.Length).OrderBy(j => Depth(jointNodes[j])).ThenBy(j => j).ToArray();
        var boneOfNode = Enumerable.Repeat(-1, nodeCount).ToArray();
        for (int b = 0; b < order.Length; b++)
            boneOfNode[jointNodes[order[b]]] = b;
        int boneCount = order.Length;
        var names = new string[boneCount];
        var parents = new int[boneCount];
        var outer = new Matrix4x4[boneCount];
        var ibm = new Matrix4x4[boneCount];
        var restT = new Vector3[boneCount];
        var restR = new Quaternion[boneCount];
        var restS = new Vector3[boneCount];
        for (int b = 0; b < boneCount; b++)
        {
            int j = order[b], node = jointNodes[j];
            names[b] = nodes[node].TryGetProperty("name", out var nm) ? nm.GetString()! : $"bone{b}";
            // Up to the nearest joint above, collecting any plain nodes in between.
            var above = Matrix4x4.Identity;
            int p = parentOf[node];
            while (p >= 0 && boneOfNode[p] < 0)
            {
                above *= local[p];
                p = parentOf[p];
            }
            parents[b] = p >= 0 ? boneOfNode[p] : -1;
            outer[b] = above;
            ibm[b] = ibmRaw is null ? Invert(World(node)) : Glb.Mat4(ibmRaw, j * 16);
            (restT[b], restR[b], restS[b]) = trs[node];
        }

        var materials = ReadMaterials(g);
        var parts = new List<MeshPart>();
        var weighted = new bool[boneCount];
        int maxVariant = 0;
        for (int n = 0; n < nodeCount; n++)
        {
            var node = nodes[n];
            if (!node.TryGetProperty("mesh", out var meshIndex))
                continue;
            string nodeName = node.TryGetProperty("name", out var nn) ? nn.GetString()! : $"mesh{n}";
            ulong variants = ulong.MaxValue;
            string? clip = null;
            if (node.TryGetProperty("extras", out var extras))
            {
                if (extras.TryGetProperty("variants", out var v) && v.GetString() is { Length: > 0 } list)
                {
                    variants = 0;
                    foreach (var s in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        int k = int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                        variants |= 1UL << k;
                        maxVariant = Math.Max(maxVariant, k);
                    }
                }
                if (extras.TryGetProperty("clip", out var c))
                    clip = c.GetString();
            }
            bool skinned = node.TryGetProperty("skin", out _);
            // Unskinned under a bone: bound rigidly to it, positions taken to model space at bind.
            int rigidBone = -1;
            var rigidXf = Matrix4x4.Identity;
            if (!skinned)
            {
                for (int p = parentOf[n]; p >= 0 && rigidBone < 0; p = parentOf[p])
                    rigidBone = boneOfNode[p];
                if (rigidBone < 0)
                    throw new InvalidDataException($"{name}: mesh '{nodeName}' is neither skinned nor under a bone");
                rigidXf = World(n);
            }
            var mesh = g.Array("meshes")[meshIndex.GetInt32()];
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                if (prim.TryGetProperty("mode", out var mode) && mode.GetInt32() != 4)
                    continue; // triangles only
                var attrs = prim.GetProperty("attributes");
                var pos = g.Floats(attrs.GetProperty("POSITION").GetInt32(), out _);
                int count = pos.Length / 3;
                var nor = attrs.TryGetProperty("NORMAL", out var na) ? g.Floats(na.GetInt32(), out _) : new float[count * 3];
                var uv = attrs.TryGetProperty("TEXCOORD_0", out var ta) ? g.Floats(ta.GetInt32(), out _) : new float[count * 2];
                var joints = new int[count * 4];
                var weights = new float[count * 4];
                if (skinned && attrs.TryGetProperty("JOINTS_0", out var ja) && attrs.TryGetProperty("WEIGHTS_0", out var wa))
                {
                    var jr = g.Ints(ja.GetInt32());
                    var wr = g.Floats(wa.GetInt32(), out _);
                    for (int i = 0; i < count; i++)
                    {
                        float sum = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int bone = boneOfNode[jointNodes[jr[i * 4 + k]]];
                            float w = wr[i * 4 + k];
                            joints[i * 4 + k] = w > 0 ? bone : 0;
                            weights[i * 4 + k] = w;
                            sum += w;
                            if (w > 0)
                                weighted[bone] = true;
                        }
                        if (sum <= 0)
                            throw new InvalidDataException($"{name}: vertex {i} of '{nodeName}' has no weight");
                        for (int k = 0; k < 4; k++)
                            weights[i * 4 + k] /= sum;
                    }
                }
                else
                {
                    int bone = Math.Max(rigidBone, 0);
                    for (int i = 0; i < count; i++)
                    {
                        joints[i * 4] = bone;
                        weights[i * 4] = 1;
                    }
                    weighted[bone] = true;
                }
                var positions = new Vector3[count];
                var normals = new Vector3[count];
                var uvs = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    positions[i] = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                    normals[i] = new Vector3(nor[i * 3], nor[i * 3 + 1], nor[i * 3 + 2]);
                    if (!skinned)
                    {
                        positions[i] = Vector3.Transform(positions[i], rigidXf);
                        normals[i] = Vector3.TransformNormal(normals[i], rigidXf);
                    }
                    normals[i] = normals[i].LengthSquared() > 0 ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
                    uvs[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
                }
                var indices = prim.TryGetProperty("indices", out var ia) ? g.Ints(ia.GetInt32()) : Enumerable.Range(0, count).ToArray();
                int material = prim.TryGetProperty("material", out var ma) ? ma.GetInt32() : -1;
                if (material < 0)
                {
                    material = materials.Count;
                    materials.Add(new ModelMaterial("default", "", new Vector3(0.5f), 0, 0, 0, Vector3.One));
                }
                parts.Add(new MeshPart
                {
                    Name = nodeName,
                    Material = material,
                    Positions = positions,
                    Normals = normals,
                    Uvs = uvs,
                    Joints = joints,
                    Weights = weights,
                    Indices = indices,
                    Variants = variants,
                    Clip = clip,
                });
            }
        }

        var skeleton = new Skeleton
        {
            Names = names,
            Parents = parents,
            RestTranslation = restT,
            RestRotation = restR,
            RestScale = restS,
            InverseBind = ibm,
            Outer = outer,
            Socket = [.. weighted.Select(w => !w)],
        };
        var clips = ReadClips(g, skeleton, boneOfNode);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in parts)
            foreach (var v in p.Positions)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        return new Model
        {
            Name = name,
            Parts = [.. parts],
            Materials = [.. materials],
            Skeleton = skeleton,
            Clips = clips.ToDictionary(c => c.Name),
            ClipNames = [.. clips.Select(c => c.Name)],
            VariantCount = maxVariant + 1,
            Min = min,
            Max = max,
        };
    }

    /// <summary>
    /// <paramref name="model"/> with the clips of <paramref name="extra"/> added (a companion <c>name_clips.glb</c>: clips
    /// authored on the same skeleton without re-baking the model's mesh). Bones are matched by name; a bone the extra
    /// file doesn't have holds the model's rest. A clip already in the model keeps the model's.
    /// </summary>
    public static Model WithClips(Model model, Model extra)
    {
        var target = model.Skeleton;
        int bones = target.Count;
        var from = new int[bones];
        for (int b = 0; b < bones; b++)
            from[b] = extra.Skeleton.IndexOf(target.Names[b]);
        var clips = new Dictionary<string, AnimationClip>(model.Clips);
        var names = new List<string>(model.ClipNames);
        foreach (var name in extra.ClipNames)
        {
            if (clips.ContainsKey(name))
                continue;
            var c = extra.Clips[name];
            var T = new Vector3[c.Frames * bones];
            var R = new Quaternion[c.Frames * bones];
            var S = new Vector3[c.Frames * bones];
            for (int f = 0; f < c.Frames; f++)
                for (int b = 0; b < bones; b++)
                {
                    int at = f * bones + b, src = from[b];
                    (T[at], R[at], S[at]) = src < 0
                        ? (target.RestTranslation[b], target.RestRotation[b], target.RestScale[b])
                        : (c.Translation[f * c.Bones + src], c.Rotation[f * c.Bones + src], c.Scale[f * c.Bones + src]);
                }
            clips[name] = new AnimationClip { Name = name, Frames = c.Frames, Fps = c.Fps, Bones = bones, Translation = T, Rotation = R, Scale = S };
            names.Add(name);
        }
        return new Model
        {
            Name = model.Name,
            Parts = model.Parts,
            Materials = model.Materials,
            Skeleton = model.Skeleton,
            Clips = clips,
            ClipNames = [.. names],
            VariantCount = model.VariantCount,
            Min = model.Min,
            Max = model.Max,
        };
    }

    static (Matrix4x4, (Vector3, Quaternion, Vector3)) NodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var m))
        {
            var f = m.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            var mat = Glb.Mat4(f, 0);
            Matrix4x4.Decompose(mat, out var s, out var r, out var t);
            return (mat, (t, r, s));
        }
        var tr = node.TryGetProperty("translation", out var te) ? V3(te) : Vector3.Zero;
        var ro = node.TryGetProperty("rotation", out var re) ? Q(re) : Quaternion.Identity;
        var sc = node.TryGetProperty("scale", out var se) ? V3(se) : Vector3.One;
        return (Compose(tr, ro, sc), (tr, ro, sc));
    }

    public static Matrix4x4 Compose(Vector3 t, Quaternion r, Vector3 s) =>
        Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);

    static Vector3 V3(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
    static Quaternion Q(JsonElement a) => Quaternion.Normalize(new Quaternion(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle(), a[3].GetSingle()));

    static Matrix4x4 Invert(Matrix4x4 m) => Matrix4x4.Invert(m, out var inv) ? inv : Matrix4x4.Identity;

    static List<ModelMaterial> ReadMaterials(Glb g)
    {
        var list = new List<ModelMaterial>();
        for (int i = 0; i < g.Count("materials"); i++)
        {
            var m = g.Array("materials")[i];
            string name = m.TryGetProperty("name", out var n) ? n.GetString()! : $"material{i}";
            var colour = new Vector3(0.5f);
            if (m.TryGetProperty("pbrMetallicRoughness", out var pbr) && pbr.TryGetProperty("baseColorFactor", out var bc))
                colour = new Vector3(bc[0].GetSingle(), bc[1].GetSingle(), bc[2].GetSingle());
            float shine = 0.1f, emissive = 0, glow = 0;
            var tint = Vector3.One;
            if (m.TryGetProperty("extras", out var x))
            {
                if (x.TryGetProperty("dt_tint", out var ti) && ti.ValueKind == JsonValueKind.Array && ti.GetArrayLength() >= 3)
                    tint = new Vector3(ti[0].GetSingle(), ti[1].GetSingle(), ti[2].GetSingle());
                if (x.TryGetProperty("dt_shine", out var s))
                    shine = s.GetSingle();
                if (x.TryGetProperty("dt_emissive", out var e))
                    emissive = e.GetSingle();
                if (x.TryGetProperty("dt_glow", out var gl))
                    glow = gl.GetSingle();
            }
            int dot = name.IndexOf('.');
            list.Add(new ModelMaterial(name, dot < 0 ? name : name[..dot], colour, shine, emissive, glow, tint));
        }
        return list;
    }

    /// <summary>Each animation sampled at every 1/30 s from 0 to its last key; bones it doesn't touch hold their rest.</summary>
    static List<AnimationClip> ReadClips(Glb g, Skeleton sk, int[] boneOfNode)
    {
        var clips = new List<AnimationClip>();
        int bones = sk.Count;
        for (int a = 0; a < g.Count("animations"); a++)
        {
            var anim = g.Array("animations")[a];
            string name = anim.TryGetProperty("name", out var n) ? n.GetString()! : $"clip{a}";
            var samplers = anim.GetProperty("samplers");
            var channels = new List<(int Bone, string Path, float[] Times, float[] Values, int Width, string Interp)>();
            float end = 0;
            foreach (var ch in anim.GetProperty("channels").EnumerateArray())
            {
                var target = ch.GetProperty("target");
                if (!target.TryGetProperty("node", out var tn))
                    continue;
                int bone = boneOfNode[tn.GetInt32()];
                if (bone < 0)
                    continue;
                var s = samplers[ch.GetProperty("sampler").GetInt32()];
                var times = g.Floats(s.GetProperty("input").GetInt32(), out _);
                var values = g.Floats(s.GetProperty("output").GetInt32(), out int width);
                string interp = s.TryGetProperty("interpolation", out var ip) ? ip.GetString()! : "LINEAR";
                string path = target.GetProperty("path").GetString()!;
                if (path is not ("translation" or "rotation" or "scale"))
                    continue;
                channels.Add((bone, path, times, values, width, interp));
                if (times.Length > 0)
                    end = Math.Max(end, times[^1]);
            }
            int frames = (int)Math.Round(end * Fps) + 1;
            var T = new Vector3[frames * bones];
            var R = new Quaternion[frames * bones];
            var S = new Vector3[frames * bones];
            for (int f = 0; f < frames; f++)
                for (int b = 0; b < bones; b++)
                {
                    T[f * bones + b] = sk.RestTranslation[b];
                    R[f * bones + b] = sk.RestRotation[b];
                    S[f * bones + b] = sk.RestScale[b];
                }
            foreach (var (bone, path, times, values, width, interp) in channels)
                for (int f = 0; f < frames; f++)
                {
                    float t = f / Fps;
                    var v = Sample(times, values, width, interp, t, path == "rotation");
                    int at = f * bones + bone;
                    if (path == "translation")
                        T[at] = new Vector3(v.X, v.Y, v.Z);
                    else if (path == "scale")
                        S[at] = new Vector3(v.X, v.Y, v.Z);
                    else
                        R[at] = Quaternion.Normalize(new Quaternion(v.X, v.Y, v.Z, v.W));
                }
            // Keep each bone's rotations in one hemisphere frame to frame, so blending between frames takes the short way.
            for (int b = 0; b < bones; b++)
                for (int f = 1; f < frames; f++)
                    if (Quaternion.Dot(R[(f - 1) * bones + b], R[f * bones + b]) < 0)
                        R[f * bones + b] = Quaternion.Negate(R[f * bones + b]);
            clips.Add(new AnimationClip { Name = name, Frames = frames, Fps = Fps, Bones = bones, Translation = T, Rotation = R, Scale = S });
        }
        return clips;
    }

    static Vector4 Sample(float[] times, float[] values, int width, string interp, float t, bool rotation)
    {
        int n = times.Length;
        bool cubic = interp == "CUBICSPLINE";
        Vector4 At(int k)
        {
            // Cubic spline outputs are (in-tangent, value, out-tangent) triples: take the value.
            int i = (cubic ? k * 3 + 1 : k) * width;
            return new Vector4(values[i], values[i + 1], values[i + 2], width > 3 ? values[i + 3] : 0);
        }
        if (n == 0)
            return Vector4.Zero;
        if (t <= times[0] + 1e-6f)
            return At(0);
        if (t >= times[n - 1] - 1e-6f)
            return At(n - 1);
        int hi = System.Array.BinarySearch(times, t);
        if (hi >= 0)
            return At(hi);
        hi = ~hi;
        int lo = hi - 1;
        if (interp == "STEP")
            return At(lo);
        float u = (t - times[lo]) / Math.Max(times[hi] - times[lo], 1e-9f);
        var a = At(lo);
        var b = At(hi);
        if (rotation)
        {
            var q = Quaternion.Slerp(new Quaternion(a.X, a.Y, a.Z, a.W), new Quaternion(b.X, b.Y, b.Z, b.W), u);
            return new Vector4(q.X, q.Y, q.Z, q.W);
        }
        return Vector4.Lerp(a, b, u);
    }
}
