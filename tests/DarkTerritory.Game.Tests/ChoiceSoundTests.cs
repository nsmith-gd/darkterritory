using Ballast;
using Ballast.Audio;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The dead's and the run end's choices heard (GDD v1.4 App. D.11, D.12; ARCHITECTURE §8 note 218): the ballot's pick, its
/// cast and the host's lock, a bookmark this player took, a commendation given; each once, and none for what was already so.
/// </summary>
public class ChoiceSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    /// <summary>A session as far as the choices go: its world, its ballot and picker, its commendation pick.</summary>
    sealed class Choosing : IPlaySession
    {
        public World World { get; } = new(new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)),
            new RailLine(new LineDefinition("t", [new TrackSegment(5_000)])), 600));
        public TrainOnLine Train => World.Train;
        public (IReadOnlyList<EnemyKind> Options, EnemyKind? Cast)? Ballot { get; set; }
        public BallotPicker? Picker { get; } = new();
        public (string To, string What, bool Given)? CommendPick { get; set; }
        public int PlayerId => 3;
        public Sim.Route.Route? Route => null;
        public PlayerState Player => default;
        public TrainControls Controls => default;
        public long Tick => 0;
        public PlayerTuning PlayerTuning => throw new NotSupportedException();
        public string Status() => "";
        public void Step(in PlayerIntent intent) { }
        public IReadOnlyList<CarFrame> InterpolatedFrames(double alpha) => Train.Frames;
        public Camera EyeCamera(IReadOnlyList<CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) => throw new NotSupportedException();
        public IReadOnlyList<Crewmate> Crew(IReadOnlyList<CarFrame> frames, double alpha) => [];
    }

    static int Count(GameAudio audio, string name) => audio.Mixer.Voices.Count(v => v.Name == name);

    [Fact]
    public void TheBallotsPickCastAndLockAreEachHeardOnce()
    {
        var audio = new GameAudio(Content);
        var s = new Choosing { Ballot = ([EnemyKind.CinderHound, EnemyKind.Choir, EnemyKind.FireFlies], null) };
        audio.Choices(s);
        Assert.Empty(audio.Mixer.Voices);
        s.Picker!.Key(2, 3);
        audio.Choices(s);
        Assert.Equal(1, Count(audio, UiCue.Move));
        s.Picker.Key(2, 3);
        audio.Choices(s);
        Assert.Equal(1, Count(audio, UiCue.Select));
        Assert.Equal(0, Count(audio, UiCue.Vote));
        s.Ballot = (s.Ballot!.Value.Options, EnemyKind.Choir);
        audio.Choices(s);
        audio.Choices(s);
        Assert.Equal(1, Count(audio, UiCue.Vote));
        Assert.Equal(1, Count(audio, UiCue.Move));
    }

    [Fact]
    public void ABookmarkThisPlayerTookAndACommendationGivenAreHeard()
    {
        var audio = new GameAudio(Content);
        var s = new Choosing();
        // One already taken when this machine first looks: old news.
        s.World.Bookmarks.Mirror(new Bookmark(1, BookmarkKind.Manual, 10, -1, 1, -1, default, new Double3(0, 0, -1), Taker: 3));
        audio.Choices(s);
        Assert.Equal(0, Count(audio, UiCue.Bookmark));
        s.World.Bookmarks.Mirror(new Bookmark(2, BookmarkKind.Manual, 20, -1, 1, -1, default, new Double3(0, 0, -1), Taker: 3));
        // Someone else's, and one the host made itself: not this player's.
        s.World.Bookmarks.Mirror(new Bookmark(3, BookmarkKind.Manual, 21, -1, 1, -1, default, new Double3(0, 0, -1), Taker: 5));
        s.World.Bookmarks.Mirror(new Bookmark(4, BookmarkKind.Grab, 22, 1, 1, -1, default, new Double3(0, 0, -1)));
        audio.Choices(s);
        audio.Choices(s);
        Assert.Equal(1, Count(audio, UiCue.Bookmark));
        s.CommendPick = ("PRIYA", "STEADY HANDS", false);
        audio.Choices(s);
        Assert.Equal(0, Count(audio, UiCue.Commendation));
        s.CommendPick = ("PRIYA", "STEADY HANDS", true);
        audio.Choices(s);
        audio.Choices(s);
        Assert.Equal(1, Count(audio, UiCue.Commendation));
    }
}
