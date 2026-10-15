using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The radio as a thing (T41, spec A.5): worn on the belt, one each, and only those wearing one are on it. The loose sets'
/// tests run with note 749's radio each off; with it on (the game's), every crewmate wears one, part of the uniform.
/// </summary>
public class RadioTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(50_000)]));

    static World Stocked(int cars = 6)
    {
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.LooseRadios, cars, 1)), Line, 1_000));
        world.EnableBodies();
        world.Stock();
        return world;
    }

    static Body RadioIn(World world, int vehicle) => world.Bodies.All.First(b => b.Kind == BodyKind.Radio && b.Parent == vehicle);

    /// <summary>A player standing beside a body on its car's floor, facing it.</summary>
    static PlayerState Beside(World world, Body b)
    {
        var shape = world.Train.Frames[b.Parent].Shape;
        double floor = shape.Cab?.Min.Y + 0.1 ?? shape.Interior!.Value.Min.Y + 0.1;
        return new PlayerState { Parent = b.Parent, Position = new Double3(b.Centre.X, floor, b.Centre.Z + 0.7), Surface = Surface.Deck, Health = P.Health };
    }

    static void Press(World world, ref PlayerState s, PlayerButtons buttons, int id = 1)
    {
        world.CrewAct(ref s, new PlayerIntent { Buttons = buttons }, id);
        world.CrewAct(ref s, default, id);
        world.StepBodies([(id, s)]);
    }

    [Fact]
    public void TheTrainLeavesWithItsRadios()
    {
        var world = Stocked();
        Assert.Equal(Tuning.Train.Kit.Radios, world.Bodies.All.Count(b => b.Kind == BodyKind.Radio));
        // One in the cab, the rest in the guard van; nobody's wearing one yet, so nobody's on the radio.
        RadioIn(world, 0);
        RadioIn(world, world.Train.Vehicles.Count - 1);
        Assert.True(world.Bodies.RadiosCarried);
        Assert.False(world.Bodies.HasRadio(1));
    }

    [Fact]
    public void ARadioGoesOnTheBeltAndLeavesTheHandsFree()
    {
        var world = Stocked();
        var radio = RadioIn(world, world.Train.Vehicles.Count - 1);
        var s = Beside(world, radio);
        Press(world, ref s, PlayerButtons.Use);
        Assert.Equal(1, radio.Carrier);
        Assert.True(world.Bodies.HasRadio(1));
        Assert.Null(world.Bodies.CarriedBy(1));
        // Still a pair of free hands: the lamp beside it comes too, and the radio stays on the belt.
        var lamp = world.Bodies.All.First(b => b.Kind == BodyKind.Lamp && !b.Stowed);
        s = Beside(world, lamp);
        Press(world, ref s, PlayerButtons.Use);
        Assert.Same(lamp, world.Bodies.CarriedBy(1));
        Assert.True(world.Bodies.HasRadio(1));
        // It rides on the belt as you go.
        Assert.InRange((world.Train.Frames[radio.Parent].ToWorld(radio.Centre) - PlayerMotor.WorldPosition(s, world.Train)).Length, 0.8, 1.2);
    }

    [Fact]
    public void OneEachAndPassedOnBySettingItDown()
    {
        var world = Stocked(cars: 4);
        var guard = world.Train.Vehicles.Count - 1;
        var first = RadioIn(world, guard);
        var s = Beside(world, first);
        Press(world, ref s, PlayerButtons.Use);
        // Another one in reach isn't taken: one's enough.
        var second = world.Bodies.SpawnCrate(world.Train, guard, first.Centre with { X = first.Centre.X + 0.2 }, BodyKind.Radio);
        Press(world, ref s, PlayerButtons.Use);
        Assert.Equal(-1, second.Carrier);
        // (That press took the guard van's lamp instead, the next thing in reach: put it back down.)
        if (world.Bodies.CarriedBy(1) is not null)
            Press(world, ref s, PlayerButtons.Use);
        Assert.Null(world.Bodies.CarriedBy(1));
        // Empty hands, right mouse: it comes off the belt and down, for someone else to take.
        Press(world, ref s, PlayerButtons.Throw);
        Assert.Equal(-1, first.Carrier);
        Assert.False(world.Bodies.HasRadio(1));
        var mate = Beside(world, first);
        Press(world, ref mate, PlayerButtons.Use, id: 2);
        Assert.True(world.Bodies.HasRadio(2));
    }

    [Fact]
    public void TheDeadDropTheirs()
    {
        var world = Stocked();
        var radio = RadioIn(world, 0);
        var s = Beside(world, radio);
        Press(world, ref s, PlayerButtons.Use);
        Assert.True(world.Bodies.HasRadio(1));
        s = s with { Health = 0, Death = DeathCause.Mauled };
        Press(world, ref s, PlayerButtons.None);
        Assert.Equal(-1, radio.Carrier);
        Assert.False(world.Bodies.HasRadio(1));
    }

    [Fact]
    public void WithoutOneYouAreNotOnIt()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.LooseRadios, 20, 1)), Line, 1_000);
        var a = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        var b = PlayerMotor.SpawnOnRoof(train, 19, 0, P);
        Assert.Equal(VoicePath.Radio, VoiceRouting.Route(a, b, radio: true, train));
        Assert.Equal(VoicePath.None, VoiceRouting.Route(a, b, radio: true, train, listenerRadio: false));
        // Spec A.5 "dies in tunnels and mine spurs": dead wherever it's underground.
        Assert.Equal(VoicePath.None, VoiceRouting.Route(a, b, radio: true, train, underground: s => s.Parent == 19));
    }

    [Fact]
    public void TheHostOnlyPutsTheWearersOnTheRadio()
    {
        var net = new LoopbackNetwork();
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.LooseRadios, 12, 1)), Line, 1_000);
        var world = new World(Train());
        world.EnableBodies();
        world.Stock();
        var host = new HostSession(net.CreateHost(), world, Tuning.LooseRadios, P);
        var talker = new ClientSession(net.CreateClient(), Train(), Tuning.LooseRadios, P);
        var listener = new ClientSession(net.CreateClient(), Train(), Tuning.LooseRadios, P);
        void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                talker.Step(default);
                listener.Step(default);
            }
        }
        Step(30);
        // Put the listener at the far end of the train, out of earshot: only the radio could reach them.
        host.SetPlayerState(listener.PlayerId!.Value, PlayerMotor.SpawnOnRoof(host.Train, 11, 0, P));
        Step(5);
        var at = PlayerMotor.WorldPosition(host.Players.First(p => p.Id == listener.PlayerId).State, host.Train);
        Assert.True((at - PlayerMotor.WorldPosition(host.Players.First(p => p.Id == talker.PlayerId).State, host.Train)).Length > VoiceRouting.ProximityCutoff + 5);

        int Heard()
        {
            listener.VoiceFrames.Clear();
            talker.SendVoice(1, radio: true, new byte[] { 1, 2, 3 });
            Step(3);
            return listener.VoiceFrames.Count(f => f.Path.HasFlag(VoicePath.Radio));
        }
        // Radios are things tonight and neither of them has one.
        Assert.Equal(0, Heard());
        var radios = world.Bodies.All.Where(b => b.Kind == BodyKind.Radio).ToList();
        radios[0].Carrier = talker.PlayerId!.Value;
        Assert.Equal(0, Heard());
        radios[1].Carrier = listener.PlayerId!.Value;
        Assert.Equal(1, Heard());
    }

    /// <summary>
    /// GDD v1.4 App. C.8, "radio broadcast of a GRAB": a grabbed player wearing a radio keys it open for the whole GRAB, so
    /// everyone else on the radio hears them, whatever they meant to send it on; it closes at break-off and with the
    /// hard-cut at death (D.2), when there's only the dead channel, and only the dead hear that.
    /// </summary>
    [Fact]
    public void AGrabKeysTheVictimsRadioOpenUntilItLetsGoOrTheyDie()
    {
        var net = new LoopbackNetwork();
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.LooseRadios, 12, 1)), Line, 1_000);
        var world = new World(Train());
        world.EnableBodies();
        world.Stock();
        var host = new HostSession(net.CreateHost(), world, Tuning.LooseRadios, P);
        var talker = new ClientSession(net.CreateClient(), Train(), Tuning.LooseRadios, P);
        var listener = new ClientSession(net.CreateClient(), Train(), Tuning.LooseRadios, P);
        void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                talker.Step(default);
                listener.Step(default);
            }
        }
        Step(30);
        byte t = talker.PlayerId!.Value, l = listener.PlayerId!.Value;
        host.SetPlayerState(l, PlayerMotor.SpawnOnRoof(host.Train, 11, 0, P));
        var radios = world.Bodies.All.Where(b => b.Kind == BodyKind.Radio).ToList();
        radios[0].Carrier = t;
        radios[1].Carrier = l;
        Step(5);
        // Held is the sim's, each tick, from whatever has hold of you; here it's set just before the frame reaches the host,
        // which routes voice before it steps.
        VoicePath Heard(bool held, bool dead = false)
        {
            listener.VoiceFrames.Clear();
            var me = host.Players.First(p => p.Id == t).State;
            var flags = held ? me.Flags | PlayerFlags.Held : me.Flags & ~PlayerFlags.Held;
            host.SetPlayerState(t, dead ? me with { Flags = flags, Health = 0, Death = DeathCause.Eaten } : me with { Flags = flags, Health = P.Health, Death = DeathCause.None });
            // Not on the radio: just talking (or screaming).
            talker.SendVoice(1, radio: false, new byte[] { 1, 2, 3 });
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            Step(2);
            return listener.VoiceFrames.Aggregate(VoicePath.None, (p, f) => p | f.Path);
        }
        Assert.False(Heard(held: false).HasFlag(VoicePath.Radio));
        Assert.True(Heard(held: true).HasFlag(VoicePath.Radio), "a held player is on the radio");
        // Let go: off it again.
        Assert.False(Heard(held: false).HasFlag(VoicePath.Radio));
        // Dead mid-GRAB: cut. Nothing of them reaches a living listener, near or on the radio: only the host's cut, so what
        // they had of the last words is dropped (D.2).
        Assert.Equal(VoicePath.Cut, Heard(held: true, dead: true));
        Assert.Equal(VoicePath.None, Heard(held: true, dead: true));
    }

    // Note 749 (GDD §12; the director, 14 Oct 2026: "make the radios part of the default kit"; "Radio is part of uniform and
    // doesnt take inventory space"): with kit.radioEach, the game's, every crewmate wears one.

    static World Uniformed(int cars = 6)
    {
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), Line, 1_000));
        world.EnableBodies();
        world.Stock();
        return world;
    }

    static Body Worn(World world, int id) => world.Bodies.All.Single(b => b.Kind == BodyKind.Radio && b.Carrier == id);

    [Fact]
    public void EveryLivingCrewmateWearsARadioFromTheNightsFirstTick()
    {
        Assert.True(Tuning.Train.Kit.RadioEach);
        var world = Uniformed();
        // None lies loose: they're the crew's uniform.
        Assert.DoesNotContain(world.Bodies.All, b => b.Kind == BodyKind.Radio);
        Assert.True(world.Bodies.RadiosCarried);
        var a = PlayerMotor.SpawnOnRoof(world.Train, 1, 0, P);
        var b = PlayerMotor.SpawnOnRoof(world.Train, 4, 0, P);
        var dead = PlayerMotor.SpawnOnRoof(world.Train, 2, 0, P) with { Health = 0, Death = DeathCause.Mauled };
        world.StepBodies([(1, a), (2, b), (3, dead)]);
        Assert.True(world.Bodies.HasRadio(1));
        Assert.True(world.Bodies.HasRadio(2));
        Assert.False(world.Bodies.HasRadio(3));
        Assert.Equal(2, world.Bodies.All.Count(r => r.Kind == BodyKind.Radio));
        // A joiner (or a bot) gets theirs on the tick they're in the crew; nobody gets a second.
        var joiner = PlayerMotor.SpawnOnRoof(world.Train, 3, 0, P);
        world.StepBodies([(1, a), (2, b), (3, dead), (4, joiner)]);
        world.StepBodies([(1, a), (2, b), (3, dead), (4, joiner)]);
        Assert.True(world.Bodies.HasRadio(4));
        Assert.Equal(3, world.Bodies.All.Count(r => r.Kind == BodyKind.Radio));
        // On the belt, riding with them.
        var radio = Worn(world, 2);
        Assert.InRange((world.Train.Frames[radio.Parent].ToWorld(radio.Centre) - PlayerMotor.WorldPosition(b, world.Train)).Length, 0.8, 1.2);
    }

    [Fact]
    public void TheUniformsRadioTakesNoSlotAndNeverComesOff()
    {
        var world = Uniformed();
        var s = PlayerMotor.SpawnOnRoof(world.Train, 2, 0, P);
        world.StepBodies([(1, s)]);
        var radio = Worn(world, 1);
        // Not in the hands, not in a slot.
        Assert.Null(world.Bodies.CarriedBy(1));
        Assert.Empty(world.Bodies.Slotted(1));
        Assert.Equal(-1, radio.Pocket);
        // Empty hands and Throw: it stays on the belt (with the loose sets, that set it down).
        Press(world, ref s, PlayerButtons.Throw);
        Press(world, ref s, PlayerButtons.Throw);
        Assert.Same(radio, Worn(world, 1));
        Assert.True(world.Bodies.HasRadio(1));
    }

    [Fact]
    public void TheDeadLoseTheirsAndComeBackWithAWorkingOne()
    {
        var world = Uniformed();
        var s = PlayerMotor.SpawnOnRoof(world.Train, 2, 0, P);
        world.StepBodies([(1, s)]);
        var radio = Worn(world, 1);
        radio.Broken = true;
        Assert.False(world.Bodies.HasRadio(1));
        // Dead: it goes with them, not left squawking on the roof.
        var dead = s with { Health = 0, Death = DeathCause.Mauled };
        Press(world, ref dead, PlayerButtons.None);
        Assert.DoesNotContain(world.Bodies.All, b => b.Kind == BodyKind.Radio);
        // Back from a Holdout (App. D.8): a working one on the belt.
        var back = PlayerMotor.SpawnOnRoof(world.Train, 3, 0, P);
        world.StepBodies([(1, back)]);
        Assert.False(Worn(world, 1).Broken);
        Assert.True(world.Bodies.HasRadio(1));
    }

    [Fact]
    public void AGrabNeverBreaksTheRadioAHardKnockStillCan()
    {
        // App. C.8's broadcast of a GRAB is the point: the grab doesn't smash the set (it was 0.25).
        Assert.Equal(0, Tuning.Train.Kit.RadioBreakOnGrab);
        Assert.True(Tuning.Train.Kit.RadioBreakPerDamage > 0);
        for (int id = 1; id <= 200; id++)
        {
            var world = Uniformed();
            var s = PlayerMotor.SpawnOnRoof(world.Train, 2, 0, P);
            world.StepBodies([(id, s)]);
            world.StepBodies([(id, s with { Flags = s.Flags | PlayerFlags.Held })]);
            Assert.False(Worn(world, id).Broken);
        }
    }

    [Fact]
    public void OffTheTrainCarriesItsLooseSetsAsBefore()
    {
        var world = Stocked();
        var s = PlayerMotor.SpawnOnRoof(world.Train, 2, 0, P);
        world.StepBodies([(1, s)]);
        // Nobody's given one: the sets lie where they were stowed, and nobody's on the radio till they put one on.
        Assert.Equal(Tuning.LooseRadios.Kit.Radios, world.Bodies.All.Count(b => b.Kind == BodyKind.Radio));
        Assert.All(world.Bodies.All.Where(b => b.Kind == BodyKind.Radio), b => Assert.Equal(-1, b.Carrier));
        Assert.False(world.Bodies.HasRadio(1));
    }
}
