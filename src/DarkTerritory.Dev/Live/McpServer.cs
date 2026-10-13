using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkTerritory.Dev.Live;

/// <summary>Something a tool hands back: text, or an image (a shot, as PNG).</summary>
public abstract record ToolContent;
public sealed record ToolText(string Text) : ToolContent;
public sealed record ToolImage(byte[] Data, string MimeType = "image/png") : ToolContent;

/// <summary>
/// What a tool call comes to (MCP's CallToolResult): its content, and whether it failed. A failure is the tool's answer
/// (no night to step, a subject that isn't there), for the caller to read and act on; it isn't a protocol error.
/// </summary>
public sealed record ToolResult(IReadOnlyList<ToolContent> Content, bool IsError = false)
{
    public static ToolResult Text(string text) => new([new ToolText(text)]);
    public static ToolResult Failed(string message) => new([new ToolText(message)], IsError: true);
}

/// <summary>A tool: its name, what it does (the client's model reads it to choose), its arguments' JSON Schema, and the call.</summary>
public sealed record McpTool(string Name, string Description, JsonObject InputSchema, Func<ToolArgs, ToolResult> Call);

/// <summary>The caller's arguments were wrong (one missing, one of the wrong type, one the tool doesn't take): invalid params.</summary>
public sealed class BadArgumentsException(string message) : Exception(message);

/// <summary>
/// A tool's arguments, read against its schema: a name it doesn't list, or a value of the wrong type, is the caller's
/// mistake (<see cref="BadArgumentsException"/>, a JSON-RPC invalid-params error), said before the tool does anything.
/// </summary>
public sealed class ToolArgs
{
    readonly JsonObject _args;
    readonly string _tool;

    public ToolArgs(string tool, JsonObject args, JsonObject schema)
    {
        _tool = tool;
        _args = args;
        var known = schema["properties"] as JsonObject;
        foreach (var (name, _) in args)
            if (known is null || !known.ContainsKey(name))
                throw new BadArgumentsException($"{tool} takes no '{name}' (it takes {(known is { Count: > 0 } ? string.Join(", ", known.Select(k => k.Key)) : "nothing")})");
        if (schema["required"] is JsonArray required)
            foreach (var name in required.Select(r => r?.GetValue<string>()))
                if (name is not null && !args.ContainsKey(name))
                    throw new BadArgumentsException($"{tool} needs '{name}'");
    }

    public bool Has(string name) => _args[name] is not null;

    public string? String(string name) => _args[name] switch
    {
        null => null,
        JsonValue v when v.TryGetValue(out string? s) => s,
        _ => throw Wrong(name, "a string"),
    };

    public double? Number(string name) => _args[name] switch
    {
        null => null,
        JsonValue v when v.TryGetValue(out double d) && double.IsFinite(d) => d,
        _ => throw Wrong(name, "a number"),
    };

    public int? Int(string name, int min, int max) => Number(name) switch
    {
        null => null,
        double d when d == Math.Floor(d) && d >= min && d <= max => (int)d,
        _ => throw Wrong(name, $"a whole number from {min} to {max}"),
    };

    public bool? Bool(string name) => _args[name] switch
    {
        null => null,
        JsonValue v when v.TryGetValue(out bool b) => b,
        _ => throw Wrong(name, "true or false"),
    };

    public IReadOnlyList<string>? Strings(string name) => _args[name] switch
    {
        null => null,
        JsonArray a when a.All(e => e is JsonValue v && v.TryGetValue(out string? _)) => [.. a.Select(e => e!.GetValue<string>())],
        _ => throw Wrong(name, "an array of strings"),
    };

    /// <summary>A string or a number, as given (a tick, or minutes:seconds).</summary>
    public string? Text(string name) => _args[name] switch
    {
        null => null,
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonValue v when v.TryGetValue(out double d) => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw Wrong(name, "a string or a number"),
    };

    BadArgumentsException Wrong(string name, string what) => new($"{_tool}: '{name}' must be {what} (got {_args[name]?.ToJsonString()})");
}

/// <summary>
/// A Model Context Protocol server (ARCHITECTURE §8 note 524), by hand on System.Text.Json: JSON-RPC 2.0, one message a line
/// (MCP's stdio transport), the tools capability and nothing else. It answers <c>initialize</c>, <c>ping</c>,
/// <c>tools/list</c> and <c>tools/call</c>, takes the client's notifications without a word, and never dies of what it's
/// sent: a line that isn't JSON, an unknown method, an unknown tool or bad arguments are JSON-RPC errors, and whatever a
/// tool throws is that call's failed result. Calls are answered in turn, one at a time, as they come (a night is stepped on
/// this thread).
/// </summary>
public sealed class McpServer(string name, string version, IReadOnlyList<McpTool> tools, string? instructions = null)
{
    /// <summary>The protocol revisions it speaks, newest first: a client asking for one of them gets it, any other the newest.</summary>
    public static readonly string[] Versions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    public const int ParseError = -32700, InvalidRequest = -32600, MethodNotFound = -32601, InvalidParams = -32602, InternalError = -32603;

    static readonly JsonSerializerOptions Wire = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Where a tool's crash is told in full (stderr from <c>dt mcp</c>: stdout is the protocol's alone).</summary>
    public TextWriter? Log { get; init; }

    public IReadOnlyList<McpTool> Tools => tools;

    /// <summary>Reads messages a line at a time and answers each that wants an answer, until the input ends.</summary>
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken cancel = default)
    {
        while (await input.ReadLineAsync(cancel) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (Handle(line) is { } reply)
            {
                await output.WriteLineAsync(reply.AsMemory(), cancel);
                await output.FlushAsync(cancel);
            }
        }
    }

    /// <summary>One message in; its answer out, or null for a notification (and for a client's answer, which it never asks for).</summary>
    public string? Handle(string line)
    {
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException e)
        {
            return Error(null, ParseError, $"not JSON: {e.Message}");
        }
        if (message is not JsonObject request)
            return Error(null, InvalidRequest, message is JsonArray ? "batches aren't taken (MCP 2025-06-18 dropped them): one message a line" : "a message is a JSON object");
        bool notification = !request.ContainsKey("id");
        var id = request["id"]?.DeepClone();
        if (Str(request["method"]) is not { } method)
            return notification || request.ContainsKey("result") || request.ContainsKey("error") ? null : Error(id, InvalidRequest, "no method");
        if (notification)
            return null;
        if (Str(request["jsonrpc"]) != "2.0")
            return Error(id, InvalidRequest, "jsonrpc must be \"2.0\"");
        var parameters = request["params"] as JsonObject ?? [];
        return method switch
        {
            "initialize" => Result(id, Initialize(parameters)),
            "ping" => Result(id, []),
            "tools/list" => Result(id, new JsonObject { ["tools"] = new JsonArray([.. tools.Select(Describe)]) }),
            "tools/call" => Call(id, parameters),
            _ => Error(id, MethodNotFound, $"no method '{method}' (this server has tools only: initialize, ping, tools/list, tools/call)"),
        };
    }

    JsonObject Initialize(JsonObject parameters)
    {
        string asked = Str(parameters["protocolVersion"]) ?? Versions[0];
        var result = new JsonObject
        {
            ["protocolVersion"] = Versions.Contains(asked) ? asked : Versions[0],
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = name, ["version"] = version },
        };
        if (instructions is not null)
            result["instructions"] = instructions;
        return result;
    }

    static JsonNode Describe(McpTool tool) => new JsonObject
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["inputSchema"] = tool.InputSchema.DeepClone(),
    };

    string Call(JsonNode? id, JsonObject parameters)
    {
        if (Str(parameters["name"]) is not { } called)
            return Error(id, InvalidParams, "tools/call needs the tool's name");
        if (tools.FirstOrDefault(t => t.Name == called) is not { } tool)
            return Error(id, InvalidParams, $"no tool '{called}' (tools: {string.Join(", ", tools.Select(t => t.Name))})");
        var arguments = parameters["arguments"] switch
        {
            null => [],
            JsonObject o => o,
            _ => null,
        };
        if (arguments is null)
            return Error(id, InvalidParams, $"{called}: arguments must be an object");
        ToolResult result;
        try
        {
            result = tool.Call(new ToolArgs(called, arguments, tool.InputSchema));
        }
        catch (BadArgumentsException e)
        {
            return Error(id, InvalidParams, e.Message);
        }
        catch (Exception e)
        {
            // The tool's failure, not the server's: said in the result, where the caller's model reads it, and the server goes on.
            // One it means (no night, a subject that isn't there) in a line; anything else with where it came from.
            bool meant = e is InvalidOperationException or ArgumentException or IOException or FormatException;
            Log?.WriteLine($"dt mcp: {called} failed: {(meant ? e.Message : e.ToString())}");
            result = ToolResult.Failed(meant ? e.Message : $"{e.GetType().Name}: {e.Message}");
        }
        return Result(id, Encode(result));
    }

    static JsonObject Encode(ToolResult result) => new()
    {
        ["content"] = new JsonArray([.. result.Content.Select(c => (JsonNode)(c switch
        {
            ToolImage image => new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(image.Data), ["mimeType"] = image.MimeType },
            ToolText text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
            _ => throw new InvalidOperationException($"no encoding for {c.GetType().Name}"),
        }))]),
        ["isError"] = result.IsError,
    };

    static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    static string Result(JsonNode? id, JsonObject result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString(Wire);

    static string Error(JsonNode? id, int code, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString(Wire);
}
