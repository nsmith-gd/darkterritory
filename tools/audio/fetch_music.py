#!/usr/bin/env python3
"""The derailment's opera, for real (GDD v1.4 App. E.6; ARCHITECTURE §8 notes 174, 194): CC0 recordings of public-domain works.

    python3 tools/audio/fetch_music.py --dry-run         # check the candidate list below, offline
    python3 tools/audio/fetch_music.py --offline-test    # the cut/normalise/encode/manifest path on a made-up recording (needs ffmpeg, dotnet)
    python3 tools/audio/fetch_music.py --search          # what Commons has for each work, with each file's own licence (no downloads)
    python3 tools/audio/fetch_music.py                   # the intake: fetch, check, cut, normalise, encode, write the manifest

E.6's one rule: a CC0 1.0 recording of a public-domain composition. A public-domain *work* doesn't make a recording public
domain, and a "public domain" mark on an old recording isn't enough (it depends on the country, and the game sells
worldwide). So a file is kept only if its own Commons page dedicates it under CC0 1.0: a {{cc-zero}} template in the page's
wikitext, read through the API, with the licence metadata alongside. A PD-old, PD-US, PD-USGov or "public domain mark"
file is refused, however old the recording. So are renderings of MIDI or notation software: E.6 wants singers and players.

For every kept file the intake saves E.6's evidence: the licence metadata and the page's wikitext as fetched, the page as
rendered (the archived licence page), and the SHA-256 of the file as downloaded, in intake/_sources/music/<id>/ (gitignored;
the workflow uploads it as an artifact), with the licence part committed beside the music in
content/audio/music/evidence/<id>.json. Then it finds the hit (the climax the candidate's hint describes: the strongest
onset into the loudest bar within the hint's range), cuts a window around it (up to PRE s before and POST s after; E.6's
sequence needs the hit at least the replay's lead in and the track running to the sequence's end), normalises it to
-16 LUFS (ffmpeg loudnorm, two passes, linear where it can be) and encodes mono Ogg Opus at 48 kHz (the game decodes it with
the managed Opus it already has for voice: Ballast.Audio.OggOpus). Last, `dt audio music` refines each hit onto the sharpest
onset the manifest test looks for, measures the loudness the way the game does (BS.1770), and writes manifest.json and
CREDITS.md. With 4 or more CC0 recordings (E.6's demo bar) they replace the synthesised fallback (`dt audio opera`); a mood
none of them covers keeps its synthesised track. With fewer, the fallback stays and the recordings join it.

The cloud sessions this was written in can't reach Commons (the proxy refuses commons/upload.wikimedia.org, musopen.org,
archive.org and freesound.org), so the intake runs in GitHub Actions: .github/workflows/music-intake.yml.
"""
from __future__ import annotations

import argparse
import array
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ROOT / "intake" / "_sources" / "music"
MUSIC = ROOT / "content" / "audio" / "music"
API = "https://commons.wikimedia.org/w/api.php"
UA = "DarkTerritoryMusicIntake/1.0 (https://github.com/{}; asset licence check) python-urllib".format(
    os.environ.get("GITHUB_REPOSITORY", "darkterritory"))

MOODS = ("lament", "gallop", "doom", "swagger")
# The window around the hit. The game starts a track wreck.json replayLeadSeconds (3) before its hit and needs it to run
# to the end of the sequence (18 s past the hit): MIN_PRE and MIN_POST, with slack for `dt audio music` moving the hit onto
# the sharpest onset (up to REFINE s). PRE is more lead than the build uses, for E.6's planner putting the hit on the
# film's last apex (E.5), which can be most of the film in.
PRE, POST, REFINE = 24.0, 21.0, 1.0
MIN_PRE, MIN_POST = 3.0 + REFINE + 0.5, 18.0 + REFINE + 0.5
LUFS, TRUE_PEAK = -16.0, -1.5
OPUS_KBPS = 64
MAX_TRACKS = 16
# What the intake takes, when it runs: a small request file, so a push of it can start the workflow (music-intake.yml).
REQUEST = ROOT / "tools" / "audio" / "music_intake.json"
# E.6 wants performers: these say a file was rendered by software, not played.
NOT_PLAYED = re.compile(r"\b(midi|synthesi[sz]ed|synth|musescore|sibelius|noteworthy|lilypond|timidity|soundfont|"
                        r"computer[- ]generated|8-?bit|chiptune|vocaloid|text-to-speech)\b", re.I)
CC0 = re.compile(r"\{\{\s*(cc-zero|cc0|cc-0|cc0-1\.0|cc-zero-1\.0)\s*(\||\}\})", re.I)
# A rights holder's own public-domain release of the recording ({{PD-self}} and the like) is a dedication, but not CC0 1.0,
# which is all E.6 (and MusicManifestTests) accept: the report lists such files as near misses, with the template, for a
# person to decide on (a CC0 dedication from the performer would let them in).
DEDICATION = re.compile(r"\{\{\s*(pd-self|pd-author|pd-release|pd-musopen|musopen)\s*(\||\}\})", re.I)
# Seen beside a CC0 tag these are fine (the composition's own status); alone they're a refusal (the recording isn't CC0).
PD_ONLY = re.compile(r"\{\{\s*(pd-|public domain|pdm|pd mark)", re.I)

# ---- The candidates. Every work is a public-domain composition; each still needs a CC0 recording, which the API decides.
# titles: Commons files to try first (well-known uploads; a title that's missing or not CC0 is skipped and logged).
# search: what to ask Commons for when none of the titles is CC0; match: patterns every found title must contain.
# hit: where the climax is, as a range in the file: seconds from the start, negative from the end, or with "fraction"
# as fractions of the length (recordings differ, so a range the analysis searches, not a timestamp). note: what it is.
# e6: in E.6's candidate table (preferred); the others are public-domain works in the same moods.
CANDIDATES: list[dict] = [
    # Lament: a slow tip-over's tragedy.
    dict(id="lament-vesti-la-giubba", work="\"Vesti la giubba\" (\"Ridi, Pagliaccio\"), Pagliacci", composer="Ruggero Leoncavallo",
         year=1892, mood="lament", e6=True,
         titles=["File:Vesti la giubba.ogg", "File:Leoncavallo - Pagliacci - Vesti la giubba.ogg", "File:Ruggero Leoncavallo - Vesti la giubba.ogg"],
         search=["\"Vesti la giubba\"", "Pagliacci Leoncavallo aria"], match=["giubba"],
         hit=dict(fraction=[0.45, 0.82], note="\"Ridi, Pagliaccio\": the tenor's top note on \"Ridi\" with the orchestra's tutti under it, before the sob")),
    dict(id="lament-o-mio-babbino-caro", work="\"O mio babbino caro\", Gianni Schicchi", composer="Giacomo Puccini", year=1918,
         mood="lament", e6=True,
         titles=["File:O mio babbino caro.ogg", "File:Puccini - O mio babbino caro.ogg", "File:Giacomo Puccini - O mio babbino caro.ogg"],
         search=["\"O mio babbino caro\"", "Gianni Schicchi Puccini"], match=["babbino"],
         hit=dict(fraction=[0.50, 0.85], note="\"Mi struggo e mi tormento! O Dio, vorrei morir!\": the soprano's high A-flat")),
    dict(id="lament-flower-duet", work="\"Flower Duet\" (\"Sous le dôme épais\"), Lakmé", composer="Léo Delibes", year=1883,
         mood="lament", e6=True,
         titles=["File:Flower Duet.ogg", "File:Delibes - Lakmé - Flower Duet.ogg", "File:Lakme Flower Duet.ogg"],
         search=["Lakmé \"Flower Duet\"", "Lakme duo des fleurs", "\"Sous le dôme épais\""], match=["lakm|flower|fleurs|dome"],
         hit=dict(fraction=[0.55, 0.85], note="the reprise, the two voices together at the top of the line")),
    dict(id="lament-nessun-dorma", work="\"Nessun dorma\", Turandot", composer="Giacomo Puccini", year=1926, mood="lament", e6=True,
         titles=["File:Nessun dorma.ogg", "File:Puccini - Nessun dorma.ogg", "File:Giacomo Puccini - Turandot - Nessun dorma.ogg"],
         search=["\"Nessun dorma\"", "Turandot Puccini Nessun dorma"], match=["nessun|turandot"],
         hit=dict(start=-50, end=-19.5, note="\"Vincerò!\": the tenor's held top B as the orchestra comes in under it")),
    dict(id="lament-lacrimosa", work="\"Lacrimosa\", Requiem in D minor", composer="Wolfgang Amadeus Mozart", year=1791, mood="lament",
         titles=["File:Mozart - Requiem - Lacrimosa.ogg", "File:Wolfgang Amadeus Mozart - Requiem - Lacrimosa.ogg", "File:Lacrimosa.ogg"],
         search=["Mozart Requiem Lacrimosa", "Mozart Lacrymosa"], match=["lacrimosa|lacrymosa"],
         hit=dict(fraction=[0.25, 0.62], note="\"judicandus homo reus\": the chorus's crescendo to the forte")),
    dict(id="lament-funeral-march", work="Marche funèbre, Piano Sonata No. 2", composer="Frédéric Chopin", year=1839, mood="lament",
         titles=["File:Frederic Chopin - Funeral March.ogg", "File:Chopin - Funeral March.ogg", "File:Musopen - Chopin - Sonata No. 2 - 3. Marche funebre.ogg"],
         search=["Chopin \"Funeral March\"", "Chopin \"marche funebre\"", "Chopin Sonata 2 funeral"], match=["chopin"],
         hit=dict(start=40, end=150, note="the march's return at fortissimo, the bass chords crashing in")),
    dict(id="lament-barcarolle", work="Barcarolle (\"Belle nuit, ô nuit d'amour\"), The Tales of Hoffmann", composer="Jacques Offenbach",
         year=1881, mood="lament",
         # CC0 on Commons, as reported to a CI probe.
         titles=["File:Barcarolle - Offenbach.ogg"],
         search=["Offenbach Barcarolle", "\"Belle nuit\" Offenbach"], match=["barcarol"],
         hit=dict(fraction=[0.45, 0.85], note="the barcarolle's swell: the full orchestra (and the two voices) at the top of the tune")),
    # Gallop: fast and loud.
    dict(id="gallop-william-tell", work="Overture finale, William Tell", composer="Gioachino Rossini", year=1829, mood="gallop", e6=True,
         titles=["File:Gioachino Rossini - William Tell Overture.ogg", "File:Rossini - William Tell Overture - Finale.ogg", "File:William Tell Overture.ogg"],
         search=["\"William Tell\" overture Rossini", "\"Guillaume Tell\" ouverture", "Rossini \"William Tell\" finale"], match=["tell"],
         hit=dict(start=-80, end=-20, note="the finale's gallop: the full orchestra's return of the trumpet call, the cymbals on it")),
    dict(id="gallop-infernal-galop", work="\"Infernal Galop\" (the can-can), Orpheus in the Underworld", composer="Jacques Offenbach",
         year=1858, mood="gallop", e6=True,
         # The Musopen recording of the overture (CC0, as Commons reported it to a CI probe): it ends with the can-can.
         titles=["File:Offenbach - Orpheus in the Underworld - Overture.ogg", "File:Jacques Offenbach - Orpheus in the Underworld - Can-can.ogg", "File:Offenbach - Can Can.ogg", "File:Can-can.ogg"],
         search=["Offenbach can-can", "Offenbach \"Orpheus in the Underworld\"", "Offenbach galop infernal"], match=["offenbach|can-can|cancan|orph"],
         hit=dict(start=-55, end=-19.5, note="the can-can's last climb: the whip-crack downbeat and the cymbals")),
    dict(id="gallop-ride-of-the-valkyries", work="\"Ride of the Valkyries\", Die Walküre", composer="Richard Wagner", year=1870,
         mood="gallop", e6=True,
         titles=["File:Richard Wagner - Ride of the Valkyries.ogg", "File:Wagner - Ride of the Valkyries.ogg", "File:Ride of the Valkyries.ogg"],
         search=["\"Ride of the Valkyries\"", "Walkürenritt", "Wagner Walküre ride"], match=["valkyr|walk"],
         hit=dict(start=20, end=110, note="the brass entry: horns and trombones taking the theme over the strings' trills")),
    dict(id="gallop-largo-al-factotum", work="\"Largo al factotum\", The Barber of Seville", composer="Gioachino Rossini", year=1816,
         mood="gallop", e6=True,
         titles=["File:Largo al factotum.ogg", "File:Rossini - Largo al factotum.ogg", "File:Gioachino Rossini - Il barbiere di Siviglia - Largo al factotum.ogg"],
         search=["\"Largo al factotum\"", "Barbiere di Siviglia Figaro aria"], match=["factotum|figaro"],
         hit=dict(fraction=[0.50, 0.82], note="\"Figaro! Figaro! Figaro!\": the baritone's top note on the tutti")),
    dict(id="gallop-mountain-king", work="\"In the Hall of the Mountain King\", Peer Gynt", composer="Edvard Grieg", year=1875, mood="gallop",
         titles=["File:Edvard Grieg - In the Hall of the Mountain King.ogg", "File:Grieg - In the Hall of the Mountain King.ogg", "File:In the Hall of the Mountain King.ogg"],
         search=["\"Hall of the Mountain King\"", "Grieg \"Dovregubbens hall\"", "Grieg Peer Gynt mountain king"], match=["mountain|dovregubb|peer gynt"],
         hit=dict(start=-45, end=-19.5, note="the accelerando's peak: the full orchestra and the cymbal crash")),
    dict(id="gallop-1812-overture", work="\"1812 Overture\"", composer="Pyotr Ilyich Tchaikovsky", year=1880, mood="gallop",
         titles=["File:Tchaikovsky - 1812 Overture.ogg", "File:Pyotr Ilyich Tchaikovsky - 1812 Overture.ogg", "File:1812 Overture.ogg"],
         search=["Tchaikovsky \"1812\" overture", "Tchaikovsky 1812"], match=["1812"],
         hit=dict(start=-120, end=-25, note="the cannon: the first shot into the bells and the brass")),
    # Doom: a fast one, and the bill is due.
    dict(id="doom-dies-irae", work="\"Dies irae\", Messa da Requiem", composer="Giuseppe Verdi", year=1874, mood="doom", e6=True,
         titles=["File:Giuseppe Verdi - Messa da Requiem - Dies irae.ogg", "File:Verdi - Requiem - Dies irae.ogg", "File:Verdi Requiem Dies irae.ogg"],
         search=["Verdi Requiem \"Dies irae\"", "Verdi \"Messa da Requiem\""], match=["verdi"],
         hit=dict(start=4.5, end=40, note="the first bass-drum strikes: the off-beat blows after the opening chords (or their return)")),
    dict(id="doom-der-holle-rache", work="\"Der Hölle Rache\", The Magic Flute", composer="Wolfgang Amadeus Mozart", year=1791, mood="doom",
         e6=True,
         titles=["File:Der Hölle Rache.ogg", "File:Mozart - Der Holle Rache.ogg", "File:Wolfgang Amadeus Mozart - Die Zauberflöte - Der Hölle Rache.ogg"],
         search=["\"Der Hölle Rache\"", "\"Der Holle Rache\"", "Zauberflöte Königin der Nacht"], match=["rache|nacht|queen of the night"],
         hit=dict(fraction=[0.25, 0.70], note="the Queen of the Night's staccato run to the top F")),
    dict(id="doom-dies-irae-mozart", work="\"Dies irae\", Requiem in D minor", composer="Wolfgang Amadeus Mozart", year=1791, mood="doom",
         titles=["File:Mozart - Requiem - Dies irae.ogg", "File:Wolfgang Amadeus Mozart - Requiem - Dies irae.ogg"],
         search=["Mozart Requiem \"Dies irae\""], match=["mozart"],
         hit=dict(start=20, end=80, note="\"Quantus tremor est futurus\": the tutti's return after the basses' line")),
    dict(id="doom-night-on-bald-mountain", work="\"Night on Bald Mountain\"", composer="Modest Mussorgsky", year=1867, mood="doom",
         titles=["File:Modest Mussorgsky - Night on Bald Mountain.ogg", "File:Mussorgsky - Night on Bald Mountain.ogg", "File:Night on Bald Mountain.ogg"],
         search=["\"Night on Bald Mountain\"", "Mussorgsky \"Bald Mountain\""], match=["bald|lysoy|lysaya"],
         hit=dict(start=20, end=160, note="the brass's first great statement over the swirling strings")),
    dict(id="doom-danse-macabre", work="\"Danse macabre\"", composer="Camille Saint-Saëns", year=1874, mood="doom",
         titles=["File:Camille Saint-Saëns - Danse macabre.ogg", "File:Saint-Saens - Danse Macabre.ogg", "File:Danse macabre.ogg"],
         search=["Saint-Saëns \"Danse macabre\"", "\"Danse macabre\" Saint-Saens"], match=["macabre"],
         hit=dict(fraction=[0.55, 0.85], note="the dance's climax: the whole orchestra and the xylophone's bones")),
    dict(id="doom-fifth-symphony", work="Symphony No. 5, first movement", composer="Ludwig van Beethoven", year=1808, mood="doom",
         titles=["File:Ludwig van Beethoven - Symphonie 5 c-moll - 1. Allegro con brio.ogg", "File:Beethoven Symphony 5 movement 1.ogg"],
         search=["Beethoven \"Symphony No. 5\" allegro con brio", "Beethoven Symphonie 5"], match=["beethoven"],
         hit=dict(fraction=[0.55, 0.80], note="the recapitulation: the four-note motif fortissimo in the full orchestra")),
    # Swagger: fits any derailment.
    dict(id="swagger-anvil-chorus", work="\"Anvil Chorus\", Il trovatore", composer="Giuseppe Verdi", year=1853, mood="swagger", e6=True,
         titles=["File:Anvil Chorus.ogg", "File:Verdi - Il trovatore - Anvil Chorus.ogg", "File:Giuseppe Verdi - Il trovatore - Vedi! le fosche notturne spoglie.ogg"],
         search=["\"Anvil Chorus\"", "Trovatore \"Vedi le fosche\"", "Verdi Trovatore coro"], match=["anvil|fosche|trovatore"],
         hit=dict(start=8, end=70, note="the anvils' first strikes under the chorus")),
    dict(id="swagger-la-donna-e-mobile", work="\"La donna è mobile\", Rigoletto", composer="Giuseppe Verdi", year=1851, mood="swagger", e6=True,
         titles=["File:La donna è mobile.ogg", "File:Verdi - Rigoletto - La donna e mobile.ogg", "File:Giuseppe Verdi - La donna è mobile.ogg"],
         search=["\"La donna è mobile\"", "\"La donna e mobile\"", "Rigoletto Verdi Duke"], match=["donna|rigoletto"],
         hit=dict(fraction=[0.25, 0.75], note="the refrain's top note with the orchestra's tutti")),
    dict(id="swagger-toreador", work="\"Toreador Song\" (\"Votre toast\"), Carmen", composer="Georges Bizet", year=1875, mood="swagger", e6=True,
         titles=["File:Toreador Song.ogg", "File:Bizet - Carmen - Toreador Song.ogg", "File:Georges Bizet - Carmen - Votre toast.ogg"],
         search=["\"Toreador\" Bizet Carmen", "Carmen \"Votre toast\""], match=["toread|toast"],
         hit=dict(fraction=[0.30, 0.78], note="\"Toréador, en garde!\": the refrain's downbeat, the chorus joining")),
    dict(id="swagger-habanera", work="\"Habanera\" (\"L'amour est un oiseau rebelle\"), Carmen", composer="Georges Bizet", year=1875,
         mood="swagger", e6=True,
         titles=["File:Habanera.ogg", "File:Bizet - Carmen - Habanera.ogg", "File:Georges Bizet - Carmen - Habanera.ogg"],
         search=["Carmen \"Habanera\"", "\"L'amour est un oiseau rebelle\""], match=["habanera|oiseau"],
         hit=dict(fraction=[0.30, 0.80], note="\"L'amour!\": the chorus's shout into the refrain")),
    dict(id="swagger-carmen-prelude", work="Prelude, Carmen", composer="Georges Bizet", year=1875, mood="swagger",
         titles=["File:Georges Bizet - Carmen - Prelude.ogg", "File:Bizet - Carmen Overture.ogg", "File:Carmen Overture.ogg"],
         search=["Carmen \"Prelude\" Bizet", "Carmen overture Bizet"], match=["carmen"],
         hit=dict(fraction=[0.25, 0.60], note="the opening march's return, cymbals and full orchestra")),
    dict(id="swagger-hallelujah", work="\"Hallelujah\" chorus, Messiah", composer="George Frideric Handel", year=1741, mood="swagger",
         titles=["File:Handel - Messiah - Hallelujah.ogg", "File:George Frideric Handel - Hallelujah.ogg", "File:Hallelujah Chorus.ogg"],
         search=["Handel \"Hallelujah\" Messiah", "\"Hallelujah chorus\""], match=["hallelujah"],
         hit=dict(start=-75, end=-20, note="\"King of Kings\": the trumpets and timpani on the top of the climb")),
    dict(id="swagger-barber-overture", work="Overture, The Barber of Seville", composer="Gioachino Rossini", year=1816, mood="swagger",
         titles=["File:Gioachino Rossini - The Barber of Seville - Overture.ogg", "File:Rossini - Il barbiere di Siviglia - Overture.ogg"],
         search=["\"Barber of Seville\" overture", "\"barbiere di Siviglia\" sinfonia"], match=["barb|siviglia|seville"],
         hit=dict(start=-70, end=-20, note="the crescendo's arrival: the tutti chord on the bass drum")),
]


# ---- Checking the list (offline).

def validate(cands: list[dict]) -> list[str]:
    problems = []
    ids = set()
    for c in cands:
        where = c.get("id", "?")
        for k in ("id", "work", "composer", "year", "mood", "titles", "search", "match", "hit"):
            if k not in c:
                problems.append(f"{where}: no {k}")
        if where in ids:
            problems.append(f"{where}: id twice")
        ids.add(where)
        if not re.fullmatch(r"[a-z0-9]+(-[a-z0-9]+)+", where) or not where.startswith(c.get("mood", "") + "-"):
            problems.append(f"{where}: an id is <mood>-<slug>")
        if c.get("mood") not in MOODS:
            problems.append(f"{where}: mood {c.get('mood')} isn't one of {MOODS}")
        # A public-domain composition: MusicManifestTests holds the year to 1700..1926 (Orff's 1937 "O Fortuna" is out).
        if not isinstance(c.get("year"), int) or not 1700 <= c["year"] <= 1926:
            problems.append(f"{where}: year {c.get('year')} isn't a public-domain composition's (1700-1926)")
        if not c.get("titles") or any(not t.startswith("File:") for t in c["titles"]):
            problems.append(f"{where}: titles are Commons file pages (File:...)")
        if not c.get("search") or not c.get("match"):
            problems.append(f"{where}: needs search queries and match patterns")
        for m in c.get("match", []):
            try:
                re.compile(m)
            except re.error as e:
                problems.append(f"{where}: match {m!r} isn't a pattern ({e})")
        h = c.get("hit", {})
        if not h.get("note"):
            problems.append(f"{where}: the hit needs a note saying what the moment is")
        if "fraction" in h:
            a, b = h["fraction"]
            if not 0 <= a < b <= 1:
                problems.append(f"{where}: hit fraction {h['fraction']} isn't a range in 0..1")
        elif "start" in h and "end" in h:
            if (h["start"] >= 0) == (h["end"] >= 0) and h["start"] >= h["end"]:
                problems.append(f"{where}: hit range {h['start']}..{h['end']} is empty")
            if 0 <= h["start"] < MIN_PRE:
                problems.append(f"{where}: a hit {h['start']} s in can't have {MIN_PRE} s of lead")
            if h["end"] < 0 and -h["end"] < MIN_POST:
                problems.append(f"{where}: a hit {-h['end']} s from the end can't run {MIN_POST} s on")
        else:
            problems.append(f"{where}: the hit is a fraction range or a start..end range")
    for m in MOODS:
        if sum(1 for c in cands if c.get("mood") == m) < 2:
            problems.append(f"mood {m}: fewer than two candidates")
    if len(cands) < 14:
        problems.append(f"{len(cands)} candidates: want more than the 10 E.6 asks for at launch, since some won't be CC0")
    return problems


# ---- Commons.

# Commons rate-limits (a probe from CI got HTTP 429 after about eight searches): a descriptive User-Agent, at least
# PAUSE s between requests to it (the API and upload.wikimedia.org alike), and 429/503 retried with backoff, honouring Retry-After.
PAUSE = 2.5
_last = [0.0]


def fetch(url: str, timeout: int = 60):
    """A response from Wikimedia, politely: paced, and retried when it says slow down."""
    for attempt in range(8):
        wait = PAUSE - (time.monotonic() - _last[0])
        if wait > 0:
            time.sleep(wait)
        _last[0] = time.monotonic()
        try:
            return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=timeout)
        except urllib.error.HTTPError as e:
            if e.code not in (429, 503) or attempt == 7:
                raise
            retry = e.headers.get("Retry-After", "")
            time.sleep(float(retry) if retry.replace(".", "", 1).isdigit() else 10 * (attempt + 1))
        except OSError:
            if attempt >= 3:
                raise
            time.sleep(5 * (attempt + 1))
    raise OSError(f"{url}: still refused")


def api(params: dict) -> dict:
    url = API + "?" + urllib.parse.urlencode({**params, "format": "json", "formatversion": 2, "maxlag": 5})
    for attempt in range(5):
        with fetch(url) as r:
            data = json.load(r)
        if data.get("error", {}).get("code") != "maxlag":
            return data
        time.sleep(5 * (attempt + 1))
    raise OSError("Commons kept saying it's lagged")


INFO = dict(prop="imageinfo|revisions", iiprop="url|extmetadata|sha1|size|mime|mediatype", rvprop="content|ids|timestamp", rvslots="main")


def pages_for(titles: list[str]) -> list[dict]:
    return api({"action": "query", "titles": "|".join(titles), **INFO}).get("query", {}).get("pages", [])


def search_titles(query: str) -> list[str]:
    found = api({"action": "query", "list": "search", "srsearch": f"{query} filetype:audio", "srnamespace": 6, "srlimit": 25})
    return [p["title"] for p in found.get("query", {}).get("search", [])]


def search_pages(query: str, keep=lambda title: True) -> list[dict]:
    """The file pages a search finds, with their wikitext: only those whose titles <keep> passes are fetched (rate limits)."""
    titles = [t for t in search_titles(query) if keep(t)]
    out = []
    for i in range(0, len(titles), 10):
        out += pages_for(titles[i:i + 10])
    return out


def plain(html: str) -> str:
    return re.sub(r"\s+", " ", re.sub(r"<[^>]+>", "", html or "")).strip()


def fold(s: str) -> str:
    return "".join(ch for ch in unicodedata.normalize("NFKD", s.lower()) if not unicodedata.combining(ch))


def judge(page: dict) -> dict:
    """What a file page says about itself: E.6's licence test, and whether it's a recording of players at all."""
    info = (page.get("imageinfo") or [{}])[0]
    meta = info.get("extmetadata", {})
    rev = (page.get("revisions") or [{}])[0]
    wikitext = rev.get("slots", {}).get("main", {}).get("content", "") or rev.get("content", "") or ""
    licence = meta.get("License", {}).get("value", "")
    short = meta.get("LicenseShortName", {}).get("value", "")
    url = meta.get("LicenseUrl", {}).get("value", "")
    tagged = bool(CC0.search(wikitext))
    meta_cc0 = licence.lower() == "cc0" or "publicdomain/zero/1.0" in url or short.strip().upper() in ("CC0", "CC0 1.0")
    description = plain(meta.get("ImageDescription", {}).get("value", ""))
    mime = str(info.get("mime", ""))
    reasons = []
    if page.get("missing"):
        reasons.append("no such file")
    else:
        if not tagged:
            ded = DEDICATION.search(wikitext)
            reasons.append("no CC0 dedication on the page" + (f" (a public-domain release, {{{{{ded.group(1)}}}}}: a near miss)" if ded
                                                               else " (a public-domain tag or mark only)" if PD_ONLY.search(wikitext) else ""))
        if not mime.startswith(("audio/", "application/ogg", "video/ogg", "video/webm")) or "midi" in mime:
            reasons.append(f"not a recording ({mime})")
        if NOT_PLAYED.search(page.get("title", "")) or NOT_PLAYED.search(description) or NOT_PLAYED.search(wikitext[:4000]):
            reasons.append("rendered by software, not performed")
    return {"title": page.get("title"), "cc0": not reasons, "metaSaysCc0": meta_cc0, "licence": licence, "licenceShortName": short,
            "licenceUrl": url, "url": info.get("url"), "descriptionUrl": info.get("descriptionurl"), "sha1": info.get("sha1"),
            "size": info.get("size"), "mime": mime, "artist": plain(meta.get("Artist", {}).get("value", "")),
            "credit": plain(meta.get("Credit", {}).get("value", "")), "description": description[:600],
            "date": plain(meta.get("DateTimeOriginal", {}).get("value", "")), "revision": rev.get("revid"),
            "revisionTimestamp": rev.get("timestamp"), "refused": reasons, "_wikitext": wikitext, "_extmetadata": meta}


def find(c: dict, log: list) -> dict | None:
    """The candidate's recording: the first of its titles that passes, else the biggest search result that does."""
    tried = []
    for j in map(judge, pages_for(c["titles"])):
        tried.append({k: j[k] for k in ("title", "cc0", "licence", "refused")})
        if j["cc0"]:
            log.append({"id": c["id"], "via": "title", "tried": tried})
            return j
    pattern = [re.compile(fold(m)) for m in c["match"]]
    matches = lambda title: all(p.search(fold(title or "")) for p in pattern)
    best = None
    # The candidate's own searches, then Musopen's uploads of the composer's work (many of them dedicated CC0).
    surname = fold(c["composer"]).split()[-1]
    for q in [*c["search"], f"Musopen {surname}"]:
        for j in map(judge, search_pages(q, matches)):
            tried.append({k: j[k] for k in ("title", "cc0", "licence", "refused")})
            if j["cc0"] and (best is None or (j["size"] or 0) > (best["size"] or 0)):
                best = j
        if best:
            break
    log.append({"id": c["id"], "via": "search" if best else None, "tried": tried})
    return best


def download(url: str, to: Path) -> str:
    h = hashlib.sha256()
    with fetch(url, timeout=300) as r, open(to, "wb") as f:
        while chunk := r.read(1 << 20):
            h.update(chunk)
            f.write(chunk)
    return h.hexdigest()


def archive_page(title: str) -> str:
    """The file page as rendered: E.6's "archived copy of the licence page"."""
    return api({"action": "parse", "page": title, "prop": "text", "disablelimitreport": 1}).get("parse", {}).get("text", "")


# ---- Audio (ffmpeg).

def ffmpeg(*args: str) -> subprocess.CompletedProcess:
    return subprocess.run(["ffmpeg", "-hide_banner", "-nostdin", "-y", *args], check=True, capture_output=True, text=True)


def duration(path: Path) -> float:
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)],
                         check=True, capture_output=True, text=True).stdout
    return float(out.strip())


def pcm(path: Path, start: float, length: float, rate: int, filters: str | None = None) -> array.array:
    args = ["ffmpeg", "-hide_banner", "-nostdin", "-v", "error", "-ss", f"{start:.3f}", "-t", f"{length:.3f}", "-i", str(path), "-ac", "1",
            "-ar", str(rate)]
    if filters:
        args += ["-af", filters]
    raw = subprocess.run(args + ["-f", "f32le", "-"], check=True, capture_output=True).stdout
    a = array.array("f")
    a.frombytes(raw[: len(raw) // 4 * 4])
    if sys.byteorder != "little":
        a.byteswap()
    return a


def block_db(a: array.array, block: int) -> list[float]:
    out = []
    for i in range(0, len(a) - block + 1, block):
        s = 0.0
        for x in a[i:i + block]:
            s += x * x
        out.append(10 * math.log10(s / block + 1e-12))
    return out


def hint_range(c: dict, total: float) -> tuple[float, float]:
    h = c["hit"]
    if "fraction" in h:
        lo, hi = h["fraction"][0] * total, h["fraction"][1] * total
    else:
        lo = h["start"] if h["start"] >= 0 else total + h["start"]
        hi = h["end"] if h["end"] >= 0 else total + h["end"]
    return max(lo, MIN_PRE), min(hi, total - MIN_POST)


def find_hit(path: Path, lo: float, hi: float) -> tuple[float, dict]:
    """
    The climax within [lo, hi] s: the 10 ms block with the biggest rise in the bright (over 3 kHz) level against the 300 ms
    before it, plus half of how close the second after it is to the loudest second in the range, so a crash into the
    biggest bar beats a consonant in a quiet one. `dt audio music` then moves it onto the sharpest onset nearby (the
    manifest test's measure).
    """
    rate, block = 16000, 160
    start = max(0.0, lo - 0.5)
    length = hi - start + 1.5
    bright = block_db(pcm(path, start, length, rate, "highpass=f=3000,highpass=f=3000"), block)
    broad = block_db(pcm(path, start, length, rate), block)
    n = min(len(bright), len(broad))
    loud = [sum(broad[i:i + 100]) / max(1, len(broad[i:i + 100])) for i in range(n)]
    first, last = int((lo - start) * 100), int((hi - start) * 100)
    top = max(loud[first:last + 1] or [0])
    best, at = -1e9, first
    for i in range(max(first, 30), min(last, n - 3) + 1):
        onset = sum(bright[i:i + 3]) / 3 - sum(bright[i - 30:i]) / 30
        score = onset + 0.5 * (loud[i] - top)
        if score > best:
            best, at = score, i
    return start + at / 100, {"score": round(best, 2), "range": [round(lo, 2), round(hi, 2)]}


def cut(source: Path, out: Path, start: float, length: float) -> dict:
    """The window, at -16 LUFS (loudnorm's two passes; linear where the true peak allows), mono Opus at 48 kHz."""
    window = ["-ss", f"{start:.3f}", "-t", f"{length:.3f}"]
    measure = ffmpeg(*window, "-i", str(source), "-ac", "1", "-af", f"loudnorm=I={LUFS}:TP={TRUE_PEAK}:LRA=11:print_format=json", "-f", "null", "-")
    stats = json.loads(measure.stderr[measure.stderr.rindex("{"):measure.stderr.rindex("}") + 1])
    norm = (f"loudnorm=I={LUFS}:TP={TRUE_PEAK}:LRA=11:measured_I={stats['input_i']}:measured_TP={stats['input_tp']}:"
            f"measured_LRA={stats['input_lra']}:measured_thresh={stats['input_thresh']}:offset={stats['target_offset']}:linear=true")
    fades = f"afade=t=in:d=0.05,afade=t=out:st={max(0.0, length - 1.0):.3f}:d=1"
    ffmpeg(*window, "-i", str(source), "-ac", "1", "-af", f"{norm},aresample=48000,{fades}", "-ar", "48000", "-c:a", "libopus",
           "-b:a", f"{OPUS_KBPS}k", "-vbr", "on", "-application", "audio", "-map_metadata", "-1", "-fflags", "+bitexact",
           "-flags:a", "+bitexact", str(out))
    return {"inputLufs": float(stats["input_i"]), "inputTruePeak": float(stats["input_tp"]), "normalization": stats.get("normalization_type")}


def take(c: dict, source: Path, out_dir: Path) -> dict:
    """Finds the hit, cuts the window around it, and returns what `dt audio music` finishes."""
    total = duration(source)
    lo, hi = hint_range(c, total)
    if hi <= lo:
        raise ValueError(f"{total:.1f} s is too short for a hit with {MIN_PRE} s before and {MIN_POST} s after in its range")
    hit, why = find_hit(source, lo, hi)
    pre, post = min(hit, PRE), min(total - hit, POST)
    if pre < MIN_PRE or post < MIN_POST:
        raise ValueError(f"the hit at {hit:.2f} s leaves {pre:.1f} s before and {post:.1f} s after")
    out = out_dir / f"{c['id']}.opus"
    norm = cut(source, out, hit - pre, pre + post)
    return {"file": out.name, "hitGuess": round(pre, 3), "sourceSeconds": round(total, 3), "window": [round(hit - pre, 3), round(hit + post, 3)],
            "hitInSource": round(hit, 3), "analysis": why, **norm}


# ---- The intake.

def performers(j: dict) -> str:
    return (j["artist"] or j["credit"] or "Unknown performers")[:200]


def licence_section(wikitext: str) -> str:
    m = re.search(r"==\s*\{\{\s*int:license-header\s*\}\}\s*==(.*?)(\n==[^=]|\Z)", wikitext, re.S | re.I)
    return (m.group(1) if m else "\n".join(l for l in wikitext.splitlines() if CC0.search(l) or PD_ONLY.search(l))).strip()[:2000]


def draft(c: dict, j: dict, sha256: str, cutinfo: dict) -> dict:
    day = time.strftime("%Y-%m-%d", time.gmtime())
    return {"id": c["id"], "file": cutinfo["file"], "work": c["work"], "composer": c["composer"], "year": c["year"], "mood": c["mood"],
            "performers": performers(j), "source": j["descriptionUrl"], "hitGuess": cutinfo["hitGuess"],
            "evidence": {"sourceSha256": sha256, "page": j["descriptionUrl"], "record": f"evidence/{c['id']}.json",
                         "note": f"The Wikimedia Commons file page dedicates this recording under CC0 1.0 ({{{{cc-zero}}}}, revision {j['revision']}, "
                                 f"fetched {day}); its licence metadata and wikitext are in evidence/{c['id']}.json, and the rendered page in the "
                                 f"intake's artifact. Seconds {cutinfo['window'][0]}-{cutinfo['window'][1]} of the original; the hit: {c['hit']['note']}."}}


def intake(args) -> int:
    for tool in ("ffmpeg", "ffprobe"):
        if not shutil.which(tool):
            print(f"{tool} isn't installed", file=sys.stderr)
            return 3
    SOURCES.mkdir(parents=True, exist_ok=True)
    evidence_dir = args.out / "evidence"
    evidence_dir.mkdir(parents=True, exist_ok=True)
    report = {"kept": [], "refused": [], "log": [], "fetched": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    drafts = []
    # E.6's own table first, then the rest; at most MAX_TRACKS (the repo carries every one).
    request = json.loads(strip_comments(args.request.read_text())) if args.request and args.request.exists() else {}
    only = set(args.only or [c for c in request.get("candidates", ["all"]) if c != "all"])
    unknown = only - {c["id"] for c in CANDIDATES}
    if unknown:
        print(f"no such candidates: {sorted(unknown)}", file=sys.stderr)
        return 4
    most = int(request.get("maxTracks", MAX_TRACKS))
    for c in sorted(CANDIDATES, key=lambda c: (not c.get("e6"), CANDIDATES.index(c))):
        if only and c["id"] not in only:
            continue
        if len(drafts) >= most:
            break
        try:
            j = find(c, report["log"])
        except OSError as e:
            print(json.dumps({"blocked": True, "host": "commons.wikimedia.org", "error": str(e)}))
            print("Commons can't be reached from here: run .github/workflows/music-intake.yml (E.6's fallback stays).", file=sys.stderr)
            return 2
        if j is None:
            report["refused"].append({"id": c["id"], "why": "no CC0 recording found"})
            continue
        work = SOURCES / c["id"]
        work.mkdir(parents=True, exist_ok=True)
        source = work / Path(urllib.parse.unquote(urllib.parse.urlparse(j["url"]).path)).name
        try:
            sha256 = download(j["url"], source)
        except OSError as e:
            report["refused"].append({"id": c["id"], "title": j["title"], "why": f"download failed: {e}"})
            continue
        sha1 = hashlib.sha1(source.read_bytes()).hexdigest()
        if j["sha1"] and sha1 != j["sha1"]:
            report["refused"].append({"id": c["id"], "title": j["title"], "why": f"the download's SHA-1 {sha1} isn't Commons' {j['sha1']}"})
            continue
        (work / "page.html").write_text(archive_page(j["title"]))
        (work / "wikitext.txt").write_text(j["_wikitext"])
        (work / "extmetadata.json").write_text(json.dumps(j["_extmetadata"], indent=2, ensure_ascii=False) + "\n")
        try:
            cutinfo = take(c, source, args.out)
        except (ValueError, subprocess.CalledProcessError) as e:
            report["refused"].append({"id": c["id"], "title": j["title"], "why": str(e)})
            continue
        record = {k: v for k, v in j.items() if not k.startswith("_")}
        record |= {"id": c["id"], "work": c["work"], "composer": c["composer"], "sourceSha256": sha256, "fetched": report["fetched"],
                   "licenceTemplates": sorted(set(m.group(1).lower() for m in CC0.finditer(j["_wikitext"]))),
                   "licenceSection": licence_section(j["_wikitext"]), "hitNote": c["hit"]["note"], **cutinfo}
        text = json.dumps(record, indent=2, ensure_ascii=False) + "\n"
        (evidence_dir / f"{c['id']}.json").write_text(text)
        (work / "evidence.json").write_text(text)
        drafts.append(draft(c, j, sha256, cutinfo))
        report["kept"].append({"id": c["id"], "title": j["title"], "performers": performers(j), "window": cutinfo["window"],
                               "hitInSource": cutinfo["hitInSource"], "note": c["hit"]["note"]})
    (SOURCES / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n")
    drafts_path = SOURCES / "drafts.json"
    drafts_path.write_text(json.dumps({"tracks": drafts}, indent=2, ensure_ascii=False) + "\n")
    print(json.dumps({k: report[k] for k in ("kept", "refused")}, indent=2, ensure_ascii=False))
    if args.no_finish:
        return 0
    return finish(args.dt, args.out, drafts_path)


def strip_comments(text: str) -> str:
    """The request file is JSON with // comments, like everything in content/."""
    return "\n".join(l for l in text.splitlines() if not l.lstrip().startswith("//"))


def finish(dt: str, out: Path, drafts_path: Path) -> int:
    cmd = dt.split() + ["audio", "music", "--drafts", str(drafts_path), "--folder", str(out)]
    print("$ " + " ".join(cmd), file=sys.stderr)
    return subprocess.run(cmd, cwd=ROOT).returncode


# ---- Discovery (the old listing): what Commons has for each work.

def search_report() -> int:
    out = []
    for c in CANDIDATES:
        try:
            for q in c["search"]:
                for j in map(judge, search_pages(q)):
                    out.append({"candidate": c["id"], "query": q,
                                **{k: j[k] for k in ("title", "cc0", "licence", "licenceShortName", "artist", "refused", "descriptionUrl")}})
        except OSError as e:
            print(json.dumps({"blocked": True, "host": "commons.wikimedia.org", "error": str(e)}))
            return 2
    print(json.dumps(out, indent=2, ensure_ascii=False))
    return 0


# ---- The offline test: a made-up recording through the same cut, normalise, encode and manifest path.

def offline_test(args) -> int:
    for tool in ("ffmpeg", "ffprobe"):
        if not shutil.which(tool):
            print(f"--offline-test needs {tool}", file=sys.stderr)
            return 3
    tmp = Path(tempfile.mkdtemp(prefix="dt-music-"))
    out = tmp / "music"
    out.mkdir()
    # 70 s of "orchestra" (pink noise under a chord swelling in waves), with a crash into a loud bar at 41.3 s and a smaller
    # one at 20 s that the hint's range leaves out.
    src = tmp / "made-up.wav"
    ffmpeg("-f", "lavfi", "-i", "anoisesrc=d=70:c=pink:a=0.05:seed=7", "-f", "lavfi",
           "-i", "aevalsrc=0.08*(sin(2*PI*196*t)+sin(2*PI*247*t)+sin(2*PI*294*t))*(0.6+0.4*sin(2*PI*t/9)):d=70:s=44100",
           "-f", "lavfi", "-i", "anoisesrc=d=1.5:c=white:a=0.9:seed=3", "-f", "lavfi", "-i", "anoisesrc=d=1.0:c=white:a=0.4:seed=4",
           "-filter_complex",
           "[2]afade=t=out:st=0:d=1.5,adelay=41300|41300[c1];[3]afade=t=out:st=0:d=1.0,adelay=20000|20000[c2];"
           "[0][1][c1][c2]amix=inputs=4:normalize=0,volume=0.7",
           "-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le", str(src))
    cand = dict(id="gallop-offline-test", work="\"Offline test\"", composer="Nobody", year=1900, mood="gallop", titles=["File:X.ogg"],
                search=["x"], match=["x"], hit=dict(start=30, end=50, note="the crash at 41.3 s"))
    info = take(cand, src, out)
    fake = {"artist": "The offline test", "credit": "", "descriptionUrl": "https://commons.wikimedia.org/wiki/File:Made_up.wav", "revision": 0}
    d = draft(cand, fake, hashlib.sha256(src.read_bytes()).hexdigest(), info)
    (out / "evidence").mkdir()
    (out / "evidence" / f"{cand['id']}.json").write_text(json.dumps({"offlineTest": True, **info}, indent=2) + "\n")
    drafts_path = tmp / "drafts.json"
    drafts_path.write_text(json.dumps({"tracks": [d]}, indent=2) + "\n")
    result = {"tmp": str(tmp), "hitInSource": info["hitInSource"], "window": info["window"], "normalization": info["normalization"],
              "bytes": (out / d["file"]).stat().st_size}
    ok = abs(info["hitInSource"] - 41.3) < 0.15
    if not args.no_finish:
        code = finish(args.dt, out, drafts_path)
        manifest = (out / "manifest.json").read_text() if (out / "manifest.json").exists() else ""
        body = json.loads(manifest[manifest.index("{"):]) if manifest else {"tracks": []}
        track = next((t for t in body["tracks"] if t["id"] == cand["id"]), None)
        result |= {"dt": code, "manifest": track}
        ok = ok and code == 0 and track is not None and abs(track["hit"] - info["hitGuess"]) <= REFINE + 0.01 \
            and abs(track["loudnessLufs"] + track["gainDb"] - LUFS) < 0.06 and (out / "CREDITS.md").exists()
    result["ok"] = ok
    print(json.dumps(result, indent=2))
    if ok and not args.keep:
        shutil.rmtree(tmp)
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dry-run", action="store_true", help="check the candidate list's structure, offline")
    ap.add_argument("--offline-test", action="store_true", help="run a made-up recording through the cut/normalise/encode/manifest path")
    ap.add_argument("--search", action="store_true", help="list what Commons has for each candidate, with each file's licence")
    ap.add_argument("--only", nargs="*", help="candidate ids to take (default: the request file's)")
    ap.add_argument("--request", type=Path, default=REQUEST, help="the request file (default tools/audio/music_intake.json)")
    ap.add_argument("--out", type=Path, default=MUSIC, help="where the music goes (default content/audio/music)")
    ap.add_argument("--dt", default="dotnet run --project src/DarkTerritory.Cli --", help="how to run `dt` for the last step")
    ap.add_argument("--no-finish", action="store_true", help="stop before `dt audio music` (drafts in intake/_sources/music/drafts.json)")
    ap.add_argument("--keep", action="store_true", help="--offline-test: keep its temporary folder")
    args = ap.parse_args()
    if args.dry_run:
        problems = validate(CANDIDATES)
        if args.request and args.request.exists():
            req = json.loads(strip_comments(args.request.read_text()))
            ids = {c["id"] for c in CANDIDATES}
            problems += [f"request: no candidate {x}" for x in req.get("candidates", []) if x != "all" and x not in ids]
        moods = {m: [c["id"] for c in CANDIDATES if c["mood"] == m] for m in MOODS}
        print(json.dumps({"candidates": len(CANDIDATES), "e6": sum(1 for c in CANDIDATES if c.get("e6")), "moods": moods, "problems": problems},
                         indent=2))
        return 1 if problems else 0
    if args.offline_test:
        return offline_test(args)
    if args.search:
        return search_report()
    return intake(args)


if __name__ == "__main__":
    sys.exit(main())
