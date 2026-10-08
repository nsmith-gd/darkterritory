# The townsfolk: who they are and what they're called

The personality matrix for the fortress towns' people (GDD §3.1; ARCHITECTURE §8 note 474). The numbers are
`content/tuning/townsfolk.json`, the words `content/world/townsfolk.json`, the code `Sim/Towns/TownFolk.cs`.
`dt town` shows each person's traits, temperament, people and generation; `dt town sweep` shows each custom's mean
traits, its temperaments' shares and how many have a byname.

## What the world asks of a personality

The towns are Maritime railway towns walled in after the fall. Every town lost people to one creature, kept the thing that
saved the rest, and keeps it harder every year (GDD §2: "the Corruption exaggerates whatever allows something to survive";
the town did the same). The crew learn the creatures' rules by inference from what townspeople say, never the rule whole.
The director (8 Oct) asked for "what becomes of those who rarely leave the walls" and people "trying to find ways of making
the world feel tolerable".

So the traits are not the generic ones (openness, conscientiousness...). Each is a way of carrying that town's grief, and
each changes something the crew can notice:

| Trait | −1 | +1 | What it changes |
|---|---|---|---|
| **keeping** | lapsed: keeps the custom from habit, half believes it | zealous: keeps it harder than it needs keeping | faithful and doubter temperaments; a zealous house names its children Patience and Mercy |
| **telling** | close: won't speak of what happened | open: says more of the rule than they should | close people never say the custom's lines and say the fewest lines; open people say the most. **The hint economy:** who to ask |
| **nerve** | frayed | steady | frayed and watcher temperaments |
| **grief** | sealed: won't speak of the lost | raw: talks to them still | mourner and hollow; a raw house names its child after the one it lost |
| **welcome** | wary of anyone from away | warm to the crew | watcher and comforter |
| **hope** | resigned | making the world tolerable | mender, restless and hollow; a hopeful house names its children Dawn and June |

## Where a person's traits come from

Each trait is a sum, clamped to [−1, 1], each part on its own seeded stream (the same on every machine):

1. **The custom's lean** (`cultures`): what each grief made of its people. The Passenger's town (everyone answers to their
   name at supper) is wary of strangers, welcome −0.5. The Gaunt's town (somebody's always talking) is open, telling +0.5.
   The Choir's town (no singing) is close-mouthed, telling −0.35. The Track Doll's (every toy on the shelf by the gate)
   grieves, grief +0.3.
2. **The town's mood** (`townSpread`), so two towns of one custom aren't alike.
3. **The household's mood** (`householdSpread`), so families resemble; and a household that lost somebody grieves
   (`absent`).
4. **The job** (`roles`) or **the part in the house** (`parts`). The keeper of the custom's hall is its most zealous. The
   widow's grief is raw. The fitter and the old driver are lapsed, from years on the line. The cook is warm. The elder keeps
   it hardest. A child blurts (telling +0.4) and still hopes.
5. **Their own draw** (`spread`): three uniforms summed, so most people are near their town's middle and a few are far out.

## Temperaments

A temperament is a direction in the six-trait space. Somebody is the temperament their traits lie furthest along (the dot
product over the direction's length), or **plain** when none passes `temperamentFloor`. Each temperament has its bynames and
a deck of lines. A person says one of these second, after the gate's law or a household's story. The lines say how
somebody carries it, never what the custom is, so they sit in any town.

| Temperament | Direction | Sounds like |
|---|---|---|
| faithful | keeping, a little frayed | "The council says once. I do it three times. Nobody I love has gone since I started." |
| doubter | lapsed, open | "Half of it's grief and half of it's habit. Nobody can tell me which half saves you." |
| mourner | raw, resigned | "I talk to him at the window. Some nights the glass fogs on the other side." |
| hollow | sealed, resigned, wary | "Funny. You stop hoping and you stop hurting about the same time." |
| watcher | wary, steady | "Your boots are wet. It's not rained. Where've you been walking?" |
| comforter | warm, hopeful | "Sit a minute. There's tea. It's mostly tea." |
| talker | open, warm | "I'm not supposed to say. So I'll say it quiet..." |
| frayed | no nerve | "Talk to me. Keep talking. When people stop talking is when I hear it." |
| mender | hopeful, keeps the law | "You keep the law, and then you find a little room inside it. That's where you live." |
| restless | hopeful, lapsed, steady | "One day I'll walk out that gate and keep walking till I see the sea. The real one, not the painted one." |

`dt town sweep --seeds 200`: every custom has its own mix. The Passenger's towns are 31% faithful and 16% watchers. The
Gaunt's are 26% talkers. The Track Doll's are 29% mourners. The Tippy Toesie's hold the most comforters (15%). Doubters are
rare everywhere (1–5%): these towns keep their customs harder every year.

## Names

Names come from the town's people, then from who each person is.

- **Heritages** (`heritages`): the province's peoples. Highland Scots (Gaelic, Cape Breton), Acadian, Irish, Lunenburg
  German and Loyalist English, each with its own given names and surnames.
  - **A town's mix.** One heritage dominates (45–70%) and a second follows (15–30%). The dominant one is the fort's own
    name's 70% of the time (Fort Boudreau is Acadian). Otherwise it is drawn by the trade's leans: a pit town's Highland
    Scots and Irish, a farm's Acadians and Loyalists, a foundry's Lunenburg Germans.
  - **Families.** A household's surname, and so its heritage, is drawn by the mix.
  - **Given names.** Now and then a given name is from another of the town's peoples (Jean-Guy MacNeil).
- **Generations.** Elders (an elder in the house, the old driver) and adults have the old names.
  - **After-names.** A child born inside the walls has an after-name, named by what the house kept, hoped or lost:
    - a zealous house: a virtue (Patience, Constance, Silence);
    - a hopeful one: a daylight name (Dawn, June, Noon);
    - a raw one that lost somebody: the lost one's own name. The Hatfields lost Edna the week she was to be married; their
      youngest is Edna.
    - otherwise a plain short name (Ash, Wren, Jory).
  - **The custom's own names.** Now and then a child has one: a rulebook town has more than one child called Nine; a hush
    town has Silence and Hush; the toy-shelf town has Dolly.
- **Bynames, the Cape Breton way.** The nickname goes between the given name and the surname ("Holy Annie Gillis",
  "Hector the Gate MacNeil", "Angus Dan Rory MacNeil"), about a third of people. Who gets which:
  - Somebody strongly of a temperament is known by it: Black Flora the mourner, Jumpy Seamus, Sweet Maureen, Talking
    Jean-Guy, Doubting Cornelius.
  - Otherwise, often their job's: Lamp Hughie, Steam Archie, Widow Effie, and a pit hand's "Coal Angus".
  - Otherwise, for the Gaels, a patronymic: Angus, son of Dan, son of Rory.
  - The byname is part of the name the HUD shows and the one others use when they talk about somebody ("{name}").

## Not yet

- The talk card reads the same for everyone. A frayed person could type in bursts, a hollow one slowly (presentation only:
  `TownTalk`).
- Rounds don't read the matrix yet (note 353's `TownRounds`). The restless could walk to the gate and look out, a watcher
  could stand at the wall, a mender could tend the lamp garden.
- Households don't argue: a lapsed lodger in a zealous house could say so.
- Townsfolk lines are fixed per town. The crew could hear a different line from someone who's been told "the custom" by
  another townsperson first.
