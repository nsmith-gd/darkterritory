using Ballast.Online;

namespace DarkTerritory.Game;

/// <summary>
/// Where the app goes once a night's session is over and disposed (ARCHITECTURE §8 note 937). The director's playtest of
/// 20 Oct evening, line 27: "So I escape and leave, and I've been put into automatically, like, another lobby ... you
/// can't actually leave a game". LEAVE on a campaign night opened the fortress board (CARRY ON, CREW: YOU AND 1 BOT,
/// PLAY: HOST FOR FRIENDS), which reads as a lobby to sit in. A night left on purpose now goes to the title. Nothing is
/// joined or hosted, and no crew is followed. The night itself is kept as the campaign keeps it: autosaved at the last
/// stop, CARRY ON on its slot. A night seen to its end (Enter on its end screen) still goes on to the fortress, or the
/// crew's next night, as before.
/// </summary>
public abstract record AfterNight
{
    /// <summary>An invite accepted while playing (queue #272): its lobby is the next launch.</summary>
    public sealed record Relaunch(LobbyId Lobby) : AfterNight;

    /// <summary>The blacksmith's refit (note 687): the same night again from its yard, fitted.</summary>
    public sealed record Refit : AfterNight;

    /// <summary>A joiner whose host went on from the town (note 606): after it, to its next night.</summary>
    public sealed record Follow(NetPlaySession.Following Going) : AfterNight;

    /// <summary>Started from the command line: done when the night is.</summary>
    public sealed record Quit : AfterNight;

    /// <summary>Back to the front end, on <paramref name="Screen"/>.</summary>
    public sealed record Menu(MenuAfter Screen) : AfterNight;

    /// <param name="left">The player chose LEAVE (or END THE NIGHT): they're done with this game, whatever it was.</param>
    /// <param name="kicked">The crew put this player off (note 724).</param>
    /// <param name="invited">An invite accepted during the night: they asked to go there, so it stands even after a LEAVE.</param>
    /// <param name="refitting">The blacksmith's fitted something (note 697), and the night went to fit it.</param>
    /// <param name="following">This joiner's host went on to its next night (note 606).</param>
    /// <param name="campaign">The night was a campaign night this machine hosted (or a crew ridden with, hosted from its copy).</param>
    public static AfterNight Decide(bool left, bool kicked, LobbyId? invited, bool refitting, NetPlaySession.Following? following,
        bool fromCommandLine, bool campaign, bool crew)
    {
        if (invited is { } lobby)
            return new Relaunch(lobby);
        // Left, nothing of this game carries on: no refit, no following the host, no fortress board to start the next.
        if (!left && refitting)
            return new Refit();
        if (!left && following is { } going)
            return new Follow(going);
        if (fromCommandLine)
            return new Quit();
        return new Menu(kicked ? MenuAfter.PutOff : left ? MenuAfter.Title : crew ? MenuAfter.Crew : campaign ? MenuAfter.Fortress : MenuAfter.Title);
    }
}

/// <summary>Which of the front end's screens a night comes back to.</summary>
public enum MenuAfter { Title, Fortress, Crew, PutOff }
