using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The flare pistol heard (queue #675, note 975; GameAudio.Flare.cs; n1-w3's note 871): the shot on the shooter's own Fire,
/// once, and where the flare first flies for everyone else; the flare burning held on it for its minute, coming in as the
/// shot's hiss of it dies; a dry click on Fire with none left. Each cue installed as a short tone (the burn a loop) and
/// counted off the mixer.
/// </summary>
public class FlareSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly Sim.Combat.CombatTuning C = DataFile.Load<Sim.Combat.CombatTuning>(Path.Combine(Content, Sim.Combat.CombatTuning.File));
    static readonly string[] Cues = ["up-flare.shot", "up-flare.burn", "up-flare.click"];
    static PlayerIntent Fire => new() { Buttons = PlayerButtons.Fire };
    const double Dt = SimConstants.TickSeconds;

    sealed class Bench
    {
        public readonly World World;
        public readonly GameAudio Audio = new(Content);
        public readonly Dictionary<int, PlayerState> Crew = new();
        public readonly List<(string Name, Double3 At)> Heard = [];
        readonly HashSet<int> _seen = [];

        public Bench(bool host = true)
        {
            var train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
            World = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(train, 4, 1)), line, 5_000), C);
            // The host steps the bodies (World.FireFlare spawns the flare); a client mirrors the host's record.
            if (host)
                World.EnableBodies();
            else
                World.EnableEnemies(DataFile.Load<Sim.Enemies.EnemyTuning>(Path.Combine(Content, Sim.Enemies.EnemyTuning.File)), null, 1, 1, authority: false);
            Audio.Bank.Add("up-flare.shot", new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Duration: 0.1, MaxInstances: 64));
            Audio.Bank.Add("up-flare.click", new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 880)], Duration: 0.05, MaxInstances: 64));
            Audio.Bank.Add("up-flare.burn", new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 220)], Loop: true, MaxInstances: 8));
            Audio.PlayerTuning = P;
            Audio.OwnId = 1;
        }

        public TrainOnLine Train => World.Train;

        /// <summary>Crewmate <paramref name="id"/> on the ground beside car 2, out to its right, looking out and up a little.</summary>
        public void Stand(int id, double along)
        {
            var at = Train.Frames[2].ToWorld(new Double3(3, 0, along));
            var right = Train.Frames[2].Right;
            var s = PlayerMotor.SpawnOnGround(at, Train.Line, Train.Dynamics.Distance, P);
            Crew[id] = s with { Yaw = Math.Atan2(-right.X, -right.Z), Pitch = 20 * Math.PI / 180 };
        }

        public Double3 Hand(int id) => PlayerMotor.WorldPosition(Crew[id], Train) + Double3.Up * 1.1;

        /// <summary>A loaded flare pistol in crewmate <paramref name="id"/>'s hand.</summary>
        public Body Pistol(int id, int flares)
        {
            var pistol = World.Bodies.SpawnItem(Hand(id), Train.Dynamics.Distance, BodyKind.FlarePistol);
            pistol.Charge = flares;
            pistol.Carrier = id;
            return pistol;
        }

        public void Step(Func<int, PlayerIntent>? intent = null)
        {
            World.BeginTick();
            foreach (var id in Crew.Keys.ToList())
            {
                var s = Crew[id];
                World.CrewAct(ref s, intent?.Invoke(id) ?? default, id);
                Crew[id] = s;
            }
            World.Step(default);
            World.StepBodies([.. Crew.Select(c => (c.Key, c.Value))]);
            Audio.CrewStates = [.. Crew.Select(c => (c.Key, c.Value))];
            Audio.OwnIntent = intent?.Invoke(1) ?? default;
            Audio.Update(World, default, Listener.At(Train.Frames[2].Origin + Double3.Up * 3, 0), exposed: true, Dt);
            foreach (var v in Audio.Mixer.Voices)
                if (_seen.Add(v.Id) && Cues.Contains(v.Name))
                    Heard.Add((v.Name, v.Position));
        }

        public void Run(double seconds, Func<int, PlayerIntent>? intent = null)
        {
            for (int i = 0; i < seconds / Dt; i++)
                Step(intent);
        }

        public int Count(string name) => Heard.Count(h => h.Name == name);

        public SoundInstance[] Burning => [.. Audio.Mixer.Voices.Where(v => v.Name == "up-flare.burn" && !v.Stopped && !v.Finished)];
    }

    [Fact]
    public void TheShootersOwnShotIsHeardOnceOnFireItsFlareBurnsItsMinuteAndAnEmptyPistolClicks()
    {
        var b = new Bench();
        b.Stand(1, 0);
        var pistol = b.Pistol(1, 2);
        b.Run(0.2);
        // One press, held: one shot, at the hand, the moment it's pressed; the host's flare of it isn't heard again.
        b.Run(0.5, _ => Fire);
        Assert.Equal(1, b.Count("up-flare.shot"));
        Assert.Equal(1, pistol.Charge);
        Assert.True((b.Heard.Single(h => h.Name == "up-flare.shot").At - b.Hand(1)).Length < 1.5);
        // Pressed again within flarePistol.every: nothing, as the pistol does nothing.
        b.Run(0.1);
        b.Run(0.1, _ => Fire);
        Assert.Equal(1, b.Count("up-flare.shot"));
        Assert.Equal(0, b.Count("up-flare.click"));
        // The burn comes in as the shot's hiss of the flare dies, and is full a couple of seconds on, on the flare.
        var flare = Assert.Single(b.World.Bodies.All, x => x.Kind == BodyKind.Flare);
        b.Run(2.5);
        var burn = Assert.Single(b.Burning);
        Assert.Equal(1f, burn.Volume, 3);
        Assert.True((burn.Position - Bodies.WorldCentre(flare, b.Train)).Length < 1e-6);
        // The second flare: a second shot, and two burning.
        b.Run(0.2, _ => Fire);
        Assert.Equal(2, b.Count("up-flare.shot"));
        Assert.Equal(0, pistol.Charge);
        b.Run(2.5);
        Assert.Equal(2, b.Burning.Length);
        // None left: a dry click, no shot.
        b.Run(0.2, _ => Fire);
        Assert.Equal(1, b.Count("up-flare.click"));
        Assert.Equal(2, b.Count("up-flare.shot"));
        // The first flare's minute out: its burn dying away over the last of it, the second still full; then both gone.
        b.Run(C.FlarePistol.BurnSeconds - 7.5);
        Assert.InRange(burn.Volume, 0.01f, 0.99f);
        Assert.Contains(b.Burning, v => v != burn && v.Volume == 1);
        b.Run(8);
        Assert.Empty(b.Burning);
        Assert.DoesNotContain(b.World.Bodies.All, x => x.Kind == BodyKind.Flare);
    }

    [Fact]
    public void AnotherCrewmatesShotIsHeardOnceWhereTheirFlareFirstFlies()
    {
        var b = new Bench();
        b.Stand(1, -6);
        b.Stand(2, 6);
        b.Pistol(2, 1);
        b.Run(0.2);
        // Crewmate 2 fires; this machine's own (1) has nothing in hand: the shot where the flare first flies, at their muzzle.
        b.Run(0.5, id => id == 2 ? Fire : default);
        var shot = Assert.Single(b.Heard, h => h.Name == "up-flare.shot");
        // (The muzzle's at the eye, and the flare's a tick out of it when it's first seen.)
        Assert.True((shot.At - b.Hand(2)).Length < 3, $"{(shot.At - b.Hand(2)).Length:0.0} m from their hand");
        Assert.Equal(0, b.Count("up-flare.click"));
    }

    [Fact]
    public void AClientHearsItsOwnShotAtOnceAndNotAgainWhenTheHostsFlareComes()
    {
        // A client presses Fire: the host fires the flare (World.FireFlare), and its body comes in a few ticks later with the
        // snapshot. Heard at once, at the hand, and only the once.
        var b = new Bench(host: false);
        b.Stand(1, 0);
        var pistol = b.Pistol(1, 1);
        b.Run(0.2);
        b.Step(_ => Fire);
        Assert.Equal(1, b.Count("up-flare.shot"));
        Assert.DoesNotContain(b.World.Bodies.All, x => x.Kind == BodyKind.Flare);
        b.Run(0.15, _ => Fire);
        // The host's record: one flare less, the flare four metres out along the look.
        pistol.Charge = 0;
        var flare = b.World.Bodies.SpawnItem(b.Hand(1) + new Double3(0, 1.5, 0) + b.Train.Frames[2].Right * 4, b.Train.Dynamics.Distance, BodyKind.Flare);
        flare.Charge = 1;
        b.Run(0.5);
        Assert.Equal(1, b.Count("up-flare.shot"));
        // Someone else's a moment later, well away from this one's hand: heard.
        var theirs = b.World.Bodies.SpawnItem(b.Hand(1) + b.Train.Frames[2].Right * 60, b.Train.Dynamics.Distance, BodyKind.Flare);
        theirs.Charge = 1;
        b.Run(0.2);
        Assert.Equal(2, b.Count("up-flare.shot"));
    }
}
