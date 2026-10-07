# tools/audio: building the game's sounds

Every sound the game triggers is a cue (cues.py): one thing a player or the sim does, a one-shot with a few takes or a
seamless loop, split again by the surface it touches. Candidates for each cue are built here, judged by the director on
the Dark Territory Audio Checklist (https://claude.ai/artifact/F5szjdzd8Svn3nfH3mDWMN) with Keep/Redo, and the chosen
ones are installed into the game as Opus takes and sound definitions.

## The loop

```bash
python3 tools/audio/fetch.py packs                  # sources into out/audio/src (Drive; see sources.json)
python3 tools/audio/fetch.py index                  # the Sonniss bundles' file lists (range requests, nothing unzipped)
python3 tools/audio/fetch.py pick picks.txt         # pull only the Sonniss files a recipe uses
python3 tools/audio/build.py [line | line.cue]      # build candidates: takes, a preview each, a manifest entry
python3 tools/audio/look.py out/audio/takes/<line>/<cue>/<key>_00.wav --band 2000 5000   # look at one (PNG)
python3 tools/audio/assets.py pending               # previews to upload to the page's asset store (Artifact tool)
python3 tools/audio/assets.py record < result.txt   # remember the asset ids the upload returned (assets.json)
python3 tools/audio/cues.py --store NEW --from DIR  # the store's cues, built candidates joined, verdicts carried over
python3 tools/audio/storesync.py changed NEW DIR    # which lines to write back (pinned to the versions just read)
python3 tools/audio/install.py --from DIR           # kept (or, on L0 lines, first) candidates -> content/audio
python3 tools/audio/swaplevel.py                    # the SWAPS' takes against the synths they replaced -> SWAP_GAIN_DB
dotnet run --project src/DarkTerritory.Cli -- audio render --scenario bend [--listener 0|n]   # a bend taken too fast, by ear
```

DIR is the store's `items` as an ArtifactData list saves them with `out_dir`.

## Files

- `cues.py` the cue list, per checklist line. `MATERIALS` are the surfaces.
- `build.py` runs recipes. `recipes/*.py` are the candidates, one module per family: `choir.py`, `tells.py` and the
  creature modules (enemies, kitbashed and organic), `crew_*.py` (foley), `world_*.py` (the train, the world, places).
- `dsp.py` (filters, pitch and time, rooms, levels, measuring), `synth.py` (throats, bubbles, chitin, creaks, fire,
  reeds), `recipes/kit.py` (creature parts), `src.py` (sources by key), `look.py` (spectrograms).
- `assets.py` + `assets.json` the page's asset store: which preview went up as which asset.
- `install.py` writes `content/audio/samples/<line>/<cue>[/<surface>]/NN.opus`, `content/audio/sounds/<line>.<cue>.json`
  and `content/audio/samples/index.json` (provenance and licence of each folder). A cue whose every candidate is
  marked Redo keeps its installed takes but is no longer kept.
- **Tells keep their game names.** The game plays each enemy tell by its own sound (`hound-howl`, `tippy-tiptoe`, ...),
  which the tell audit (AudioTests) measures. `TELL_SOUNDS` in install.py points that sound at its line's kept takes
  (until then, the tuned synth definition saved in `synth-defs/`); `TELL_GAIN_DB` lifts one that sits under the bed.
  Kept takes that are single bursts or steps rather than a loop (`PACED`) are fired again at an uneven pace by
  `GameAudio.Repeat`; a cue split by surface gives `<sound>.<surface>` variants the game picks by what's underfoot.
- **Main's other synth sounds** (the line's warnings, notes 260 and 265; the gun's laying; the cannon's landings; the
  noisy toys; the depot's blast; the stranded ending) are swapped the same way, cue by cue: `SWAPS` in install.py maps
  `line.cue` to the game's sound and the param the game drives on it. `swaplevel.py` renders each synth and its takes
  through the game's mixer and prints the `SWAP_GAIN_DB` that puts the takes at the synth's loudness, which main tuned
  its mix against. A definition that already says the same isn't rewritten, so files tidied by hand keep their notes.
- **The bend's stress** (note 265; build 1121, "telegraph ... that the train is going under stress"): the flange squeal
  from a third of the derailing pull to the board, the creaks, the slack's lurch, the scream climbing with the stress
  (`LAYER_EXTRAS`' rate on "stress"), the cab's bell. `dt audio render --scenario bend` runs a 6-car train onto
  deepTerritory:2's first bend that can derail it at its board and on past its derailing speed, and reports each of
  those a second at a time: read it before changing any of them (`recipes/stress.py` has the squeal and scream that
  were made for it).
- **The voice booth** (queue #42; https://claude.ai/artifact/3VvuCkMwP3hJ6iDb1PZaoe): the director records lines where a
  real voice beats a stand-in (the prisoners, the Soot Children's call, the clerk, the crew's breath, creature sources)
  and `booth.py make LINE TAKE --out DIR` disguises each take so it isn't the director's voice (another throat: formants
  moved with the pitch), a few versions a take, for Keep or Redo on the page. The page's source and its draft script are
  in `booth/`.
- `CUE_DEF` (install.py) overrides one cue's tier or range where it isn't like its line (the low-steam whistle is still
  the train's whistle, tier 1; the startled livestock are the world's); `FIRST_CHOICE` names the candidate that plays
  while nothing's kept, where the director's brief already says which (the tunnel's bonk).
- **The crew's mishaps** (`crew-mishaps`, `recipes/mishaps_body.py` and `mishaps_world.py`): the funny ones, by the
  director's call (3 Oct). Real sounds with comic timing where the crew does it to themselves or the world shrugs; never
  on a tell, a lure, an alarm or a monster's kill.
- `levels.py` where each line stands in the game: installed and hooked (L1), cue by cue. A cue whose `need` starts
  "Not in the game yet" waits on an action the sim doesn't have, and doesn't count.
- Voices are stand-ins from Piper's LibriTTS model (CC BY 4.0, credit "LibriTTS, Zen et al. 2019") in out/audio/tts:
  the prisoners (`recipes/voices.py`) and the Soot Children's call, made a child's size (`recipes/children.py`). Get it
  with `pip install piper-tts` and the model from Piper's own release (Hugging Face is blocked from cloud sessions):
  `curl -L https://github.com/rhasspy/piper/releases/download/v0.0.2/voice-en-us-libritts-high.tar.gz | tar xz -C out/audio/tts`.
- `clerk.py` the yard's voice on the radio (note 240): the clerk's vocabulary, a take per phrase and word, into
  `content/audio/samples/voice-clerk/bank` (with `bank.json`), which the game strings into lines. Not a checklist pick:
  `--speaker N` rebuilds it in the voice the director keeps; `--previews DIR` reads a sample in each candidate's voice.

## Sources and licences

The four small packs (Kenney impact, RPG and UI audio; sfx_100 v2) are CC0. The Sonniss GDC 2026 bundles are
royalty-free for use in the game but not CC0: they can be built into the game's sounds, and the checklist page only
ever gets trimmed, processed previews of them, never the originals. Sources are never committed (out/ is ignored).
