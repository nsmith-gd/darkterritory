namespace Ballast.Online;

/// <summary>
/// A platform whose lobbies friends reach by a short code typed in, rather than through a friends list and an overlay
/// (EOS without Epic accounts, note 896): the host reads its code out, the friend types it on the join screen.
/// </summary>
public interface IJoinCodes
{
    /// <summary>The code for a lobby we host or are in ("" when it has none yet).</summary>
    string CodeOf(LobbyId lobby);

    /// <summary>
    /// Finds the lobby with this code and joins it: <see cref="OnlineEventKind.LobbyEntered"/> (with the lobby's id) or
    /// <see cref="OnlineEventKind.LobbyFailed"/> on a later poll.
    /// </summary>
    void JoinByCode(string code);
}

/// <summary>What a join code looks like: six of 32 letters and digits that can't be misread (no 0, O, 1 or I), shown "K7Q-M2X".</summary>
public static class JoinCode
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    /// <summary>The code as stored and searched for: upper case, without the dash or spaces; null if it isn't one.</summary>
    public static string? Normalize(string? typed)
    {
        if (typed is null)
            return null;
        var s = new string([.. typed.Where(c => c is not ('-' or ' ')).Select(char.ToUpperInvariant)]);
        return s.Length == Length && s.All(c => Alphabet.Contains(c)) ? s : null;
    }

    /// <summary>The code made from a lobby's own id (FNV-1a), so the host needs no randomness to make one.</summary>
    public static string From(string lobbyId)
    {
        ulong h = Fnv(lobbyId);
        var c = new char[Length];
        for (int i = 0; i < Length; i++, h >>= 5)
            c[i] = Alphabet[(int)(h & 31)];
        return new string(c);
    }

    /// <summary>"K7QM2X" as "K7Q-M2X", for reading out.</summary>
    public static string Show(string code) => code.Length == Length ? $"{code[..3]}-{code[3..]}" : code;

    /// <summary>FNV-1a, 64-bit: a stable hash of an id the platform gave us as text.</summary>
    public static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037;
        foreach (char ch in s)
        {
            h ^= ch;
            h *= 1099511628211;
        }
        return h;
    }
}
