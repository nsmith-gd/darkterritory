using System.Globalization;
using System.Text;

namespace Ballast;

/// <summary>A scalar in a JSON-with-comments document: where it is, what it is, and the comment above it.</summary>
/// <param name="Path">Dotted path, array indices in brackets: <c>chute.pourPerSecond</c>, <c>performance[2].brake</c>.</param>
public sealed record JsoncScalar(string Path, int Start, int Length, JsoncKind Kind, string Text, string? Comment);

public enum JsoncKind : byte { Number, String, Bool, Null }

/// <summary>
/// Edits values in a JSONC file without touching anything else. Tuning files carry their design rationale
/// and spec citations in comments; a serialise-and-write round trip would throw that away, so the editor
/// changes only the characters of the value it's told to.
/// </summary>
public static class Jsonc
{
    /// <summary>Every scalar in the document, in order.</summary>
    public static List<JsoncScalar> Scalars(string text)
    {
        var result = new List<JsoncScalar>();
        int i = 0;
        string? pending = null;
        Value(text, ref i, "", result, ref pending);
        return result;
    }

    /// <summary>Replaces one scalar's literal. Numbers are written invariant; strings are quoted and escaped.</summary>
    public static string Set(string text, string path, object value)
    {
        var target = Scalars(text).FirstOrDefault(s => s.Path == path) ?? throw new KeyNotFoundException($"no value at '{path}'");
        string literal = value switch
        {
            bool b => b ? "true" : "false",
            string s => System.Text.Json.JsonSerializer.Serialize(s),
            null => "null",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new ArgumentException($"can't write a {value.GetType().Name}"),
        };
        return string.Concat(text.AsSpan(0, target.Start), literal, text.AsSpan(target.Start + target.Length));
    }

    static void Skip(string t, ref int i, ref string? comment)
    {
        var sb = new StringBuilder();
        while (i < t.Length)
        {
            char c = t[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                // A blank line ends a comment's claim on what follows.
                if (c == '\n' && i + 1 < t.Length && t[i + 1] is '\n' or '\r')
                    sb.Clear();
                i++;
            }
            else if (c == '/' && i + 1 < t.Length && t[i + 1] == '/')
            {
                int end = t.IndexOf('\n', i);
                if (end < 0) end = t.Length;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t.AsSpan(i + 2, end - i - 2).Trim());
                i = end;
            }
            else if (c == '/' && i + 1 < t.Length && t[i + 1] == '*')
            {
                int end = t.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? t.Length : end + 2;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t.AsSpan(i + 2, Math.Max(0, end - i - 4)).Trim());
                i = end;
            }
            else
                break;
        }
        if (sb.Length > 0)
            comment = sb.ToString();
    }

    static void Value(string t, ref int i, string path, List<JsoncScalar> into, ref string? comment)
    {
        Skip(t, ref i, ref comment);
        if (i >= t.Length)
            return;
        char c = t[i];
        if (c == '{')
        {
            i++;
            comment = null;
            while (true)
            {
                string? keyComment = null;
                Skip(t, ref i, ref keyComment);
                if (i >= t.Length)
                    throw new FormatException("unterminated object");
                if (t[i] == '}')
                {
                    i++;
                    return;
                }
                string key = ReadString(t, ref i);
                Skip(t, ref i, ref keyComment);
                if (i >= t.Length || t[i] != ':')
                    throw new FormatException($"expected ':' after \"{key}\"");
                i++;
                string child = path.Length == 0 ? key : path + "." + key;
                Value(t, ref i, child, into, ref keyComment);
            }
        }
        if (c == '[')
        {
            i++;
            int index = 0;
            while (true)
            {
                string? itemComment = null;
                Skip(t, ref i, ref itemComment);
                if (i >= t.Length)
                    throw new FormatException("unterminated array");
                if (t[i] == ']')
                {
                    i++;
                    return;
                }
                Value(t, ref i, $"{path}[{index++}]", into, ref itemComment);
            }
        }
        int start = i;
        JsoncKind kind;
        if (c == '"')
        {
            ReadString(t, ref i);
            kind = JsoncKind.String;
        }
        else
        {
            while (i < t.Length && !char.IsWhiteSpace(t[i]) && t[i] is not (',' or '}' or ']' or '/'))
                i++;
            string word = t[start..i];
            kind = word switch { "true" or "false" => JsoncKind.Bool, "null" => JsoncKind.Null, _ => JsoncKind.Number };
            if (kind == JsoncKind.Number && !double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                throw new FormatException($"'{word}' at {start} isn't a value");
        }
        into.Add(new JsoncScalar(path, start, i - start, kind, t[start..i], comment));
        comment = null;
    }

    static string ReadString(string t, ref int i)
    {
        if (t[i] != '"')
            throw new FormatException($"expected a string at {i}");
        int start = i++;
        while (i < t.Length && t[i] != '"')
            i += t[i] == '\\' ? 2 : 1;
        i++;
        return System.Text.Json.JsonSerializer.Deserialize<string>(t[start..i])!;
    }
}
