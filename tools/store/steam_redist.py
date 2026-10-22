#!/usr/bin/env python3
"""Valve's Steam library into external/steam/: from the STEAM_REDIST_URL secret, else Steamworks.NET's release (note 976).

Tonight's build (.github/workflows/tonight.yml) runs it before packaging, so the director's playtest builds carry
`steam_api64.dll` (Windows) and `libsteam_api.so` (Linux) and a Steam friend night needs nothing copied by hand (queue #269's
first real Steam evening). DarkTerritory.App copies whatever is in external/steam/ beside the game (external/steam/README.md).

The secret is a private link to Valve's redistributables: those two files alone, or the Steamworks SDK's zip whole, where
the 64-bit ones are taken (`win64/`, `linux64/`; the SDK has a 32-bit `libsteam_api.so` too). Without the secret it takes
them from the public Steamworks.NET 2024.8.0 release (the wrapper the game builds against, `Ballast.Online.csproj`), whose
Standalone zip carries Valve's SDK 1.60 builds of both (`Windows-x64/`, `OSX-Linux-x64/`); the zip is pinned by its
SHA-256, so a changed or tampered file is refused. When neither can be had it does nothing and says so, with what to set
and where: a build without it still plays (LAN, direct IP, EOS).

    STEAM_REDIST_URL=... python3 tools/store/steam_redist.py [--out external/steam] [--plan out/tonight/TEST-PLAN.md]
    python3 tools/store/steam_redist.py --offline-test

`--plan` adds the plan's "A Steam night" section (whether this build has the library, and how to run a friend night);
the same section goes to the run's step summary when there is one ($GITHUB_STEP_SUMMARY).
"""

import argparse
import hashlib
import io
import os
import sys
import tempfile
import urllib.request
import zipfile

# Steamworks.NET's release (MIT wrapper; the zip also carries Valve's redistributable libraries, which ship with games).
# Its version matches Ballast.Online's Steamworks.NET package, built against Steamworks SDK 1.60 (external/steam/README.md).
PUBLIC_URL = ("https://github.com/rlabrecque/Steamworks.NET/releases/download/2024.8.0/"
              "Steamworks.NET-Standalone_2024.8.0.zip")
PUBLIC_SHA256 = "08094d1372c6d793d6b5b87c59f1362a10fe8580acb1db6bfaf081a511f6758c"

# The library each platform's game loads, and the SDK folder its 64-bit build is in.
WANTED = {"steam_api64.dll": "win64", "libsteam_api.so": "linux64"}

SECRET_HELP = (
    "To add it: the repository's **Settings > Secrets and variables > Actions > New repository secret**, named "
    "`STEAM_REDIST_URL`, a private link to a zip with Valve's `steam_api64.dll` and `libsteam_api.so` (from the Steamworks "
    "SDK 1.60's `sdk/redistributable_bin/win64` and `linux64`, partner.steamgames.com/downloads/list; the SDK's own zip "
    "works too). The next build has them. Until then, a Steam night needs `steam_api64.dll` copied beside the game by hand "
    "(external/steam/README.md)."
)

FRIEND_NIGHT = (
    "**A Steam friend night** (#269): Steam running and signed in on both machines, the two accounts friends. The host "
    "starts the game with `--steam` (it hosts a Steam lobby; the game is Valve's test app 480, Spacewar, until it has its "
    "own) and, in the night, presses **F2** to invite. The friend starts the game with `--steam` too (the game only starts "
    "Steam when asked; their own night is left for the host's) and accepts in Steam's overlay, or picks **Join Game** on "
    "the host in the friends list. If neither brings them in: the host's console prints `Steam lobby <id>`, and the friend "
    "starts the game with `--join-lobby <id>` instead."
)


def pick(names):
    """Which member of the zip each wanted library comes from: by its name, the 64-bit folder's when there are several."""
    chosen = {}
    for want, folder in WANTED.items():
        found = [n for n in names if not n.endswith("/") and n.rsplit("/", 1)[-1] == want]
        if len(found) > 1:
            found = [n for n in found if f"/{folder}/" in f"/{n}"] or found[:0]
        if len(found) == 1:
            chosen[want] = found[0]
    return chosen


def extract(data, out):
    """Puts the libraries from the zip's bytes in `out`. Returns the names put there."""
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        chosen = pick(z.namelist())
        os.makedirs(out, exist_ok=True)
        for want, member in chosen.items():
            with z.open(member) as src, open(os.path.join(out, want), "wb") as dst:
                dst.write(src.read())
    return sorted(chosen)


def section(have, why=None, source="the zip at `STEAM_REDIST_URL`"):
    """The plan's "A Steam night" section: whether the build has the library, and how to run a friend night."""
    t = ["## A Steam night", ""]
    if len(have) == len(WANTED):
        t.append("Valve's Steam library is in both builds (`steam_api64.dll` for Windows, `libsteam_api.so` for Linux), "
                 f"from {source}.")
    elif have:
        missing = sorted(set(WANTED) - set(have))
        t.append(f"Only {', '.join(f'`{n}`' for n in have)} is in the builds: {source} has no "
                 f"{', '.join(f'`{n}`' for n in missing)} (or more than one and none in a 64-bit folder).")
    else:
        t.append(f"Valve's Steam library is **not** in these builds: {why}. {SECRET_HELP}")
    t += ["", FRIEND_NIGHT, ""]
    return "\n".join(t)


def fetch(url):
    with urllib.request.urlopen(url, timeout=120) as r:
        return r.read()


def run(url, out, plan=None, summary=None, get=fetch, public=(PUBLIC_URL, PUBLIC_SHA256)):
    have, why = [], None
    if url:
        source = "the zip at `STEAM_REDIST_URL`"
        try:
            have = extract(get(url), out)
            if not have:
                why = "the zip at `STEAM_REDIST_URL` has neither library in it"
        except Exception as e:  # a bad link or a bad zip skips the library, never the build
            why = f"the zip at `STEAM_REDIST_URL` couldn't be read ({type(e).__name__})"
    else:
        # No secret: Steamworks.NET's public release, refused unless it is byte for byte the zip pinned here.
        public_url, public_sha = public
        source = "Steamworks.NET 2024.8.0's release (no `STEAM_REDIST_URL` secret)"
        try:
            data = get(public_url)
            if hashlib.sha256(data).hexdigest() != public_sha:
                why = "Steamworks.NET's release zip isn't the one pinned in tools/store/steam_redist.py (its SHA-256 differs)"
            else:
                have = extract(data, out)
                if not have:
                    why = "Steamworks.NET's release zip has neither library in it"
        except Exception as e:  # no network, a moved file: the build goes on without it
            why = f"Steamworks.NET's release couldn't be fetched ({type(e).__name__}), and there's no `STEAM_REDIST_URL` secret"
    text = section(have, why, source)
    for path in (plan, summary):
        if path:
            with open(path, "a") as f:
                f.write("\n" + text)
    print(f"steam: {', '.join(have) if have else 'none'} in {out}" + (f" ({why})" if why and not have else ""))
    if why and not have and os.environ.get("GITHUB_ACTIONS"):
        print(f"::warning::Valve's Steam library isn't in this build: {why}")
    return have


# ---- offline ------------------------------------------------------------------------------------------------------------

def offline_test():
    failures = []

    def expect(ok, what):
        print(("ok    " if ok else "FAIL  ") + what)
        if not ok:
            failures.append(what)

    def zipped(names):
        b = io.BytesIO()
        with zipfile.ZipFile(b, "w") as z:
            for n in names:
                z.writestr(n, n)
        return b.getvalue()

    sdk = ["sdk/redistributable_bin/steam_api.dll", "sdk/redistributable_bin/win64/steam_api64.dll",
           "sdk/redistributable_bin/linux32/libsteam_api.so", "sdk/redistributable_bin/linux64/libsteam_api.so",
           "sdk/redistributable_bin/osx/libsteam_api.dylib", "sdk/public/steam/lib/win64/sdkencryptedappticket64.dll"]
    with tempfile.TemporaryDirectory() as tmp:
        out, plan = os.path.join(tmp, "steam"), os.path.join(tmp, "TEST-PLAN.md")
        have = run("https://example.invalid/sdk.zip", out, plan, get=lambda _: zipped(sdk))
        expect(have == ["libsteam_api.so", "steam_api64.dll"], f"the SDK's zip: both libraries ({have})")
        with open(os.path.join(out, "libsteam_api.so")) as f:
            expect(f.read().endswith("linux64/libsteam_api.so"), "the SDK's zip: Linux's from linux64, not linux32")
        with open(plan) as f:
            md = f.read()
        expect("## A Steam night" in md and "in both builds" in md and "`--steam`" in md and "**F2**" in md
               and "--join-lobby" in md, "the plan says it's in, and how to run a friend night")

        out2 = os.path.join(tmp, "flat")
        have = run("u", out2, get=lambda _: zipped(["steam_api64.dll", "libsteam_api.so"]))
        expect(have == ["libsteam_api.so", "steam_api64.dll"], "a zip of the two files alone")

        # No secret: the public release, its layout (Windows-x64/, OSX-Linux-x64/), pinned by its SHA-256.
        swnet = zipped(["Windows-x64/steam_api64.dll", "Windows-x86/steam_api.dll", "OSX-Linux-x64/libsteam_api.so",
                        "OSX-Linux-x64/Steamworks.NET.dll", "Windows-x64/Steamworks.NET.dll"])
        pin = ("https://example.invalid/swnet.zip", hashlib.sha256(swnet).hexdigest())
        plan3 = os.path.join(tmp, "public.md")
        have = run("", os.path.join(tmp, "public"), plan3, get=lambda _: swnet, public=pin)
        with open(plan3) as f:
            md = f.read()
        expect(have == ["libsteam_api.so", "steam_api64.dll"], f"no secret: both from the public release ({have})")
        expect("in both builds" in md and "Steamworks.NET 2024.8.0" in md, "no secret: the plan names where they came from")

        plan5 = os.path.join(tmp, "tampered.md")
        have = run("", os.path.join(tmp, "tampered"), plan5, get=lambda _: swnet + b"x", public=pin)
        with open(plan5) as f:
            md = f.read()
        expect(have == [] and not os.path.exists(os.path.join(tmp, "tampered")) and "SHA-256 differs" in md
               and "New repository secret" in md, "no secret, a zip that isn't the pinned one: refused, and says what to set")

        def offline(_):
            raise OSError("no network")
        plan6 = os.path.join(tmp, "offline.md")
        have = run("", os.path.join(tmp, "offline"), plan6, get=offline, public=pin)
        with open(plan6) as f:
            md = f.read()
        expect(have == [] and "couldn't be fetched (OSError)" in md, "no secret, no network: the build goes on, and says so")

        expect(len(PUBLIC_SHA256) == 64 and PUBLIC_URL.endswith("Steamworks.NET-Standalone_2024.8.0.zip"),
               "the public release is pinned to 2024.8.0 by its SHA-256")

        def broken(_):
            raise OSError("404")
        plan4 = os.path.join(tmp, "broken.md")
        have = run("https://example.invalid/gone.zip", os.path.join(tmp, "broken"), plan4, get=broken)
        with open(plan4) as f:
            md = f.read()
        expect(have == [] and "couldn't be read (OSError)" in md, "a broken link skips the library, and says so")

        have = run("u", os.path.join(tmp, "win"), get=lambda _: zipped(["redist/steam_api64.dll"]))
        expect(have == ["steam_api64.dll"], "a zip with Windows' alone")

    print(f"\n{len(failures)} failed" if failures else "\nall passed")
    return 1 if failures else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--out", default="external/steam")
    ap.add_argument("--plan", default=None, help="a test plan to add the \"A Steam night\" section to")
    ap.add_argument("--offline-test", action="store_true")
    a = ap.parse_args()
    if a.offline_test:
        return offline_test()
    run(os.environ.get("STEAM_REDIST_URL", "").strip(), a.out, a.plan, os.environ.get("GITHUB_STEP_SUMMARY"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
