namespace DarkTerritory.Sim.Train;

/// <summary>What cut a coupling (note 663): said to the crew as it happens, and in the report.</summary>
public enum Parting : byte { Unknown, Pin, Knotter, Crew, CarHugger, Passenger, Apart, Shunt }

/// <summary>A coupling cut this tick: the car ahead of it, the car that was behind it, what cut it and whose hand (−1 none).</summary>
public readonly record struct PartingMark(int Ahead, int Behind, Parting Cause, int Who);

/// <summary>
/// What last knocked a vehicle's shell about (note 663), and at what speed: what the crew's told took the engine down
/// ("ENGINE FAILING · HIT THE BUFFER STOP AT 21 KM/H").
/// </summary>
public enum KnockCause : byte { Unknown, BufferStop, Collision, Wreck, Sleepers, Points, Brass, Bend, Fire, Blast, Worn }
