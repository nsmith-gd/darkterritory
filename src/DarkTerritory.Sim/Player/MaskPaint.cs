namespace DarkTerritory.Sim.Player;

/// <summary>
/// A crewmate's painted gas mask (GDD §29 "Painted masks", §9's yard; ARCHITECTURE §8 note 731): its base colour, a stencil
/// and the stencil's colour, each counted from 1 (look.json <c>maskPaints</c>, <c>maskStencils</c>); 0 is none: the mask as it
/// came, no stencil, the stencil in the default ink (look.json <c>maskInk</c>). Chosen in the settings, said in the Hello and
/// tried on in the yard as an outfit is (note 298), but in bytes of its own, apart from the outfit's, so the hat rack's
/// (note 714) and this change apart. Presentation only: nothing in the sim reads it.
/// </summary>
public readonly record struct MaskPaint(byte Colour, byte Stencil, byte Ink)
{
    /// <summary>Unpainted: the mask as the crew's kit has it.</summary>
    public static readonly MaskPaint None = default;

    /// <summary>Anything on it at all.</summary>
    public bool Painted => Colour != 0 || Stencil != 0;

    /// <summary>Each part the host takes (<see cref="Max"/>); past it, that part's none. The art goes round its own lists.</summary>
    public MaskPaint Clamped() => new(Colour < Max ? Colour : (byte)0, Stencil < Max ? Stencil : (byte)0, Ink < Max ? Ink : (byte)0);

    /// <summary>The most of each part there can be, from 1 (the art draws as many as look.json lists, round again past them).</summary>
    public const int Max = 32;
}
