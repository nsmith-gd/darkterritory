# Store uploads

`tools/upload.sh` sends the folders `tools/package.sh` builds to Steam (through `steamcmd`) or itch.io (through `butler`).
CI dry-runs both on every push: it checks the builds, writes the Steam app build and the butler commands to `out/store/`, and uploads nothing.
A real upload is the **Release** workflow (Actions > Release > Run workflow), or the same script on a developer's machine.

```bash
tools/package.sh                                # out/dist/DarkTerritory-win-x64, -linux-x64
tools/upload.sh steam --dry-run                 # out/store/steam/app_build_<app>.vdf and out/store/steam.json
tools/upload.sh steam --preview                 # steamcmd builds the manifests and uploads nothing (needs a login)
tools/upload.sh steam --branch beta             # up to Steam, and live on the beta branch
tools/upload.sh steam --demo --branch beta      # the demo's app instead
tools/upload.sh itch                            # both platforms to their channels
tools/upload.sh itch --demo                     # to windows-demo and linux-demo
```

A Steam build goes live on a beta branch at most. Steam never lets a script set the default branch live, so releasing to players is always a click in Steamworks.
Re-pushing the same build to itch is a no-op (`--if-changed`).

## Once, before the first upload

**Steam:**
1. Create the game's app and the demo's app in Steamworks. Give each a Windows depot and a Linux depot.
2. Put the IDs in `store.conf`.
3. In each app's Installation > General, add two launch options:
   - `DarkTerritory.exe` for Windows;
   - `DarkTerritory` for Linux.
4. Make a Steamworks account just for builds, with only the **Edit App Metadata** and **Publish App Changes To Steam** permissions.
5. Put Valve's `steam_api64.dll` in `external/steam/` before packaging (`external/steam/README.md`). A real Steam upload refuses a Windows build without it.

**itch.io:**
1. Create the project page.
2. Put `user/game` in `store.conf` as `ITCH_TARGET`.
3. Make an API key under Settings > API keys.

## Credentials

The scripts read these from the environment, and the Release workflow reads them from the repository's secrets. None of them go in the repository.

| Name | Store | What |
|---|---|---|
| `STEAM_USERNAME` | Steam | the build account |
| `STEAM_CONFIG_VDF` | Steam, CI only | base64 of `config/config.vdf` from a machine where the build account has logged in to `steamcmd` once. This is how CI gets past Steam Guard without a code. |
| `STEAM_REDIST_URL` | Steam, CI only | a private link to a zip of Valve's redistributables (`steam_api64.dll`, `libsteam_api.so`). The Release workflow puts them in `external/steam/` before packaging. |
| `BUTLER_API_KEY` | itch | the API key. Running `butler login` does the same on a developer's machine. |

`STEAMCMD` and `BUTLER` point the script at the tools when they aren't on `PATH`.
