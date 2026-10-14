using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Ballast.Dev;

/// <summary>
/// Whether a built game is fit for players (ARCHITECTURE §8 note 514): none of the developer tools are in it. A developer
/// assembly is found three ways, so no one slip lets it through: by its file, by an assembly that references it, and by
/// the mark every assembly built with the developer tools carries (<c>[assembly: AssemblyMetadata("DevTools", "true")]</c>,
/// which the dev assemblies and a dev build of the app put on themselves). The .deps.json files are read for the names too.
/// </summary>
public static class PlayerBuild
{
    /// <summary>The engine's and the game's developer assemblies: never in a player's build.</summary>
    public static readonly IReadOnlyList<string> DevAssemblies = ["Ballast.Dev", "DarkTerritory.Dev"];

    /// <summary>The mark a developer build puts on itself.</summary>
    public const string MarkKey = "DevTools";

    public sealed record Finding(string File, string Why);

    /// <param name="Assemblies">Managed assemblies read (native libraries are skipped).</param>
    /// <param name="Findings">Everything in the folder that's a developer tool, or that a developer build made.</param>
    public sealed record Result(string Folder, int Assemblies, IReadOnlyList<Finding> Findings)
    {
        public bool Clean => Findings.Count == 0;
    }

    public static Result Check(string folder)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"no build at {folder}");
        var findings = new List<Finding>();
        int assemblies = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string at = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (IsDev(Path.GetFileNameWithoutExtension(file)))
                findings.Add(new(at, "a developer assembly"));
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                continue;
            var md = pe.GetMetadataReader();
            if (!md.IsAssembly)
                continue;
            assemblies++;
            foreach (var handle in md.AssemblyReferences)
            {
                string name = md.GetString(md.GetAssemblyReference(handle).Name);
                if (IsDev(name))
                    findings.Add(new(at, $"references {name}"));
            }
            if (Marked(md))
                findings.Add(new(at, $"built with the developer tools (AssemblyMetadata \"{MarkKey}\")"));
        }
        foreach (var deps in Directory.EnumerateFiles(folder, "*.deps.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string text = File.ReadAllText(deps);
            foreach (var name in DevAssemblies)
                if (text.Contains($"\"{name}/", StringComparison.OrdinalIgnoreCase) || text.Contains($"{name}.dll", StringComparison.OrdinalIgnoreCase))
                    findings.Add(new(Path.GetRelativePath(folder, deps).Replace('\\', '/'), $"lists {name}"));
        }
        return new Result(Path.GetFullPath(folder), assemblies, findings);
    }

    /// <summary>Whether this assembly was built with the developer tools (its <see cref="MarkKey"/> mark).</summary>
    public static bool IsMarked(System.Reflection.Assembly assembly) =>
        assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>().Any(a => a.Key == MarkKey && a.Value == "true");

    static bool IsDev(string name) => DevAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase);

    static bool Marked(MetadataReader md)
    {
        foreach (var handle in md.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = md.GetCustomAttribute(handle);
            if (AttributeType(md, attribute) != "System.Reflection.AssemblyMetadataAttribute")
                continue;
            var value = attribute.DecodeValue(Strings.Provider);
            if (value.FixedArguments is [{ Value: string key }, { Value: string on }] && key == MarkKey && on == "true")
                return true;
        }
        return false;
    }

    static string? AttributeType(MetadataReader md, CustomAttribute attribute)
    {
        EntityHandle parent = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default,
        };
        return parent.Kind switch
        {
            HandleKind.TypeReference => md.GetTypeReference((TypeReferenceHandle)parent) is var t ? $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)}" : null,
            HandleKind.TypeDefinition => md.GetTypeDefinition((TypeDefinitionHandle)parent) is var d ? $"{md.GetString(d.Namespace)}.{md.GetString(d.Name)}" : null,
            _ => null,
        };
    }

    /// <summary>Just enough of a type provider to read an attribute's string arguments.</summary>
    sealed class Strings : ICustomAttributeTypeProvider<string>
    {
        public static readonly Strings Provider = new();
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == "System.Type";
    }
}
