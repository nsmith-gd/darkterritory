using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The night's new jobs heard (queue #122, note 385): a coupling working loose and tightened (D1's note 356), powder carried
/// to the guns (note 374), the wrench's dents and headlamp (note 301's slice 2) and a stumble on a straining car (note 375),
/// read off what the clients are sent. The sounds are stand-ins, so what's installed doesn't change what's tested: which cue
/// plays, when, how often.
/// </summary>
public class JobSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly UpkeepTuning U = DataFile.Load<UpkeepTuning>(Path.Combine(Content, UpkeepTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    const double Dt = SimConstants.TickSeconds;

    sealed class Bench
    {
        readonly HashSet<int> _seen = [];
        readonly HashSet<string> _under;
        public readonly World World;
        public readonly GameAudio Audio;
        public double? Speed;

        public Bench(LineDefinition? line, params string[] sounds)
        {
            var rail = new RailLine(line ?? new LineDefinition("t", [new TrackSegment(60_000)]));
            World = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), rail, 5_000), C);
            World.Upkeep = U;
            Audio = new GameAudio(Content);
            Audio.PlayerTuning = P;
            _under = [.. sounds.Select(s => s.TrimEnd('~'))];
            foreach (var s in sounds)
                Audio.Bank.Add(s.TrimEnd('~'), new SoundDef(5, [new LayerDef(SourceKind.Noise, 0.1)], Loop: s.EndsWith('~'), Duration: 0.1, MaxInstances: 16));
        }

        public TrainOnLine Train => World.Train;

        /// <summary>One tick, as a client hears it; the sounds under test it started (the content's own bed and world go on too).</summary>
        public List<string> Update(params (int, PlayerState)[] crew)
        {
            World.BeginTick();
            World.Step(new TrainControls { Reverser = 1 });
            if (Speed is { } v)
                Train.Dynamics.Velocity = v;
            Audio.CrewStates = crew;
            var frame = Train.Frames[2];
            Audio.Update(World, new TrainControls { Reverser = 1 }, Listener.At(frame.ToWorld(new Double3(4, 1.6, 0)), frame.Heading), exposed: true, Dt);
            return [.. Audio.Mixer.Voices.Where(v => _seen.Add(v.Id) && _under.Contains(v.Name)).Select(v => v.Name)];
        }

        public List<string> Run(double seconds, params (int, PlayerState)[] crew)
        {
            var heard = new List<string>();
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
                heard.AddRange(Update(crew));
            return heard;
        }

        public bool Playing(string name) => Audio.Mixer.Voices.Any(v => v.Name == name && !v.Stopped);
    }

    static PlayerState InTheGap(int car, double progress = 0) => new()
    {
        Parent = car,
        Surface = Surface.Coupler,
        Position = new Double3(T.Geometry.PlateX, T.Geometry.CouplerHeight, T.Geometry.CarLength / 2 + 0.7),
        Health = 100,
        Kit = Kit.Of([Tool.Shovel, Tool.Wrench]),
        HeldSlot = 1,
        ActionProgress = progress,
    };

    [Fact]
    public void ALooseCouplingKnocksFasterAsItWorksOutAndTheWrenchSeatsItsPin()
    {
        var b = new Bench(null, "state-coupling-loose.knock", "crew-upkeep.tighten~", "crew-upkeep.tightened");
        var v = b.Train.Vehicles[2];
        // Just come loose, and about to drop: a knock a take, faster the looser it is (D1's synth's 1.4 to 5.5 a second).
        v.Loose = 1;
        int early = b.Run(10).Count(h => h == "state-coupling-loose.knock");
        v.Loose = U.Coupling.PartAfter - 12;
        int late = b.Run(10).Count(h => h == "state-coupling-loose.knock");
        Assert.InRange(early, 10, 18);
        Assert.True(late > 2.5 * early, $"{early} knocks just loose, {late} about to drop");
        // The wrench on it in the gap: held while the hold goes on, and the pin seated home once, the knocking gone.
        v.Loose = 30;
        Assert.Contains("crew-upkeep.tighten", b.Update((1, InTheGap(2, 1.0))));
        b.Update((1, InTheGap(2, 2.0)));
        Assert.True(b.Playing("crew-upkeep.tighten"));
        v.Loose = 0;
        var done = b.Run(2, (1, InTheGap(2)));
        Assert.Equal(["crew-upkeep.tightened"], done);
        Assert.False(b.Playing("crew-upkeep.tighten"));
        // Left to drop, nobody at it: no seating, only the parting (the knuckle's and the hoses', CrewCouplings).
        v.Loose = 60;
        b.Run(1);
        v.Loose = 0;
        Assert.DoesNotContain("crew-upkeep.tightened", b.Run(1));
    }

    [Fact]
    public void AChargeTakenFromTheLockerIsHeardFillingTheRackAndTheRackFull()
    {
        var b = new Bench(null, "crew-powder.take", "crew-powder.fill~", "crew-powder.filled");
        var guard = b.Train.Vehicles.Last(v => v.HasGun);
        guard.Gun.Rack = 0;
        var at = new PlayerState { Parent = guard.Id, Surface = Surface.Deck, Health = 100 };
        b.Update((1, at));
        // A charge comes into the hands at the locker (Bodies.Handle's fetch): taken, once.
        var charge = b.World.Bodies.SpawnCrate(b.Train, guard.Id, new Double3(0, 1.2, 0), BodyKind.Powder);
        charge.Carrier = 1;
        Assert.Equal(["crew-powder.take"], b.Update((1, at)));
        Assert.Empty(b.Run(0.5, (1, at)));
        // Use held at the gun: the rack filled while it goes on (World.Charge's count), then full.
        for (int i = 1; i <= 10; i++)
        {
            charge.MendTicks = i;
            b.Update((1, at));
        }
        Assert.True(b.Playing("crew-powder.fill"));
        b.World.Bodies.Remove(charge);
        guard.Gun.Rack = C.Guns.Rack;
        Assert.Contains("crew-powder.filled", b.Run(0.5, (1, at)));
        Assert.False(b.Playing("crew-powder.fill"));
    }

    [Fact]
    public void ADentBeatenOutAndTheHeadlampMendedWithTheWrenchAreHeardAndDone()
    {
        var b = new Bench(null, "crew-repair.dent~", "crew-repair.lamp~", "crew-repair.done");
        var train = b.Train;
        Assert.True(Repairs.ByWrench(train));
        // A battered car, a crewmate inside at its dent with the wrench in hand.
        var car = train.Vehicles[2];
        car.Integrity = 0.3;
        var dent = Repairs.DentAt(train, 2)!.Value;
        var mender = new PlayerState
        {
            Parent = 2,
            Surface = Surface.Deck,
            Position = dent with { Y = train.Frames[2].Shape.Interior!.Value.Min.Y },
            Health = 100,
            Kit = Kit.Of([Tool.Shovel, Tool.Wrench]),
            HeldSlot = 1,
        };
        Assert.Equal(2, Repairs.Dent(mender, train));
        Assert.Empty(b.Update((1, mender)));
        Assert.Contains("crew-repair.dent", b.Update((1, mender with { ActionProgress = 0.5 })));
        b.Update((1, mender with { ActionProgress = 1.0 }));
        Assert.True(b.Playing("crew-repair.dent"));
        car.Integrity = 1;
        Assert.Equal(["crew-repair.done"], b.Run(0.5, (1, mender)));
        Assert.False(b.Playing("crew-repair.dent"));
        // The smashed headlamp, from the cab's front windows.
        b.World.LampOutSeconds = 5;
        var cab = PlayerMotor.SpawnInCab(train, P) with { Kit = Kit.Of([Tool.Shovel, Tool.Wrench]), HeldSlot = 1 };
        cab = cab with { Position = cab.Position with { Z = PlayerMotor.CabFloorZ(train.Frames[0].Shape) } };
        Assert.True(Repairs.Lamp(cab, train));
        Assert.Contains("crew-repair.lamp", b.Update((1, cab with { ActionProgress = 0.5 })));
        b.Update((1, cab with { ActionProgress = 1.0 }));
        b.World.LampOutSeconds = 0;
        Assert.Equal(["crew-repair.done"], b.Run(0.5, (1, cab)));
    }

    [Fact]
    public void ACrewmateOnACarStrainingOnABendScuffsForFootingAndNotOnAnEasyOne()
    {
        // Note 375's stumble, by the art's rule (SceneArt.StumbleAt): the car past halfway from the bend's board to off.
        var bend = new LineDefinition("t", [new TrackSegment(4_000), new TrackSegment(3_000, Radius: 200)]);
        var b = new Bench(bend, "crew-footsteps.scuff.roof");
        var roof = PlayerMotor.SpawnOnRoof(b.Train, 2, 0, P);
        b.Speed = 6;
        b.Run(1, (1, roof));
        Assert.DoesNotContain("crew-footsteps.scuff.roof", b.Run(3, (1, roof)));
        b.Speed = 14;
        var heard = b.Run(3, (1, roof));
        Assert.InRange(heard.Count(h => h == "crew-footsteps.scuff.roof"), 4, 8);
        // Busy hands keep their act: no stumble (once the last one's step back has landed).
        var busy = roof with { ActionProgress = 0.5 };
        b.Run(0.5, (1, busy));
        Assert.DoesNotContain("crew-footsteps.scuff.roof", b.Run(2, (1, busy)));
    }
}
