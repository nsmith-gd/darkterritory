# Late Dispatch

An example Dark Territory mod: the old dawn, held late. In the game, the Wakers rise at dawn and come down the line after the train. This mod leaves them asleep (`wakers.enabled: false`, in `content/tuning/enemies.json`), so dawn is the main line going live again, as it was before them, and dispatch holds it twelve minutes past dawn instead of two (`dawnGraceSeconds` 120 → 720, in `content/tuning/run.json`). A little more time; when it runs out, the night is lost ("Dawn missed").

## Making your own

A Dark Territory mod is a Thunderstore package:

- `manifest.json`: `name` (letters, digits, underscores), `version_number` (`1.0.0`), `website_url`, `description` (250 characters at most), and `dependencies`, written as `Namespace-Name-1.0.0`. Mods you depend on load before yours.
- `README.md`: this page.
- `icon.png`: 256×256.
- `content/`: files laid over the game's `content/` folder at the same paths.
  - A new path adds a file; the same path replaces the game's.
  - A JSON file with `"$patch": true` is merged into the game's key by key, so you only write what you change (arrays are replaced whole).

`dt mods pack <your folder>` checks it against Thunderstore's rules and writes the zip to upload. `dt mods` lists what's installed, the order it loads in, and anything that can't load (a missing dependency).

To install by hand, drop the zip, or the unzipped folder, into `mods/` beside the game. Mod managers such as r2modman start the game with `--mods-dir <profile folder>`.
