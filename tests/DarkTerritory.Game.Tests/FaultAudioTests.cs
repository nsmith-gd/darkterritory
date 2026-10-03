using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The train's faults on the audio checklist (GameAudio.Faults.cs): a cannon fouled (state-cannon-foul, GDD §23) and a car
/// breached (state-breach, decided 1 Oct), with its boarding up (crew-repair). The sim runs for real (the crew's hands
/// through World.CrewAct), and each cue is installed as a short tone, so what plays can be counted off the mixer.
/// </summary>
public class FaultAudioTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    const double Dt = SimConstants.TickSeconds;

    const string Misfire = "state-cannon-foul.misfire", Clear = "state-cannon-foul.clear", Cleared = "state-cannon-foul.cleared",
        Breach = "state-breach.breach", OpenToOutside = "state-breach.open-to-outside",
        BoardPlace = "crew-repair.board-place", Hammer = "crew-repair.hammer", Done = "crew-repair.done",
        Fizzle = "crew-mishaps.foul-fizzle";
    static readonly string[] Cues = [Misfire, Clear, Cleared, Breach, OpenToOutside, BoardPlace, Hammer, Done, Fizzle];
    static readonly string[] Loops = [Clear, OpenToOutside];

    static readonly PlayerIntent Fire = new() { Buttons = PlayerButtons.Fire };
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    sealed class Bench
    {
        public readonly World World;
        public readonly GameAudio Audio = new(Content);
        public readonly Dictionary<int, PlayerState> Crew = new();
        public readonly List<(string Name, Double3 At, int Tick, float Occlusion)> Heard = [];
        readonly HashSet<int> _seen = [];
        public int Tick;
        /// <summary>Whose ears (their space and shelter, as the app works them out); unset, over car 1 out in the open.</summary>
        public int? Ears;

        public Bench(CombatTuning? combat = null)
        {
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
            World = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000), combat ?? C);
            foreach (var cue in Cues)
                Audio.Bank.Add(cue, new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Loop: Loops.Contains(cue), Duration: 0.1, MaxInstances: 64));
            Audio.PlayerTuning = P;
            Audio.OwnId = 1;
        }

        public TrainOnLine Train => World.Train;

        /// <summary>One tick: the crew's hands, the world, then what's heard.</summary>
        public void Step(PlayerIntent intent = default)
        {
            World.BeginTick();
            foreach (var id in Crew.Keys.ToList())
            {
                var s = Crew[id];
                World.CrewAct(ref s, intent, id);
                Crew[id] = s;
            }
            World.Step(World.Controls);
            Audio.CrewStates = [.. Crew.Select(c => (c.Key, c.Value))];
            Audio.OwnIntent = intent;
            var ear = Ears is { } e ? PlayerMotor.WorldPosition(Crew[e], Train) + Double3.Up * 1.6 : Train.Frames[1].Origin + Double3.Up * 5;
            int space = Ears is { } who ? PlayerMotor.Space(Crew[who], Train) : PlayerMotor.Outside;
            bool exposed = Ears is not { } me || !PlayerMotor.Indoors(Crew[me], Train);
            Audio.Update(World, World.Controls, Listener.At(ear, 0), exposed, Dt, space);
            foreach (var v in Audio.Mixer.Voices)
                if (_seen.Add(v.Id) && Cues.Contains(v.Name))
                    Heard.Add((v.Name, v.Position, Tick, v.Occlusion));
            Tick++;
        }

        public void Run(double seconds, PlayerIntent intent = default)
        {
            for (int i = 0; i < seconds / Dt; i++)
                Step(intent);
        }

        public int Count(string name) => Heard.Count(h => h.Name == name);
        public bool Playing(string name) => Audio.Mixer.Voices.Any(v => v.Name == name && !v.Stopped);
        public IEnumerable<SoundInstance> Live(string name) => Audio.Mixer.Voices.Where(v => v.Name == name && !v.Stopped);

        /// <summary>The wind past the listener: its recorded loops where they're installed (bed-wind), else the synth's.</summary>
        public bool Windy() => Playing("bed-wind.wind-slow") || Playing("bed-wind.wind-fast")
            || Live("wind").Any(v => v.Params.Get("wind") > 5);

        /// <summary>On a car's floor, in the aisle, this far along it, facing its rear end.</summary>
        public PlayerState Inside(int car, double z) => new()
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, z),
            Surface = Surface.Deck,
            Health = P.Health,
            Yaw = Math.PI,
            LineHint = Train.Cars[car].FrontDistance,
        };
    }

    [Fact]
    public void AFoulClicksAtTheGunIsWorkedClearAndSaysSo()
    {
        // Every pull misfires here: what's heard when one does, not how often.
        var b = new Bench(C with { Guns = C.Guns with { FoulChance = 1 } });
        var mount = b.Train.Frames[0].Shape.Gun!.Value;
        var gunner = PlayerMotor.SpawnOnRoof(b.Train, 0, mount.Position.Z - mount.Facing.Z * 0.7, P);
        b.Crew[1] = gunner with { Yaw = mount.Facing.Z < 0 ? 0 : Math.PI };
        b.Step();
        // The pull that fouls it: a dead click, once, at the touch hole; the trigger held on is no new pull.
        b.Run(0.5, Fire);
        Assert.True(b.Train.Vehicles[0].Gun.Jammed);
        Assert.Equal(1, b.Count(Misfire));
        var touchHole = b.Train.Frames[0].ToWorld(Guns.Mount(b.Train, 0)!.Value.Position);
        Assert.True((b.Heard.Single(h => h.Name == Misfire).At - touchHole).Length < 1);
        // And as it fouls, the damp charge's pfft a beat after the click (crew-mishaps): once, not at every pull.
        Assert.Equal(1, b.Count(Fizzle));
        Assert.True(b.Heard.Single(h => h.Name == Fizzle).Tick > b.Heard.First(h => h.Name == Misfire).Tick);
        // Let go and pull again at the fouled gun: it clicks again.
        b.Run(0.2);
        b.Step(Fire);
        Assert.Equal(2, b.Count(Misfire));
        b.Run(0.3);
        Assert.Equal(1, b.Count(Fizzle));
        // Worked clear by hand: the bore heard while the hold goes on; let go and it stops.
        b.Run(2, Use);
        Assert.True(b.Playing(Clear));
        b.Run(0.5);
        Assert.False(b.Playing(Clear));
        Assert.Equal(0, b.Count(Cleared));
        b.Run(C.Guns.FoulClearSeconds + 0.2, Use);
        Assert.False(b.Train.Vehicles[0].Gun.Jammed);
        Assert.Equal(1, b.Count(Cleared));
        b.Run(0.5, Use);
        Assert.False(b.Playing(Clear));
        Assert.Equal(1, b.Count(Cleared));
        Assert.Equal(2, b.Count(Misfire));
    }

    [Fact]
    public void ABreachWrenchesOnceAtTheHoleAndCarries()
    {
        var b = new Bench();
        b.Step();
        const int car = 2;
        var wall = Breaches.EndWall(b.Train.Frames[car].Shape)!.Value;
        b.Train.Vehicles[car].Breach(wall);
        b.Run(1);
        var heard = Assert.Single(b.Heard, h => h.Name == Breach);
        Assert.True((heard.At - b.Train.Frames[car].ToWorld(wall)).Length < 1e-6);
        // Out in the open over car 1: unmuffled, a car away.
        Assert.Equal(0, heard.Occlusion);
        // The ears aren't in that car: the outside isn't coming in on them.
        Assert.False(b.Playing(OpenToOutside));
    }

    [Fact]
    public void InsideABreachedCarTheOutsideComesInUnmuffled()
    {
        var b = new Bench();
        const int car = 2;
        b.Crew[1] = b.Inside(car, 0);
        b.Ears = 1;
        b.Train.Dynamics.Velocity = 12;
        // The Choir gathering: voices out there round the train, heard through the walls when you're shut in.
        b.World.Choir = new ChoirState { Build = 0.6 };
        b.Run(0.2);
        Assert.NotEmpty(b.Live("choir-voice"));
        Assert.All(b.Live("choir-voice"), v => Assert.Equal(1, v.Occlusion));
        Assert.False(b.Windy());
        Assert.False(b.Playing(OpenToOutside));

        // Breached: the car stops muffling the outside, its wind blows in, and the hole's heard.
        var wall = Breaches.EndWall(b.Train.Frames[car].Shape)!.Value;
        b.Train.Vehicles[car].Breach(wall);
        b.Run(0.2);
        Assert.All(b.Live("choir-voice"), v => Assert.Equal(0, v.Occlusion));
        Assert.True(b.Windy());
        var hole = Assert.Single(b.Live(OpenToOutside));
        Assert.True((hole.Position - b.Train.Frames[car].ToWorld(wall)).Length < 1e-6);
        Assert.Equal(0, hole.Occlusion);

        // Boarded up: shut in again.
        b.Train.Vehicles[car].Breached = false;
        b.Run(0.2);
        Assert.All(b.Live("choir-voice"), v => Assert.Equal(1, v.Occlusion));
        Assert.False(b.Windy());
        Assert.False(b.Playing(OpenToOutside));
    }

    [Fact]
    public void BoardingItUpPutsABoardToTheHoleHammersItHomeAndIsDone()
    {
        var b = new Bench();
        const int car = 2;
        b.Train.Vehicles[car].Breach(Breaches.EndWall(b.Train.Frames[car].Shape)!.Value);
        b.Crew[1] = b.Inside(car, 0) with { Position = Breaches.StandAt(b.Train, car) };
        b.Step();
        var hole = b.Train.Frames[car].ToWorld(b.Train.Vehicles[car].BreachAt);
        // A board up, the first nails, then let go: the next hold puts up a board again.
        b.Run(2, Use);
        Assert.Equal(1, b.Count(BoardPlace));
        Assert.True((b.Heard.Single(h => h.Name == BoardPlace).At - hole).Length < 1e-6);
        Assert.InRange(b.Count(Hammer), 2, 4);
        b.Run(0.3);
        int nails = b.Count(Hammer);
        b.Run(0.5);
        Assert.Equal(nails, b.Count(Hammer));
        b.Run(T.Breach.BoardSeconds + 0.2, Use);
        Assert.False(b.Train.Vehicles[car].Breached);
        Assert.Equal(2, b.Count(BoardPlace));
        // A nail every half second or so, the length of the hold.
        Assert.InRange(b.Count(Hammer) - nails, 10, 18);
        var done = Assert.Single(b.Heard, h => h.Name == Done);
        Assert.True((done.At - hole).Length < 1e-6);
        // Done, and the hand still held on hammers nothing more.
        nails = b.Count(Hammer);
        b.Run(1, Use);
        Assert.Equal(nails, b.Count(Hammer));
        Assert.Equal(1, b.Count(Done));
    }
}
