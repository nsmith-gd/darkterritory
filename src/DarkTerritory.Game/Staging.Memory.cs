using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

/// <summary>Crew memory staged for the screenshots (note 721): the photo's crew, a logbook's pages, the lost crews' cards.</summary>
public static partial class Staging
{
    /// <summary>
    /// The crew photo's crew (`dt screenshot --view crewphoto`): four, one dead. Dave waving, Priya sooted from the shovel's
    /// lump (note 717), Okafor (a bot) stood, Dunmore's cap on the crate.
    /// </summary>
    public static IReadOnlyList<Sitter> PhotoCrew() =>
    [
        new(1, "Dave", true, Emote.Wave),
        new(2, "Priya", true, Sooted: true),
        new(3, "Okafor", true, Emote.Point),
        new(4, "Dunmore", false),
    ];

    /// <summary>A crew's logbook, three nights in (`dt screenshot --hud --logbook`): the last delivered, with its photo's file.</summary>
    public static IReadOnlyList<LogPage> LogPages(string photo = "") =>
    [
        new LogPage(1, "Local 4, Hollin Halt to Barrow Bay", "12 October 2026", RunEnd.Delivered, 860)
        {
            Line = "DELIVERED. 3 CARS, 0 LOST. CREW HOME 4",
            Commendations = ["PRIYA: Kept the Fire, from Dave"],
        },
        new LogPage(2, "Frontier 3, Barrow Bay to Moss Settlement", "13 October 2026", RunEnd.Derailed, -420)
        {
            Line = "DERAILED. 4 CARS LOST. CREW HOME 0",
            Losses = ["Consist derailed at km 14, 68 km/h. Took the 45 km/h bend at 68 km/h, 23 km/h too fast. Throttle: Dave."],
        },
        new LogPage(3, "Frontier 7, Moss Settlement to Voss Crossing", "14 October 2026", RunEnd.Delivered, 1450)
        {
            Line = "DELIVERED. 5 CARS, 1 LOST. CREW HOME 3",
            Losses = ["DUNMORE: Mauled by the hounds on the roof of car 3 at km 4. Nearest crew: Dave, 12 m. Fee 350. Body recovered. Refund 263."],
            Commendations = ["DAVE: Came Back For Me, from Okafor", "PRIYA: Kept the Fire, from Dave"],
            Photo = photo,
        },
    ];

    /// <summary>The board of lost crews' cards (`--lost-crews`): two crews this player rode with, both taken at dawn.</summary>
    public static IReadOnlyList<(string Title, string Text)> LostCrews() =>
    [
        Logbook.Card(new CrewLog("The Night Mail#1", "The Night Mail") { Over = true, Pages = [.. LogPages().Take(2), new LogPage(5, "Dead Lines 2", "", RunEnd.DawnMissed, -900)
        {
            Line = "TAKEN AT DAWN. 2 CARS LOST. CREW HOME 0",
            Losses = ["PRIYA: Taken at dawn by a Waker on the line at km 21. At the controls at dawn: Dave."],
        }] }),
        Logbook.Card(new CrewLog("Cold Feet#2", "Cold Feet") { Over = true, Pages = [new LogPage(1, "Local 2", "", RunEnd.DawnMissed, -300)
        {
            Line = "TAKEN AT DAWN. 3 CARS LOST. CREW HOME 0",
            Losses = ["OKAFOR: Taken at dawn by a Waker in the cab at km 19. Nobody at the controls."],
        }] }),
    ];
}
