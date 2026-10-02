using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ballast;

/// <summary>
/// Loads human- and agent-editable JSON data (tuning, prefabs, scenes). All engine data is
/// plain text so it can be diffed, reviewed and written by tools without an editor.
/// </summary>
public static class DataFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new Vector3Json(), new Double3Json() },
    };

    public static T Load<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"{path}: deserialized to null");

    public static void Save<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options) + "\n");

    /// <summary>Walks up from <paramref name="start"/> to find the repo's content directory.</summary>
    public static string FindContentRoot(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        for (; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "Ballast.slnx")))
                return candidate;
        }
        // A shipped build: content/ beside the executable, wherever it was started from.
        var shipped = Path.Combine(AppContext.BaseDirectory, "content");
        if (Directory.Exists(shipped))
            return shipped;
        throw new DirectoryNotFoundException("Could not locate content/ (looked for Ballast.slnx in parent dirs, and beside the executable)");
    }
}

/// <summary>
/// A data file that reloads itself when changed on disk, so designers (and agents) can
/// tweak numbers while the game runs. Poll <see cref="Refresh"/> once per frame.
/// </summary>
public sealed class HotData<T> where T : class
{
    public HotData(string path)
    {
        Path = path;
        _stamp = File.GetLastWriteTimeUtc(path);
        Value = DataFile.Load<T>(path);
    }

    public string Path { get; }
    public T Value { get; private set; }
    public event Action<T>? Reloaded;

    DateTime _stamp;

    /// <summary>Reloads if the file changed. A malformed edit keeps the previous value.</summary>
    public bool Refresh(Action<Exception>? onError = null)
    {
        var stamp = File.GetLastWriteTimeUtc(Path);
        if (stamp == _stamp)
            return false;
        _stamp = stamp;
        try
        {
            Value = DataFile.Load<T>(Path);
            Reloaded?.Invoke(Value);
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidDataException)
        {
            onError?.Invoke(e);
            return false;
        }
    }
}

/// <summary>A <see cref="System.Numerics.Vector3"/> as <c>[x, y, z]</c>: colours and tints in the look's data (look.json).</summary>
public sealed class Vector3Json : JsonConverter<System.Numerics.Vector3>
{
    public override System.Numerics.Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("a vector is [x, y, z]");
        Span<float> v = stackalloc float[3];
        int n = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (n >= 3)
                throw new JsonException("a vector is [x, y, z]");
            v[n++] = reader.GetSingle();
        }
        if (n != 3)
            throw new JsonException("a vector is [x, y, z]");
        return new System.Numerics.Vector3(v[0], v[1], v[2]);
    }

    public override void Write(Utf8JsonWriter writer, System.Numerics.Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}
