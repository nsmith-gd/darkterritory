# Prompt for the audio chat (AU1)

You're AU1, the audio session on Dark Territory (nsmith-gd/darkterritory). Before anything else, read `CLAUDE.md` and `docs/COORDINATION.md`, then claim a queue number and a note number the usual way and mark your Audio Checklist rows.

K1's friendslop work (GDD App. F.6, `docs/design/friendslop.md`) is all on main. You've already given most of it a voice: horseplay (#1012), the kettle, cuckoo, shutter and logbook (#1025), and the sense table, bringing them home, big finds, the handcar and the sound cues in the world (#1040). Thank you. Three features still play stand-ins or silence. They're the last of it:

1. **Emotes for two** (queue #456, note 722; `docs/notes/722.md` "Sounds"). The high five's slap and the fist bump's knock, each played once per pair at the hands where they meet. Right now they borrow `crew-mishaps.bare-slap` and `crew-mishaps.body-knock`. Also a settle (cloth and boots) when someone sits on an edge, and a creak of the rail or door when someone leans out of a car. The sim already says when: `Crewmate.EmoteWith` and the emote record.
2. **Odd outcomes** (queue #464, note 730; `docs/notes/730.md` "Sounds"). `GameAudio.Oddities.cs` already hooks each sound by id and plays it once:
   - `place-town.bell`: a cracked council bell.
   - `world-odd.well-splash`: a small thing dropping into deep water, far down.
   - `world-odd.coin`: a coin landing on stone.
   - `world-odd.wash`: a bucket drawn up and poured.
   - `world-odd.can-kick`: a tin can kicked down a road.
   - `world-odd.bark`: a dog, or something like one, far off in the dark.
   - `world-odd.scarecrow`: a post turning with a creak, far quieter than anything else.
   - `world-odd.letter`: an envelope torn open.
   - `world-odd.painting`.
   - `world-odd.sooted`: a cough.
   - `world-odd.lurch`.

   The stand-ins are listed in the note. Replace them.
3. **A friend on the crane's hook** (queue #460, note 726; and note 802, which makes the drop a gag). The cable creaks under a rider (`Crane.Riders` > 0, louder as the hook moves). A short yelp plays when the operator shakes them off (the `Hoisted` flag goes off while a shake-off is recorded). On 15 Oct the director made the drop "a silly gag, not a big grief": at most 10 health, scaled by height. So the landing wants a comic thump-and-oof, not a death thud. Note 802 says how the sim marks that landing.

**No longer needed:** the mail. The director cut it on 15 Oct (queue #530, note 803). The bag snatched or torn at a lineside crane (`Lineside.Caught`/`Torn`, your #504) won't be heard while the mail is off. Leave the sounds installed (the mail is kept behind one switch) and mark the rows as parked, not dropped.

The usual rules apply: tells must stay readable (`dt audio render --listener all`; look at the spectrograms), keep `AudioTests` green, write candidates, captions and provenance, and update `dt credits --write` if new packs come in. Open one PR. Say what's installed against what stays a stand-in. Log it in `docs/log/AU1.md`.

---

# Prompt for the animation chat (E1, Art & Animation 3)

You're E1, the third art and animation session on Dark Territory (nsmith-gd/darkterritory). Before anything else, read `CLAUDE.md` and `docs/COORDINATION.md`. Claim a queue number and a note number. Then mark the Art Checklist lines you take (claude.ai/artifact/7MnAAHwaVRtNosBH7hRLNu): put `assignee`, `queue` and `assignedAt` on each line's `items` row. C1 owns the checklist and the Look Review, so ask on C1's PR before you touch a line someone else holds.

K1's friendslop work (GDD App. F.6, `docs/design/friendslop.md`) is all on main. Its two-person moments are where a crew of friends laughs, and right now they're carried by stand-in poses. Making them read is the job. Each note's "Art"/"Clips" line says what stands in today:

1. **Emotes for two** (note 722). New crew clips named `high_five`, `fist_bump`, `dance_off`, `sit_edge` and `lean_out`, replacing the IK poses (`CreatureArt.EmoteArms`, `SitOnEdge`, `CrewPose.HighFive/FistBump/DanceOff/SitEdge/LeanOut`). The two crewmates' hands must actually meet. The dance-off is two people taking turns. The sit dangles the legs off a car's edge or roof. The lean hangs out of an open side door with one hand on the frame. Views: `highfive`, `danceoff`, `sitedge`, `leanout`.
2. **The shove** (note 717). A shove clip of its own, with weight in it, to replace the gun's push clip (`CrewActs.Shoving`, 0.45 s). View: `shove`.
3. **Bringing them home** (note 723).
   - A proper fireman's carry, for both the carrier and the carried. Today it's the body-carry's shoulder clip plus the pinned clip (`CrewActs.OnTheShoulder`).
   - A reach-down hand-up from a car's step. Today the Dragger rescue's haul stands in.

   Views: `carryfriend`, `handup`.
4. **A friend on the crane's hook** (note 726, note 802).
   - A pose for standing on the hook with both hands up the cable, a little sway, and a flail when they let go or are shaken off. Today it's the lever pose (`CrewActs.OnTheHook`).
   - The hook swinging under a rider's weight.

   View: `hoist` (and `--view hoist --operator --hook 4`).
5. **The handcar** (note 729).
   - A pumping clip with the hands on the grip bar, in time with the sim's handle swing (`CrewActs.AtTheHandle`). Today it's the lever pose.
   - If you take it: a modelled handcar (plank deck, iron, four turning wheels, the hung trolley's chains) to replace the greybox boxes in `GreyboxScene.Handcar.cs`.

   Views: `handcar`, `handcarlift`.

The tools:
- `tools/blender/crew_clips.py` (it already has #967's bow, salute and conga to follow).
- `dt art clip`, `dt art reel --only crew`, and `dt art clearance --only crew` (no limbs through bodies; `CreatureArtTests` pins the crew).
- Render each view and read the PNGs before you call it done.

Clips are presentation only: don't change the sim's timings or reaches. If a clip needs the sim to say something it doesn't yet, name it in your note rather than change the sim. A PR per batch is fine (emotes first; they're the most visible). Update each note's "left for later". Log it in `docs/log/E1.md`.
