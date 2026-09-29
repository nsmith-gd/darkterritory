# Steam's native library

`Ballast.Online` talks to Steam through [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) (MIT, a NuGet package), which is only the managed wrapper.
Valve's own library comes from the Steamworks SDK and has to be put here by hand, once per machine that builds the game.

1. Download the Steamworks SDK **1.60** from <https://partner.steamgames.com/downloads/list> (Steamworks partner login). Steamworks.NET 2024.8.0 is built against 1.60, so match the version.
2. Copy these files from `sdk/redistributable_bin/` into this folder:
   - `win64/steam_api64.dll` (Windows)
   - `linux64/libsteam_api.so` (Linux, optional)
3. Build. `DarkTerritory.App` copies whatever is here next to the game.

The files are Valve's redistributables. They ship with the game but aren't committed to this repository.

Check it with `dt online check` while Steam is running. Until Dark Territory has its own app id, the game uses Valve's test app 480 ("Spacewar"), which any Steam account can use.
