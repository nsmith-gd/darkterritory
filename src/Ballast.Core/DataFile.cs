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
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
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
        throw new DirectoryNotFoundException("Could not locate content/ (looked for Ballast.slnx in parent dirs)");
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
