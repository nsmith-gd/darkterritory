using System.Numerics;
using System.Runtime.CompilerServices;
using Ballast;
using Ballast.Assets;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The sourced props (content/art/models/props, cooked by tools/models from CC0 and CC-BY models: ARCHITECTURE §8 note
/// 55): each one a one-bone model whose bind pose is cooked once into a <see cref="MeshAsset"/> the scene places like
/// any kit piece, wearing its own layers (content/art/textures/models). A prop's sockets say where its light is, or
/// where it hangs from. Anything missing returns null, and the caller draws the kit's own piece instead.
/// </summary>
public sealed class PropArt
{
    public const string Folder = "art/models/props";

    static readonly ConditionalWeakTable<Look, PropArt> ByLook = new();

    /// <summary>The props for a look (one set per look, so the scene's pieces and the world's share them).</summary>
    public static PropArt Of(Look look) => ByLook.GetValue(look, l => new PropArt(l));

    readonly Dictionary<string, (MeshAsset Mesh, Model Model)?> _cache = new();

    public PropArt(Look look, string? contentRoot = null)
    {
        Look = look;
        contentRoot ??= look.TextureRoot is { } t ? Path.GetDirectoryName(Path.GetDirectoryName(t)) : null;
        ContentRoot = contentRoot ?? DataFile.FindContentRoot();
    }

    public Look Look { get; }
    public string ContentRoot { get; }

    /// <summary>Every prop there is, by name.</summary>
    public IEnumerable<string> Names =>
        Directory.Exists(Path.Combine(ContentRoot, Folder))
            ? Directory.EnumerateFiles(Path.Combine(ContentRoot, Folder), "*.glb").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()
            : [];

    /// <summary>The prop called <paramref name="name"/>, cooked, or null when it isn't built.</summary>
    public MeshAsset? Get(string name) => Load(name)?.Mesh;

    /// <summary>Where a prop's socket (a lamp's flame, the ring it hangs by) is, in the prop's frame; null if it has none.</summary>
    public Vector3? Socket(string name, string socket)
    {
        if (Load(name) is not { } p || p.Model.Skeleton.IndexOf(socket) < 0)
            return null;
        var pose = new Pose(p.Model.Skeleton.Count);
        new Skinner().Evaluate(p.Model, null, 0, false, pose);
        return Skinner.Socket(p.Model, pose, socket, Matrix4x4.Identity).Translation;
    }

    // (Locked: a cell of the line can be cooked on a worker while the frame's built, note 479.)
    (MeshAsset Mesh, Model Model)? Load(string name)
    {
        lock (_cache)
            return LoadLocked(name);
    }

    (MeshAsset Mesh, Model Model)? LoadLocked(string name)
    {
        if (_cache.TryGetValue(name, out var hit))
            return hit;
        (MeshAsset, Model)? made = null;
        var path = Path.Combine(ContentRoot, Folder, name + ".glb");
        if (File.Exists(path))
        {
            var model = ModelLoader.Load(path);
            var looks = model.Materials.Select(Resolve).ToArray();
            var pose = new Pose(model.Skeleton.Count);
            var skinner = new Skinner();
            skinner.Evaluate(model, null, 0, false, pose);
            var mesh = new MeshBuilder();
            skinner.Emit(mesh, model, pose, Matrix4x4.Identity, looks, new EmitSettings(0, null, Look.Tuning.TexelsPerMetre, default));
            made = (new MeshAsset($"prop-{name}", mesh.Vertices.ToArray()), model);
        }
        _cache[name] = made;
        return made;
    }

    /// <summary>A material's layer when the look has it (a prop's own, <c>&lt;prop&gt;_&lt;i&gt;</c>); its flat colour when not.</summary>
    MaterialLook Resolve(ModelMaterial m)
    {
        int layer = string.IsNullOrEmpty(m.Texture) ? -1 : Look.Layer(m.Texture);
        // The sourced textures carry their own grime and relief: the shader's weathering only breaks them up a little.
        const float Wear = 0.25f;
        return layer >= 0
            ? new MaterialLook(layer, Vector3.One, m.Emissive, m.Shine, Wear)
            : new MaterialLook(-1, m.BaseColour, Math.Max(m.Emissive, m.Glow), m.Shine, Wear);
    }
}
