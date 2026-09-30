# Late Dispatch

An example Dark Territory mod. Dispatch holds the main line ten minutes past dawn: `dawnGraceSeconds` goes from 120 to 720.

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
