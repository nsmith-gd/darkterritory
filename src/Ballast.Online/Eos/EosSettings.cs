using System.Text.Json;

namespace Ballast.Online.Eos;

/// <summary>
/// The game's EOS credentials, from the Epic Developer Portal (note 896): never in the repository. A build for players carries
/// them in <c>eos.json</c> beside the game, written by the Release workflow from its secrets; a developer sets the
/// <c>DT_EOS_*</c> variables instead (which win over the file). The client is the portal's "GameClient" policy, whose id and
/// secret Epic expects in every copy of the game: they say which game this is, and grant nothing beyond what a player may do.
/// </summary>
/// <param name="Relay">"allow" (straight to the peer when NAT allows, Epic's relays when it doesn't), "force" or "none".</param>
public sealed record EosSettings(string ProductId, string SandboxId, string DeploymentId, string ClientId, string ClientSecret, string Relay = "allow")
{
    public const string File = "eos.json";

    /// <summary>The five ids the portal gives, all there.</summary>
    public bool Complete => new[] { ProductId, SandboxId, DeploymentId, ClientId, ClientSecret }.All(s => !string.IsNullOrWhiteSpace(s));

    public EosRelay RelayControl => Relay.Trim().ToLowerInvariant() switch
    {
        "force" => EosRelay.ForceRelays,
        "none" => EosRelay.NoRelays,
        _ => EosRelay.AllowRelays,
    };

    /// <summary>
    /// The settings from <c>eos.json</c> in <paramref name="directory"/> (the game's own folder by default), with any
    /// <c>DT_EOS_PRODUCT_ID</c>, <c>DT_EOS_SANDBOX_ID</c>, <c>DT_EOS_DEPLOYMENT_ID</c>, <c>DT_EOS_CLIENT_ID</c>,
    /// <c>DT_EOS_CLIENT_SECRET</c> and <c>DT_EOS_RELAY</c> over them; null when neither says anything.
    /// </summary>
    public static EosSettings? Load(string? directory = null, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        string path = Path.Combine(directory ?? AppContext.BaseDirectory, File);
        var file = System.IO.File.Exists(path)
            ? JsonSerializer.Deserialize<EosSettings>(System.IO.File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            })
            : null;
        string Pick(string name, string? fromFile) => env($"DT_EOS_{name}") is { Length: > 0 } v ? v : fromFile ?? "";
        var s = new EosSettings(Pick("PRODUCT_ID", file?.ProductId), Pick("SANDBOX_ID", file?.SandboxId), Pick("DEPLOYMENT_ID", file?.DeploymentId),
            Pick("CLIENT_ID", file?.ClientId), Pick("CLIENT_SECRET", file?.ClientSecret), Pick("RELAY", file?.Relay is { Length: > 0 } r ? r : "allow"));
        return file is null && !new[] { s.ProductId, s.SandboxId, s.DeploymentId, s.ClientId, s.ClientSecret }.Any(v => v.Length > 0) ? null : s;
    }
}

/// <summary>Starting the real EOS SDK, when this build has it (external/eos/README.md).</summary>
public static class EosSdk
{
    /// <summary>Whether Epic's SDK was compiled in (external/eos/ held it when this was built).</summary>
#if EOS_SDK
    public const bool Available = true;
#else
    public const bool Available = false;
#endif

    /// <summary>The EOS SDK, initialised with these credentials, or why not.</summary>
    public static IEosSdk? TryCreate(EosSettings settings, out string? error)
    {
        if (!settings.Complete)
        {
            error = "the game's EOS credentials are incomplete (eos.json, or the DT_EOS_* variables; note 896)";
            return null;
        }
#if EOS_SDK
        return EpicSdk.TryCreate(settings, out error);
#else
        error = "this build has no EOS SDK in it (external/eos/README.md); LAN and direct IP still work";
        return null;
#endif
    }
}
