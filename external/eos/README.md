# Epic Online Services (note 896)

The itch.io build's online play goes through EOS: a device-id sign-in (no Epic account for anyone), lobbies friends reach
by a join code, and P2P that goes straight to the host when NAT allows and through Epic's relays when it doesn't. Free;
no servers of ours. `Ballast.Online/Eos/` holds it: `EosBackend` (the game's side), `FakeEos` (what the tests and
`dt harness --eos` run on), and `EpicSdk`, the binding to Epic's SDK, compiled only when the SDK is here.

## What goes here (never committed: `.gitignore`)

```
external/eos/
  SDK/Source/...                      Epic's C# wrapper, as it comes in the SDK (Core/ and Generated/)
  SDK/Bin/EOSSDK-Win64-Shipping.dll   the native library, Windows
  SDK/Bin/libEOSSDK-Linux-Shipping.so the native library, Linux
  eos.json                            the game's credentials (below), copied beside the game
```

1. Sign in to the Epic Developer Portal (<https://dev.epicgames.com/portal>), open the product, and download the
   **EOS SDK for C#** (the "C# SDK" zip, 1.16 or later) from SDK & Release Notes.
2. Unzip it so that `external/eos/SDK/Source` and `external/eos/SDK/Bin` are as above.
3. Build. `Ballast.Online` compiles the wrapper in and defines `EOS_SDK`; `DarkTerritory.App` copies the native
   library and `eos.json` next to the game.

The SDK's licence (the Epic Online Services SDK licence, under the Epic Developer agreement) lets the game ship Epic's
native libraries with it; it doesn't let us publish the SDK. So it's fetched by whoever builds, as Valve's redistributables
are (`external/steam/README.md`). The Release workflow does the same from a secret: `EOS_SDK_URL`, a private link to that
zip (tools/store/README.md).

## The credentials

From the portal (Product Settings): the **product id**, the **sandbox id** and **deployment id** of the live sandbox,
and a **client** with the "GameClient" policy (its client id and secret). Epic expects the GameClient's id and secret in
every copy of the game; they only say which game this is. They still don't go in this repository: the Release workflow
writes `eos.json` from its secrets, and a developer sets variables instead:

```
DT_EOS_PRODUCT_ID  DT_EOS_SANDBOX_ID  DT_EOS_DEPLOYMENT_ID  DT_EOS_CLIENT_ID  DT_EOS_CLIENT_SECRET
DT_EOS_RELAY       allow (default: direct first, the relays when NAT blocks it) | force (always the relays) | none
```

`eos.json` is the same, as `{ "productId": …, "sandboxId": …, "deploymentId": …, "clientId": …, "clientSecret": …, "relay": "allow" }`.

## Checking it

`dt online check` says whether EOS signs in from this machine. The app, without Steam: `--eos` hosts on it, and the
status line shows the lobby's join code; a friend types it where an address goes on the join screen (or `--join K7Q-M2X`).
