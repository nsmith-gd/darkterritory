using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// G1's six creatures heard (queue #125, note 388; their sims notes 362-367): the Mourners, Tower Jaw, the Brakeman, the
/// Knotter, the Freight Beetle and Hotbox, each read off its replicated record (its mode in Height, as each sim's remarks
/// say) against last tick's, as the rest of <see cref="GameAudio"/>'s creatures are. A wound handbrake's squeal and a seized
/// axle's grind are the cars' own (<c>Vehicle.Wound</c>, <c>Vehicle.Seized</c>, replicated with the train): they go on
/// after the creature that did it is gone.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The Brakeman's gait (tools/audio/recipes/g1_train.py LIMP): a long stance on the good leg, a quick one on the bad.</summary>
    const double LimpGood = 0.74, LimpBad = 0.46;
    /// <summary>A bad-leg take starts with its toe dragged (g1_train.py's step): fired this early, its plant lands on the beat.</summary>
    const double LimpLead = 0.2;
    /// <summary>He winds a brake wheel on in pawl runs (tell-brakeman.ratchet), one every second or so while he's at it.</summary>
    const double WindRatchetEvery = 1.0;
    /// <summary>A wound brake squeals, and a seized axle grinds, only with the wheels turning.</summary>
    const double TurningAbove = 0.5;
    /// <summary>Taut, the Knotter's hawser creaks as the train's speed changes this sharply (m/s²), at most every so often.</summary>
    const double HawserAccel = 0.25, HawserEvery = 1.5;
    /// <summary>The tower creaks from up its height, not at the beaver's feet.</summary>
    const double TowerUp = 6;

    void SixSounds(World world, Enemy e, Creature was, Double3 at, float occ, bool struck, double dt)
    {
        switch (e)
        {
            case Mourner m:
                MournerSounds(m, was, at, occ);
                break;
            case TowerJaw j:
                TowerJawSounds(world, j, was, at, occ);
                break;
            case Brakeman b:
                BrakemanSounds(world, b, was, at, occ);
                break;
            case Knotter k:
                KnotterSounds(world, k, was, at, occ);
                break;
            case FreightBeetle f:
                BeetleSounds(world, f, was, at, occ, struck);
                break;
            case Hotbox h:
                HotboxSounds(world, h, was, at, occ, dt);
                break;
        }
    }

    /// <summary>What goes on with no creature's record to read: a group's keening, wound brakes, seized axles.</summary>
    void SixSoundsAfter(World world)
    {
        MournersKeening(world);
        TrainDragging(world);
    }

    /// <summary>
    /// The Mourners (note 362): startled, each one's dry clicks, a moment apart; two of them hauling a body away, the drag
    /// and their breathless keening. The group's keening as they gather is <see cref="MournersKeening"/>.
    /// </summary>
    void MournerSounds(Mourner m, Creature was, Double3 at, float occ)
    {
        var before = (MournerMode)(int)was.Height;
        if (m.Mode == MournerMode.Startle && before != MournerMode.Startle)
            CueLater(0.3 * _creatureRng.Next(), "tell-mourners.clicks", at, occ);
        // Hauler and partner hold the one drag between them: it's the body's.
        if (m.Mode == MournerMode.Drag)
            Hold("cs-mourners-drag.drag", m.BodyId, at, occ);
    }

    /// <summary>
    /// After a death, the Mourners' soft keening from the dark beyond the body (note 362's telegraph): one sound for each
    /// group, where those of it who aren't hauling or leaving are, for as long as any are.
    /// </summary>
    void MournersKeening(World world)
    {
        var train = world.Train;
        foreach (var group in world.ActiveEnemies.OfType<Mourner>()
                     .Where(m => !m.Gone && m.Mode is not (MournerMode.Drag or MournerMode.Leave)).GroupBy(m => (int)m.Extra2))
        {
            var sum = Double3.Zero;
            int n = 0;
            foreach (var m in group)
            {
                sum += m.WorldPosition(train);
                n++;
            }
            Hold("tell-mourners.keening", group.Key, sum * (1.0 / n), Occlusion(PlayerMotor.Outside));
        }
    }

    /// <summary>
    /// Tower Jaw (note 363): its chiselling while it gnaws; the structure creaking from half gnawed (LeanFrom) and groaning
    /// its last seconds; come near, it rears and slaps its tail till it lunges; gnawed through, the crack like a shot at the
    /// leg and the structure crashing down across where it falls (the record's at the wreck from that tick).
    /// </summary>
    void TowerJawSounds(World world, TowerJaw j, Creature was, Double3 at, float occ)
    {
        var t = world.Enemies?.TowerJaw ?? new TowerJawTuning();
        var mode = j.Mode;
        var before = (TowerJawMode)(int)was.Height;
        bool began = mode != before;
        var up = Double3.Up * TowerUp;
        switch (mode)
        {
            case TowerJawMode.Gnaw:
                Hold("tell-tower-jaw.gnaw", j.Id, at + Double3.Up * 0.6, occ);
                if ((1 - j.Gnawed) * t.GnawFor(world.Route?.Tier ?? RouteTier.Frontier) <= t.GroanSeconds)
                    Hold("tell-tower-jaw.groan", j.Id, at + up, occ);
                break;
            case TowerJawMode.Threat:
                // Rearing, slapping its tail: at once, then every second and a half till it lunges or you back off.
                if (began || _time >= was.Stride)
                {
                    Cue("cs-tower-jaw.tail-slap", at, occ);
                    was.Stride = _time + t.ThreatSeconds * (0.85 + 0.3 * _creatureRng.Next());
                }
                break;
            case TowerJawMode.Lunge when began:
                Cue("cs-tower-jaw.lunge", at, occ);
                break;
            case TowerJawMode.Wreck when began:
                Cue("tell-tower-jaw.crack", was.At + Double3.Up * 4, Occlusion(was.Space));
                CueLater(0.6, "cs-tower-jaw.fall", at, occ);
                break;
        }
        // Half gnawed, the structure creaks now and then, whatever the beaver's at.
        if (mode != TowerJawMode.Wreck && j.Gnawed >= t.LeanFrom)
        {
            if (was.Next <= 0)
                was.Next = _time + 1 + 2 * _creatureRng.Next();
            else if (_time >= was.Next)
            {
                Cue("tell-tower-jaw.creak", at + up, occ);
                was.Next = _time + 3 + 4 * _creatureRng.Next();
            }
        }
    }

    /// <summary>
    /// The Brakeman (note 364): his iron-shod steps along the roofs, limping (the good leg's takes then the bad leg's, on a
    /// long-short beat: <see cref="LimpGood"/>, <see cref="LimpBad"/>, quicker as he runs) with his chain dragged behind
    /// him; at a brake wheel, its pawl ratcheting as he winds it on; cornered, the chain gathered up, his wheeze, and the
    /// chain's lash each time it lands (the commit and back in one tick: the phase's clock starts again).
    /// </summary>
    void BrakemanSounds(World world, Brakeman b, Creature was, Double3 at, float occ)
    {
        var t = world.Enemies?.Brakeman ?? new BrakemanTuning();
        var mode = (BrakemanMode)(int)b.Height;
        var before = (BrakemanMode)(int)was.Height;
        bool began = mode != before;
        switch (mode)
        {
            case BrakemanMode.Walk or BrakemanMode.Flee:
                // Setting off (or first heard on his way): his good leg first, now.
                if (before is not (BrakemanMode.Walk or BrakemanMode.Flee) || was.Next <= 0 || was.Next < _time - LimpGood - LimpBad)
                {
                    was.Next = _time;
                    was.Stride = 0;
                }
                double pace = mode == BrakemanMode.Flee ? t.Walk / Math.Max(t.Walk, t.Flee) : 1;
                if (was.Stride == 0 && _time >= was.Next)
                {
                    Step("good");
                    was.Next += LimpGood * pace;
                    was.Stride = 1;
                }
                else if (was.Stride == 1 && _time >= was.Next - LimpLead)
                {
                    Step("bad");
                    was.Next += LimpBad * pace;
                    was.Stride = 0;
                }
                Hold("tell-brakeman.chain", b.Id, at, occ);
                break;
            case BrakemanMode.Wind:
                if (began || _time >= was.Next)
                {
                    Cue("tell-brakeman.ratchet", at + Double3.Up * 0.4, occ);
                    was.Next = _time + WindRatchetEvery * (0.9 + 0.3 * _creatureRng.Next());
                }
                break;
            case BrakemanMode.Cornered:
                if (began)
                    Cue("cs-brakeman.chain-up", at, occ);
                else if (b.PhaseSeconds + 1e-6 < was.PhaseSeconds)
                    Cue("cs-brakeman.lash", at, occ);
                Hold("cs-brakeman.wheeze", b.Id, at + Double3.Up * 1.5, occ);
                break;
        }

        void Step(string leg)
        {
            if (Surfaced("tell-brakeman.step", leg) is { } name)
                Cue(name, at, occ);
        }
    }

    /// <summary>
    /// The Knotter (note 365): its wet creaking at the coupling while it creeps in (the 4 s to cut it); forcing the cars
    /// apart, the long stretch and the buffers parting; taut, its hawser creaking as the train's speed changes, as it takes
    /// up again from slack, and as it coils round someone slipped off its back.
    /// </summary>
    void KnotterSounds(World world, Knotter k, Creature was, Double3 at, float occ)
    {
        var mode = (KnotterMode)(int)k.Height;
        var before = (KnotterMode)(int)was.Height;
        bool began = mode != before;
        switch (mode)
        {
            case KnotterMode.Creep:
                Hold("tell-knotter.creep", k.Id, at, occ);
                break;
            case KnotterMode.Force when began:
                Cue("tell-knotter.stretch", at, occ);
                break;
            case KnotterMode.Taut:
                bool jerk = Math.Abs(world.Train.Dynamics.Acceleration) >= HawserAccel && _time >= was.Next;
                if (began && before == KnotterMode.Slack || jerk)
                    Hawser();
                break;
            case KnotterMode.Coil when began:
                Hawser();
                break;
        }

        void Hawser()
        {
            Cue("tell-knotter.hawser", at, occ);
            was.Next = _time + HawserEvery * (1 + 0.5 * _creatureRng.Next());
        }
    }

    /// <summary>
    /// The Freight Beetle (note 366): a crate shoved in surges, on whatever it's shoved over (the ground, or a platform's
    /// boards); startled at its head, or struck, its plates clattering and a hiss.
    /// </summary>
    void BeetleSounds(World world, FreightBeetle f, Creature was, Double3 at, float occ, bool struck)
    {
        var mode = (BeetleMode)(int)f.Height;
        bool startled = mode == BeetleMode.Startle && (BeetleMode)(int)was.Height != BeetleMode.Startle;
        if (startled || struck && f.Health > 0)
            Cue("cs-freight-beetle.startle", at, occ);
        if (mode == BeetleMode.Push)
        {
            double hint = f.LineDistance;
            if (Surfaced("cs-freight-beetle.push", Footing.Ground(world, at, ref hint)) is { } push)
                Hold(push, f.Id, at, occ);
        }
    }

    /// <summary>
    /// Hotbox (note 367): clamped in its truck, a dull knock once a turn of its wheel (<see cref="Hotbox.KnocksPerSecond"/>,
    /// faster the faster you run); glowing, the knock gone ragged and a wet sizzle under it. Its seized axle's grind is the
    /// car's (<see cref="TrainDragging"/>).
    /// </summary>
    void HotboxSounds(World world, Hotbox h, Creature was, Double3 at, float occ, double dt)
    {
        var t = world.Enemies?.Hotbox ?? new HotboxTuning();
        var mode = (HotboxMode)(int)h.Height;
        if (mode is not (HotboxMode.Knock or HotboxMode.Glow))
        {
            was.Stride = 0;
            return;
        }
        bool glow = mode == HotboxMode.Glow;
        double turns = Hotbox.KnocksPerSecond(t, world.Train.Dynamics.Speed) * dt * (glow ? 0.6 + 0.8 * _creatureRng.Next() : 1);
        was.Stride = Math.Min(was.Stride + turns, 2);
        if (was.Stride >= 1)
        {
            Cue("tell-hotbox.knock", at, occ);
            was.Stride -= 1;
        }
        if (glow)
            Hold("tell-hotbox.sizzle", h.Id, at, occ);
    }

    /// <summary>
    /// The cars dragging with the wheels turning: a handbrake the Brakeman wound on squealing at the shoes (steady: it never
    /// catches once a turn, as a hot box's does), and an axle Hotbox seized grinding, louder the faster the train's going.
    /// </summary>
    void TrainDragging(World world)
    {
        var train = world.Train;
        double speed = train.Dynamics.Speed;
        if (speed < TurningAbove)
            return;
        float outside = Occlusion(PlayerMotor.Outside);
        double level = Math.Clamp(speed / 10, 0.3, 1);
        foreach (var v in train.Vehicles)
        {
            if (!(v.Wound || v.Seized) || v.Id <= 0 || v.Id >= train.Frames.Count || v.Id >= train.Cars.Count)
                continue;
            var frame = train.Frames[v.Id];
            // At the end with the handbrake's wheel (its truck), at the wheels' height.
            var wheel = frame.Shape.Interactables.FirstOrDefault(i => i.Kind == InteractableKind.Handbrake);
            double z = wheel.Kind == InteractableKind.Handbrake ? wheel.Position.Z : train.Cars[v.Id].Length / 2 - 1.6;
            var truck = frame.ToWorld(new Double3(0, 0.5, z));
            if (v.Wound)
                HoldLevel("tell-brakeman.squeal", v.Id, truck, outside, level);
            if (v.Seized)
                HoldLevel("tell-hotbox.grind", v.Id, truck, outside, level);
        }
    }
}
