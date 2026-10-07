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
# The decoded peak a cut is held under (dt audio music caps the gain at -1 dBFS; this leaves it room to reach -16 LUFS).
PEAK_DB = -1.5
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
# titles: Commons files to try first (a title that's missing or not CC0 is skipped and logged).
# categories: Commons categories the work's recordings sit in (scanned with the shared ones in SCAN, below).
# must: patterns (on the title, lower case without accents) that ALL have to match: the work's own distinctive words, so
#   "Peer Gynt" alone doesn't let in Åse's Death for the Mountain King, nor "Beethoven" alone the Egmont overture for the
#   Fifth (both happened on the first CI run). never: patterns that refuse a title outright (another movement's name).
#   A file found in one of the candidate's own categories needs only to pass never.
# search: Commons searches, a last resort (they're rate-limited).
# hit: where the climax is, as a range in the file: seconds from the start, negative from the end, or with "fraction"
#   as fractions of the length (recordings differ, so a range the analysis searches, not a timestamp). note: the moment,
#   from the piece's structure, which goes into the manifest with the time the analysis chose.
# e6: in E.6's candidate table (preferred); the others are public-domain works in the same moods.
CANDIDATES: list[dict] = [
    # Lament: a slow tip-over's tragedy.
    dict(id="lament-vesti-la-giubba", work="\"Vesti la giubba\" (\"Ridi, Pagliaccio\"), Pagliacci", composer="Ruggero Leoncavallo",
         year=1892, mood="lament", e6=True,
         titles=["File:Vesti la giubba.ogg", "File:Leoncavallo - Pagliacci - Vesti la giubba.ogg"],
         categories=["Category:Vesti la giubba", "Category:Pagliacci", "Category:Audio files of Pagliacci"],
         must=["giubba|ridi,? pagliaccio"], never=[], search=["\"Vesti la giubba\""],
         hit=dict(fraction=[0.45, 0.82], note="\"Ridi, Pagliaccio\": the tenor's top note on \"Ridi\" with the orchestra's tutti under it, before the sob")),
    dict(id="lament-o-mio-babbino-caro", work="\"O mio babbino caro\", Gianni Schicchi", composer="Giacomo Puccini", year=1918,
         mood="lament", e6=True,
         titles=["File:O mio babbino caro.ogg"], categories=["Category:O mio babbino caro", "Category:Gianni Schicchi"],
         must=["babbino"], never=[], search=["\"O mio babbino caro\""],
         hit=dict(fraction=[0.50, 0.85], note="\"Mi struggo e mi tormento! O Dio, vorrei morir!\": the soprano's high A-flat")),
    dict(id="lament-flower-duet", work="\"Flower Duet\" (\"Sous le dôme épais\"), Lakmé", composer="Léo Delibes", year=1883,
         mood="lament", e6=True,
         titles=["File:Flower Duet.ogg"], categories=["Category:Flower Duet", "Category:Lakmé", "Category:Lakme"],
         must=["flower duet|duo des fleurs|sous le dome"], never=[], search=["Lakmé \"Flower Duet\""],
         hit=dict(fraction=[0.55, 0.85], note="the reprise, the two voices together at the top of the line")),
    dict(id="lament-nessun-dorma", work="\"Nessun dorma\", Turandot", composer="Giacomo Puccini", year=1926, mood="lament", e6=True,
         titles=["File:Nessun dorma.ogg"], categories=["Category:Nessun dorma", "Category:Turandot", "Category:Audio files of Turandot"],
         must=["nessun dorma"], never=[], search=["\"Nessun dorma\""],
         hit=dict(start=-50, end=-19.5, note="\"Vincerò!\": the tenor's held top B as the orchestra comes in under it")),
    dict(id="lament-lacrimosa", work="\"Lacrimosa\", Requiem in D minor", composer="Wolfgang Amadeus Mozart", year=1791, mood="lament",
         titles=["File:Mozart - Requiem - Lacrimosa.ogg"], categories=["Category:Requiem (Mozart)", "Category:Audio files of Requiem (Mozart)"],
         must=["lacrimosa|lacrymosa", "mozart|k\\.? ?626|requiem"], never=["verdi", "faure", "dvorak", "berlioz"], search=["Mozart Requiem Lacrimosa"],
         hit=dict(fraction=[0.25, 0.62], note="\"judicandus homo reus\": the chorus's crescendo to the forte")),
    dict(id="lament-funeral-march", work="Marche funèbre, Piano Sonata No. 2", composer="Frédéric Chopin", year=1839, mood="lament",
         titles=["File:Frederic Chopin Piano Sonata No.2 in B flat minor Op35 - III Marche Funebre.ogg"],
         categories=["Category:Piano Sonata No. 2 (Chopin)"],
         must=["chopin|sonata no\\.? ?2|op\\.? ?35", "funeb|funer"], never=["nocturne", "prelude", "etude", "\\b(i|ii|iv)\\. "], search=["Chopin \"marche funebre\""],
         hit=dict(start=40, end=130, note="the march theme's first fortissimo: the melody in D-flat major over crashing bass chords (bars 15-22)")),
    dict(id="lament-barcarolle", work="Barcarolle (\"Belle nuit, ô nuit d'amour\"), The Tales of Hoffmann", composer="Jacques Offenbach",
         year=1881, mood="lament",
         # CC0 on Commons, as reported to a CI probe.
         titles=["File:Barcarolle - Offenbach.ogg"], categories=["Category:The Tales of Hoffmann", "Category:Barcarolle (Offenbach)"],
         must=["barcarol"], never=["chopin", "tchaik", "mendelssohn", "faure", "rachmaninov"], search=["Offenbach Barcarolle"],
         hit=dict(fraction=[0.45, 0.85], note="the barcarolle's swell: the full orchestra at the top of the tune")),
    dict(id="lament-ase-death", work="\"Åse's Death\", Peer Gynt Suite No. 1", composer="Edvard Grieg", year=1875, mood="lament",
         # The first CI run took this for the Mountain King; it's a lament in its own right, labelled for what it is.
         titles=["File:Peer Gynt Suite No. 1, Op. 46 - II. Aase's Death.ogg"], categories=["Category:Peer Gynt (Grieg)"],
         must=["aase|ase'?s death|ases dod|death of (aa?se)"], never=["morning", "mountain", "anitra", "dovregubb"], search=["Grieg \"Aase's Death\""],
         hit=dict(fraction=[0.33, 0.62], note="the strings' fortissimo peak, the climb of the lament's phrase at its loudest before it sinks away")),
    # Gallop: fast and loud.
    dict(id="gallop-william-tell", work="Overture finale, William Tell", composer="Gioachino Rossini", year=1829, mood="gallop", e6=True,
         titles=["File:Gioachino Rossini - William Tell Overture.ogg", "File:William Tell Overture.ogg"],
         categories=["Category:William Tell Overture", "Category:Guillaume Tell (Rossini)", "Category:William Tell (opera)"],
         must=["william tell|guillaume tell|guglielmo tell"], never=["schiller", "liszt"], search=["\"William Tell\" overture Rossini"],
         hit=dict(start=-80, end=-20, note="the finale's gallop: the full orchestra's return of the trumpet call, the cymbals on it")),
    dict(id="gallop-infernal-galop", work="\"Infernal Galop\" (the can-can), Orpheus in the Underworld", composer="Jacques Offenbach",
         year=1858, mood="gallop", e6=True,
         # The Musopen recording of the overture (CC0, kept in run 1): its title names neither the galop nor the can-can, so
         # it's pinned (past the patterns; still licence-checked), with the hit on the can-can's final climax at its end.
         pinned=[dict(title="File:Offenbach - Orpheus in the Underworld - Overture.ogg",
                      hit=dict(start=-55, end=-19.5, note="the can-can's final climax at the end of the overture: the whip-crack downbeat and the cymbals"))],
         titles=[],
         categories=["Category:Orpheus in the Underworld", "Category:Orphée aux enfers"],
         must=["offenbach|can-?can|galop infernal|infernal galop|orphee aux enfers|underworld"], never=["barcarol", "hoffmann", "gluck", "liszt"],
         search=["Offenbach can-can"],
         hit=dict(start=-55, end=-19.5, note="the can-can's last climb: the whip-crack downbeat and the cymbals")),
    dict(id="gallop-ride-of-the-valkyries", work="\"Ride of the Valkyries\", Die Walküre", composer="Richard Wagner", year=1870,
         mood="gallop", e6=True,
         titles=["File:Richard Wagner - Ride of the Valkyries.ogg"], categories=["Category:Ride of the Valkyries", "Category:Die Walküre"],
         must=["valkyr|walkurenritt|ritt der walkuren"], never=[], search=["\"Ride of the Valkyries\""],
         hit=dict(start=20, end=110, note="the brass entry: horns and trombones taking the theme over the strings' trills")),
    dict(id="gallop-largo-al-factotum", work="\"Largo al factotum\", The Barber of Seville", composer="Gioachino Rossini", year=1816,
         mood="gallop", e6=True,
         titles=["File:Largo al factotum.ogg"], categories=["Category:Largo al factotum", "Category:The Barber of Seville"],
         must=["factotum"], never=[], search=["\"Largo al factotum\""],
         hit=dict(fraction=[0.50, 0.82], note="\"Figaro! Figaro! Figaro!\": the baritone's top note on the tutti")),
    dict(id="gallop-mountain-king", work="\"In the Hall of the Mountain King\", Peer Gynt", composer="Edvard Grieg", year=1875, mood="gallop",
         titles=["File:Edvard Grieg - In the Hall of the Mountain King.ogg"],
         categories=["Category:In the Hall of the Mountain King", "Category:Peer Gynt (Grieg)"],
         must=["mountain king|dovregubb"], never=["aase", "ase'?s death", "morning", "anitra"], search=["\"Hall of the Mountain King\""],
         hit=dict(start=-45, end=-19.5, note="the accelerando's peak: the full orchestra and the cymbal crash")),
    dict(id="gallop-1812-overture", work="\"1812 Overture\"", composer="Pyotr Ilyich Tchaikovsky", year=1880, mood="gallop",
         titles=["File:Tchaikovsky - 1812 Overture.ogg"], categories=["Category:1812 Overture"],
         must=["1812", "tchaik|tschaik|overture|ouverture|op\\.? ?49"], never=[], search=["Tchaikovsky 1812 overture"],
         hit=dict(start=-120, end=-25, note="the cannon: the first shot into the bells and the brass")),
    # Doom: a fast one, and the bill is due.
    dict(id="doom-dies-irae", work="\"Dies irae\", Messa da Requiem", composer="Giuseppe Verdi", year=1874, mood="doom", e6=True,
         titles=["File:Giuseppe Verdi - Messa da Requiem - Dies irae.ogg"],
         categories=["Category:Requiem (Verdi)", "Category:Messa da Requiem (Verdi)", "Category:Audio files of Requiem (Verdi)"],
         must=["verdi", "dies ira"], never=["mozart", "lacrymosa", "lacrimosa", "agnus", "sanctus", "offertor", "kyrie", "libera me", "lux aeterna"],
         search=["Verdi Requiem \"Dies irae\""],
         hit=dict(start=4.5, end=40, note="the first bass-drum strikes: the off-beat blows after the opening chords (or their return)")),
    dict(id="doom-der-holle-rache", work="\"Der Hölle Rache\", The Magic Flute", composer="Wolfgang Amadeus Mozart", year=1791, mood="doom",
         e6=True,
         titles=["File:Der Hölle Rache.ogg"], categories=["Category:Der Hölle Rache kocht in meinem Herzen", "Category:The Magic Flute"],
         must=["holle rache|hoelle rache|rache kocht"], never=[], search=["\"Der Hölle Rache\""],
         hit=dict(fraction=[0.25, 0.70], note="the Queen of the Night's staccato run to the top F")),
    dict(id="doom-dies-irae-mozart", work="\"Dies irae\", Requiem in D minor", composer="Wolfgang Amadeus Mozart", year=1791, mood="doom",
         titles=["File:Mozart - Requiem - Dies irae.ogg"], categories=["Category:Requiem (Mozart)", "Category:Audio files of Requiem (Mozart)"],
         must=["mozart|k\\.? ?626", "dies ira"], never=["verdi"], search=["Mozart Requiem \"Dies irae\""],
         hit=dict(start=20, end=80, note="\"Quantus tremor est futurus\": the tutti's return after the basses' line")),
    dict(id="doom-night-on-bald-mountain", work="\"Night on Bald Mountain\"", composer="Modest Mussorgsky", year=1867, mood="doom",
         titles=["File:Modest Mussorgsky - Night on Bald Mountain.ogg"], categories=["Category:Night on Bald Mountain"],
         must=["bald mountain|lysoy gor|lysaya gor|mont chauve"], never=[], search=["\"Night on Bald Mountain\""],
         hit=dict(start=20, end=160, note="the brass's first great statement over the swirling strings")),
    dict(id="doom-danse-macabre", work="\"Danse macabre\"", composer="Camille Saint-Saëns", year=1874, mood="doom",
         titles=["File:Camille Saint-Saëns - Danse macabre.ogg"], categories=["Category:Danse macabre (Saint-Saëns)"],
         must=["danse macabre"], never=["liszt"], search=["\"Danse macabre\" Saint-Saens"],
         hit=dict(fraction=[0.55, 0.85], note="the dance's climax: the whole orchestra and the xylophone's bones")),
    dict(id="doom-egmont-overture", work="Overture, Egmont", composer="Ludwig van Beethoven", year=1810, mood="doom",
         # The first CI run took this for the Fifth; it's a doom piece in its own right, labelled for what it is.
         titles=["File:Beethoven EgmontOvertureOp.84 LudwigVanBeethoven-EgmontOvertureOp.84.ogg"],
         categories=["Category:Egmont (Beethoven)", "Category:Egmont (Goethe)"],
         must=["egmont", "overture|ouverture|op\\.? ?84"], never=["clarchen", "trommel", "freudvoll", "lied"], search=["Beethoven Egmont overture"],
         hit=dict(start=-95, end=-25, note="the final Allegro con brio (the \"victory symphony\"): the horns' and trumpets' fanfare over the full orchestra")),
    dict(id="doom-fifth-symphony", work="Symphony No. 5, first movement", composer="Ludwig van Beethoven", year=1808, mood="doom",
         titles=["File:Ludwig van Beethoven - Symphonie 5 c-moll - 1. Allegro con brio.ogg"],
         categories=["Category:Symphony No. 5 (Beethoven)"],
         must=["beethoven|op\\.? ?67", "symphon\\w* (no\\.? ?)?5\\b|5th symphony|fifth symphony|sinfonie (nr\\.? ?)?5\\b|op\\.? ?67",
               "allegro con brio|\\b(1|i)\\.|movement 1|mvt\\.? ?1|first movement|1st movement"],
         never=["egmont", "coriolan", "pastoral", "andante", "scherzo", "\\b(ii|iii|iv)\\b", "op\\.? ?68"], search=["Beethoven Symphony 5 allegro con brio"],
         hit=dict(fraction=[0.55, 0.80], note="the recapitulation: the four-note motif fortissimo in the full orchestra")),
    # Swagger: fits any derailment.
    dict(id="swagger-anvil-chorus", work="\"Anvil Chorus\", Il trovatore", composer="Giuseppe Verdi", year=1853, mood="swagger", e6=True,
         titles=["File:Giuseppe Verdi - Anvil Chorus.ogg"], categories=["Category:Anvil Chorus", "Category:Il trovatore"],
         must=["anvil|fosche notturne|coro di zingari"], never=[], search=["\"Anvil Chorus\""],
         hit=dict(start=30, end=-19.5, note="the refrain with the anvils striking on the beat under the full chorus")),
    dict(id="swagger-la-donna-e-mobile", work="\"La donna è mobile\", Rigoletto", composer="Giuseppe Verdi", year=1851, mood="swagger", e6=True,
         titles=["File:La donna è mobile.ogg"], categories=["Category:Audio files of La donna è mobile", "Category:La donna è mobile"],
         must=["donna e mobile"], never=[], search=["\"La donna è mobile\""],
         hit=dict(fraction=[0.25, 0.75], note="the refrain's top note with the orchestra's tutti")),
    dict(id="swagger-toreador", work="\"Toreador Song\" (\"Votre toast\"), Carmen", composer="Georges Bizet", year=1875, mood="swagger", e6=True,
         titles=["File:Bizet - Carmen - Toreador Song (French, Musopen).ogg"], categories=["Category:Toreador Song"],
         must=["toread|votre toast"], never=["prelude", "entr.?acte"], search=["Carmen \"Toreador\""],
         hit=dict(fraction=[0.30, 0.78], note="\"Toréador, en garde!\": the refrain's downbeat, the chorus joining")),
    dict(id="swagger-habanera", work="\"Habanera\" (\"L'amour est un oiseau rebelle\"), Carmen", composer="Georges Bizet", year=1875,
         mood="swagger", e6=True,
         titles=["File:Habanera.ogg"], categories=["Category:Habanera (Carmen)"],
         must=["habanera|oiseau rebelle"], never=["ravel", "saint-saens", "sarasate", "chabrier", "debussy"], search=["Carmen \"Habanera\""],
         hit=dict(fraction=[0.30, 0.80], note="\"L'amour!\": the chorus's shout into the refrain")),
    dict(id="swagger-carmen-prelude", work="Prelude, Carmen", composer="Georges Bizet", year=1875, mood="swagger",
         # CC0, Musopen, kept in run 1; run 2 lost it to the Habanera through a shared category (no longer enough to match).
         pinned=[dict(title="File:Carmen - Prelude to Act 1.ogg")],
         titles=["File:Carmen - Prelude to Act 1.ogg"], categories=["Category:Audio files of Carmen"],
         must=["carmen", "prelud|overture|ouverture"], never=["act (2|3|4|ii|iii|iv)\\b", "acte? (2|3|4)", "entr.?acte"], search=["Carmen prelude Bizet"],
         hit=dict(start=50, end=90, note="the opening march's return, fortissimo with the cymbals, before the fate motif")),
    dict(id="swagger-hallelujah", work="\"Hallelujah\" chorus, Messiah", composer="George Frideric Handel", year=1741, mood="swagger",
         titles=["File:Handel - Messiah - Hallelujah.ogg"], categories=["Category:Hallelujah (Handel)", "Category:Messiah (Handel)"],
         must=["hallelujah|halleluja", "handel|haendel|messiah"], never=[], search=["Handel \"Hallelujah\" Messiah"],
         hit=dict(start=-75, end=-20, note="\"King of Kings\": the trumpets and timpani on the top of the climb")),
    dict(id="swagger-barber-overture", work="Overture, The Barber of Seville", composer="Gioachino Rossini", year=1816, mood="swagger",
         titles=["File:Gioachino Rossini - The Barber of Seville - Overture.ogg"], categories=["Category:The Barber of Seville"],
         must=["barb|siviglia|seville|sevilla", "overture|ouverture|sinfonia"], never=["paisiello"], search=["\"Barber of Seville\" overture"],
         hit=dict(start=-70, end=-20, note="the crescendo's arrival: the tutti chord on the bass drum")),
]

def pool(id: str, work: str, composer: str, year: int, must: list[str], never: list[str], hit: dict) -> dict:
    return dict(id=id, work=work, composer=composer, year=year, mood=id.split("-")[0], open=True, titles=[], search=[], categories=[],
                must=must, never=never, hit=hit)


# Movement patterns: a roman numeral or "n." as a word, so "IV." isn't read as "I.".
def mvt(n: int, *names: str) -> str:
    roman = ["i", "ii", "iii", "iv"][n - 1]
    return "|".join([f"\\b{roman}\\b", f"\\b{n}\\.", f"movement {n}", f"mvt\\.? ?{n}", *names])


def not_mvt(n: int, *names: str) -> list[str]:
    return [f"\\b{r}\\b" for i, r in enumerate(["i", "ii", "iii", "iv"], 1) if i != n] + list(names)


# ---- The open pool (run 2 found the famous arias mostly "Public domain" only): well-known dramatic public-domain works
# whose CC0 recordings (Musopen's, mostly) the category scan may turn up. Only scanned files are considered (no title
# guesses, no searches), ranked by their place here within each mood: the most recognisable first. Each hit is that
# piece's own climax.
OPEN = [
    # Doom.
    pool("doom-toccata-d-minor", "Toccata and Fugue in D minor, BWV 565", "Johann Sebastian Bach", 1708,
         ["toccata", "565|d minor|d-moll"], ["dorian|538|540|adagio"],
         dict(start=4.5, end=40, note="the full organ's diminished chord after the opening flourishes")),
    pool("doom-dvorak-new-world-finale", "Symphony No. 9 \"From the New World\", finale", "Antonín Dvořák", 1893,
         ["dvor", "new world|nuevo mundo|symphon\\w*.*\\b9\\b|op\\.? ?95", mvt(4, "allegro con fuoco", "finale")], not_mvt(4, "largo", "scherzo"),
         dict(start=4.5, end=40, note="the horns' and trumpets' theme over the strings' hammered chords")),
    pool("doom-mozart-confutatis", "\"Confutatis\", Requiem in D minor", "Wolfgang Amadeus Mozart", 1791,
         ["confutatis", "mozart|k\\.? ?626|requiem"], ["verdi"],
         dict(start=25, end=-19.5, note="the men's second \"Confutatis\" over the stabbing strings")),
    pool("doom-mozart-rex-tremendae", "\"Rex tremendae\", Requiem in D minor", "Wolfgang Amadeus Mozart", 1791,
         ["rex tremendae", "mozart|k\\.? ?626|requiem"], ["verdi"],
         dict(start=4.5, end=40, note="the chorus's \"Rex!\" shouts on the dotted rhythm")),
    pool("doom-grieg-piano-concerto", "Piano Concerto in A minor, first movement", "Edvard Grieg", 1868,
         ["grieg", "concerto|op\\.? ?16", mvt(1, "allegro molto moderato")], not_mvt(1, "adagio"),
         dict(start=-90, end=-20, note="the cadenza's thundering climax into the orchestra's return")),
    pool("doom-tchaikovsky-fourth", "Symphony No. 4, first movement", "Pyotr Ilyich Tchaikovsky", 1878,
         ["tchaik|tschaik", "symphon\\w*.*\\b4\\b|op\\.? ?36", mvt(1, "andante sostenuto")], not_mvt(1, "andantino", "scherzo", "finale"),
         dict(start=4.5, end=40, note="the fate fanfare's fortissimo chords in the full orchestra")),
    pool("doom-tchaikovsky-pathetique", "Symphony No. 6 \"Pathétique\", first movement", "Pyotr Ilyich Tchaikovsky", 1893,
         ["tchaik|tschaik", "pathetique|symphon\\w*.*\\b6\\b|op\\.? ?74", mvt(1, "adagio - allegro non troppo")], not_mvt(1, "allegro con grazia", "scherzo"),
         dict(fraction=[0.42, 0.62], note="the development's explosion: the fortissimo crash after the clarinet's dying phrase")),
    pool("doom-moonlight-presto", "Piano Sonata No. 14 \"Moonlight\", Presto agitato", "Ludwig van Beethoven", 1801,
         ["beethoven|op\\.? ?27", "moonlight|mondschein|sonata no\\.? ?14|27,? no\\.? ?2", mvt(3, "presto agitato")], not_mvt(3, "adagio sostenuto"),
         dict(fraction=[0.05, 0.4], note="the arpeggios slammed into the two sforzando chords")),
    pool("doom-pathetique-sonata", "Piano Sonata No. 8 \"Pathétique\", first movement", "Ludwig van Beethoven", 1798,
         ["beethoven|op\\.? ?13", "pathetique|sonata no\\.? ?8|op\\.? ?13", mvt(1, "grave")], not_mvt(1, "adagio cantabile", "rondo"),
         dict(start=4.5, end=40, note="the Grave's fortissimo chords")),
    pool("doom-revolutionary-etude", "Étude Op. 10 No. 12 \"Revolutionary\"", "Frédéric Chopin", 1831,
         ["chopin", "revolution|op\\.? ?10,? no\\.? ?12"], [],
         dict(start=4.5, end=-19.5, note="the right hand's fortissimo octave theme over the left hand's storm")),
    pool("doom-mozart-fortieth", "Symphony No. 40, first movement", "Wolfgang Amadeus Mozart", 1788,
         ["mozart|k\\.? ?550", "symphon\\w*.*\\b40\\b|k\\.? ?550", mvt(1, "molto allegro")], not_mvt(1, "andante", "menuet", "finale"),
         dict(fraction=[0.05, 0.35], note="the first tutti outburst after the violins' sighing theme")),
    pool("doom-vivaldi-summer", "\"Summer\", The Four Seasons, Presto", "Antonio Vivaldi", 1725,
         ["vivaldi|four seasons|quattro stagioni", "summer|estate", mvt(3, "presto", "tempo impetuoso")], not_mvt(3, "adagio", "allegro non molto"),
         dict(fraction=[0.1, 0.6], note="the storm: the strings' tremolo and hammered scales")),
    pool("doom-baba-yaga", "\"The Hut on Hen's Legs (Baba Yaga)\", Pictures at an Exhibition", "Modest Mussorgsky", 1874,
         ["baba|yaga|hen'?s legs|poules"], [],
         dict(start=4.5, end=-19.5, note="the hut's stamping fortissimo")),
    pool("doom-erlkonig", "\"Erlkönig\"", "Franz Schubert", 1815,
         ["erlk"], ["goethe(?!.*schubert)"],
         dict(fraction=[0.55, 0.85], note="the father's last gallop, the piano hammering, before \"war tot\"")),
    # Gallop.
    pool("gallop-beethoven-seventh-finale", "Symphony No. 7, finale", "Ludwig van Beethoven", 1812,
         ["beethoven|op\\.? ?92", "symphon\\w*.*\\b7\\b|op\\.? ?92", mvt(4, "allegro con brio", "finale")], not_mvt(4, "allegretto", "poco sostenuto"),
         dict(fraction=[0.6, 0.9], note="the coda's fortissimo over the basses' grinding ostinato")),
    pool("gallop-hungarian-dance-5", "Hungarian Dance No. 5", "Johannes Brahms", 1869,
         ["brahms", "hungarian|ungarisch", "\\b5\\b"], [],
         dict(fraction=[0.05, 0.6], note="the tune's fortissimo return after the slow lull")),
    pool("gallop-light-cavalry", "Overture, Light Cavalry", "Franz von Suppé", 1866,
         ["light cavalry|leichte kavallerie"], [],
         dict(start=-80, end=-20, note="the gallop's trumpet-led return")),
    pool("gallop-hungarian-rhapsody-2", "Hungarian Rhapsody No. 2", "Franz Liszt", 1847,
         ["liszt", "rhapsod", "\\b2\\b"], ["\\b(12|20)\\b"],
         dict(start=-80, end=-20, note="the friska's whirling climax")),
    pool("gallop-sorcerers-apprentice", "\"The Sorcerer's Apprentice\"", "Paul Dukas", 1897,
         ["sorcerer'?s apprentice|apprenti sorcier|zauberlehrling"], [],
         dict(fraction=[0.55, 0.82], note="the brooms' flood at its fortissimo, before the spell breaks")),
    pool("gallop-farandole", "\"Farandole\", L'Arlésienne Suite No. 2", "Georges Bizet", 1872,
         ["farandol"], [],
         dict(start=-50, end=-19.5, note="the march and the farandole together, fortissimo")),
    pool("gallop-rondo-alla-turca", "\"Rondo alla turca\", Piano Sonata No. 11", "Wolfgang Amadeus Mozart", 1783,
         ["turca|turkish march|k\\.? ?331"], ["beethoven"],
         dict(fraction=[0.2, 0.7], note="the A major refrain's crashing octaves")),
    pool("gallop-flight-of-the-bumblebee", "\"Flight of the Bumblebee\"", "Nikolai Rimsky-Korsakov", 1900,
         ["bumble|shmel"], [],
         dict(start=4.5, end=-19.5, note="the buzzing run's loudest swoop")),
    # Lament.
    pool("lament-schubert-unfinished", "Symphony No. 8 \"Unfinished\", first movement", "Franz Schubert", 1822,
         ["schubert", "unfinished|unvollendete|symphon\\w*.*\\b8\\b|d\\.? ?759", mvt(1, "allegro moderato")], not_mvt(1, "andante con moto"),
         dict(fraction=[0.08, 0.35], note="the tutti's fortissimo chords breaking in on the cellos' song")),
    pool("lament-nimrod", "\"Nimrod\", Enigma Variations", "Edward Elgar", 1899,
         ["nimrod|variation ix|var\\.? ?ix"], [],
         dict(fraction=[0.55, 0.85], note="the variation's broad fortissimo peak")),
    pool("lament-swan-lake", "Scene, Swan Lake", "Pyotr Ilyich Tchaikovsky", 1876,
         ["swan lake|lac des cygnes|lebedinoye", "scene|act ii|no\\.? ?10|finale"], ["waltz|valse|cygnets|petits"],
         dict(fraction=[0.4, 0.85], note="the oboe's swan theme taken up fortissimo by the full orchestra")),
    pool("lament-romeo-and-juliet", "Romeo and Juliet, fantasy overture", "Pyotr Ilyich Tchaikovsky", 1880,
         ["romeo", "tchaik|tschaik|fantas|overture|ouverture"], ["prokofiev|berlioz|gounod"],
         dict(fraction=[0.6, 0.85], note="the love theme's fortissimo return")),
    pool("lament-new-world-largo", "Symphony No. 9 \"From the New World\", Largo", "Antonín Dvořák", 1893,
         ["dvor", "new world|nuevo mundo|symphon\\w*.*\\b9\\b|op\\.? ?95", mvt(2, "largo")], not_mvt(2, "allegro con fuoco", "scherzo"),
         dict(fraction=[0.6, 0.85], note="the climax before the cor anglais's theme returns")),
    pool("lament-chopin-prelude-e-minor", "Prelude in E minor, Op. 28 No. 4", "Frédéric Chopin", 1839,
         ["chopin", "prelud", "e minor|e-moll|op\\.? ?28,? no\\.? ?4"], [],
         dict(start=4.5, end=-19.5, note="the stretto's forte outburst before the silence")),
    # Swagger.
    pool("swagger-ode-to-joy", "Symphony No. 9, finale (\"Ode to Joy\")", "Ludwig van Beethoven", 1824,
         ["beethoven|op\\.? ?125", "symphon\\w*.*\\b9\\b|op\\.? ?125|choral", mvt(4, "presto", "ode", "freude", "finale")],
         not_mvt(4, "adagio", "molto vivace", "allegro ma non troppo"),
         dict(fraction=[0.25, 0.5], note="\"Freude, schöner Götterfunken\": the full chorus's first entry")),
    pool("swagger-beethoven-fifth-finale", "Symphony No. 5, finale", "Ludwig van Beethoven", 1808,
         ["beethoven|op\\.? ?67", "symphon\\w*.*\\b5\\b|op\\.? ?67", mvt(4, "finale")], not_mvt(4, "andante", "con brio"),
         dict(start=-80, end=-20, note="the presto coda's final C major blaze")),
    pool("swagger-pomp-and-circumstance", "Pomp and Circumstance March No. 1", "Edward Elgar", 1901,
         ["pomp", "circumstance", "\\b1\\b|op\\.? ?39"], [],
         dict(fraction=[0.6, 0.85], note="\"Land of Hope and Glory\" at full orchestra")),
    pool("swagger-radetzky-march", "Radetzky March", "Johann Strauss I", 1848,
         ["radetzky"], [], dict(fraction=[0.15, 0.6], note="the march's tutti refrain")),
    pool("swagger-blue-danube", "\"The Blue Danube\"", "Johann Strauss II", 1866,
         ["blue danube|blauen donau|schonen blauen"], [], dict(fraction=[0.1, 0.45], note="the first waltz's tutti swing")),
    pool("swagger-wedding-march", "Wedding March, A Midsummer Night's Dream", "Felix Mendelssohn", 1842,
         ["wedding march|hochzeitsmarsch", "mendelssohn|midsummer|sommernacht"], ["wagner|lohengrin"],
         dict(start=4.5, end=30, note="the trumpets' fanfare into the march's tutti")),
    pool("swagger-great-gate-of-kiev", "\"The Great Gate of Kiev\", Pictures at an Exhibition", "Modest Mussorgsky", 1874,
         ["great gate|gate of kiev|kyiv|bogatyr"], [], dict(start=-70, end=-20, note="the bells and the full orchestra's last statement")),
    pool("swagger-zadok-the-priest", "\"Zadok the Priest\"", "George Frideric Handel", 1727,
         ["zadok"], [], dict(fraction=[0.2, 0.45], note="the choir's \"Zadok the priest!\" after the long string build")),
    pool("swagger-tchaikovsky-piano-concerto", "Piano Concerto No. 1, first movement", "Pyotr Ilyich Tchaikovsky", 1875,
         ["tchaik|tschaik", "piano concerto|concerto no\\.? ?1|op\\.? ?23", mvt(1, "allegro non troppo")], not_mvt(1, "andantino", "allegro con fuoco"),
         dict(start=4.5, end=40, note="the horns' opening fall into the piano's crashing chords")),
    pool("swagger-heroic-polonaise", "Polonaise in A-flat, Op. 53 \"Heroic\"", "Frédéric Chopin", 1842,
         ["chopin", "polonai", "op\\.? ?53|heroi|a-?flat"], [], dict(fraction=[0.1, 0.4], note="the heroic theme's fortissimo entry")),
    pool("swagger-lohengrin-act-3", "Prelude to Act 3, Lohengrin", "Richard Wagner", 1850,
         ["lohengrin", "act (3|iii)|third act|3\\. akt|dritten"], [], dict(start=4.5, end=40, note="the trombones' theme over the strings' triplets")),
]
CANDIDATES += OPEN

# Where the CC0 recordings are: scanned (list=categorymembers, files and subcategories, paced) before any search, which
# Commons rate-limits hard. Musopen's uploads are mostly CC0; the opera categories hold the arias.
SCAN = [("Category:Musopen", 3), ("Category:Audio files by Musopen", 2), ("Category:Audio files from Musopen", 2),
        ("Category:Audio files of operas", 2), ("Category:Audio files of opera arias", 2)]
SCAN_BUDGET = 260  # categorymembers requests, at PAUSE apart: about 11 minutes at most
AUDIO = re.compile(r"\.(ogg|oga|opus|flac|wav|mp3)$", re.I)


# ---- Checking the list (offline).

def check_hint(where: str, h: dict) -> list[str]:
    problems = []
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
    return problems


def validate(cands: list[dict]) -> list[str]:
    problems = []
    ids = set()
    for c in cands:
        where = c.get("id", "?")
        for k in ("id", "work", "composer", "year", "mood", "must", "hit"):
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
        if any(not t.startswith("File:") for t in c.get("titles", []) + pinned_titles(c)):
            problems.append(f"{where}: titles are Commons file pages (File:...)")
        if not c.get("must") or not c.get("open") and not c.get("search"):
            problems.append(f"{where}: needs the must patterns (and, unless it's from the open pool, searches)")
        if any(not t.startswith("Category:") for t in c.get("categories", [])):
            problems.append(f"{where}: categories are Commons category pages (Category:...)")
        for m in c.get("must", []) + c.get("never", []):
            try:
                re.compile(m)
            except re.error as e:
                problems.append(f"{where}: match {m!r} isn't a pattern ({e})")
        for h in [c.get("hit", {})] + [p["hit"] for p in c.get("pinned", []) if "hit" in p]:
            problems += check_hint(where, h)
    for m in MOODS:
        if sum(1 for c in cands if c.get("mood") == m) < 2:
            problems.append(f"mood {m}: fewer than two candidates")
    if len(cands) < 14:
        problems.append(f"{len(cands)} candidates: want more than the 10 E.6 asks for at launch, since some won't be CC0")
    # The matching, on titles seen in CI: the first run's two mislabels are refused, and the right files still match.
    by_id = {c["id"]: c for c in cands}
    for cid, title, want in MATCH_CASES:
        if cid in by_id and matches(by_id[cid], title) != want:
            problems.append(f"{cid}: {title!r} should {'match' if want else 'be refused'}")
    problems += performers_check(cands)
    for c in cands:
        for t in c.get("titles", []):
            if not matches(c, t):
                problems.append(f"{c['id']}: its own title {t!r} doesn't pass its patterns")
    return problems


# The performer parse on pages like the CI runs' (run 2 read an empty field as the next one's name). The pattypan page is
# reconstructed in the National Library of Sweden uploads' layout; once a run has saved a page's wikitext in its
# evidence record, --dry-run checks the committed records too (performers_check).
PERFORMER_CASES = [
    ("swagger-la-donna-e-mobile",
     {"artist": "", "credit": "Öppna data från Kungliga biblioteket https://data.kb.se/datasets/2015/09/fonografcylindrar/",
      "description": "http://smdb.kb.se/catalog/id/001453287 Phonograph recording from the National Library of Sweden", "user": "VisbyStar",
      "_wikitext": "=={{int:filedesc}}==\n{{Information\n|description = {{sv|Fonografcylinder}}\n|date = \n|source = "
                   "[https://data.kb.se/datasets/2015/09/fonografcylindrar/ Öppna data från Kungliga biblioteket]\n|author =\n"
                   "|permission =\n|other versions =\n}}\n{{Musical work\n|composer = Giuseppe Verdi\n|performer =\n|title = La donna è mobile\n}}\n"},
     "Unknown performers (phonograph recording, National Library of Sweden)"),
    ("lament-funeral-march",
     {"artist": "Frédéric Chopin", "credit": "This work comes from the non profit U.S. organization Musopen", "description": "", "user": "X",
      "_wikitext": "{{Information\n|description=Chopin\n|author=[[w:Frédéric Chopin|Frédéric Chopin]]\n|source=Musopen\n}}"},
     "Musopen (performers uncredited)"),
    ("lament-ase-death", {"artist": "Musopen Symphony Orchestra", "credit": "", "description": "", "user": "X", "_wikitext": ""},
     "Musopen Symphony Orchestra"),
    ("swagger-toreador", {"artist": "", "credit": "", "description": "", "user": "Foo", "_wikitext": "{{Information\n|author=[[User:Foo|Foo]]\n}}"},
     "Unknown performers (uploaded by Foo)"),
    ("swagger-toreador", {"artist": "", "credit": "", "description": "Performed by the [[United States Marine Band]].", "user": "Foo", "_wikitext": ""},
     "The United States Marine Band"),
]


def performers_check(cands: list[dict]) -> list[str]:
    by_id = {c["id"]: c for c in cands}
    problems = [f"{cid}: performers {performers(j, by_id[cid])!r}, not {want!r}" for cid, j, want in PERFORMER_CASES
                if cid in by_id and performers(j, by_id[cid]) != want]
    for record in sorted((MUSIC / "evidence").glob("*.json")):
        r = json.loads(record.read_text())
        if "wikitext" in r and r.get("id") in by_id:
            got = performers({**r, "_wikitext": r["wikitext"]}, by_id[r["id"]])
            if re.search(r"^\||=|https?://", got) or fold(by_id[r["id"]]["composer"]).split()[-1] in fold(got):
                problems.append(f"{record.name}: performers parse to {got!r}")
    return problems


# (candidate, Commons title, should it match): the CI runs' mislabels, and files they found.
MATCH_CASES = [
    ("swagger-habanera", "File:Carmen - Prelude to Act 1.ogg", False),
    ("swagger-ode-to-joy", "File:Ludwig van Beethoven - Symphony No. 9 in D minor, Op. 125 - IV. Presto - Allegro assai.ogg", True),
    ("swagger-ode-to-joy", "File:Ludwig van Beethoven - Symphony No. 9 in D minor, Op. 125 - III. Adagio molto e cantabile.ogg", False),
    ("doom-dvorak-new-world-finale", "File:Antonin Dvorak - Symphony No. 9 From the New World - IV. Allegro con fuoco.ogg", True),
    ("lament-new-world-largo", "File:Antonin Dvorak - Symphony No. 9 From the New World - IV. Allegro con fuoco.ogg", False),
    ("lament-new-world-largo", "File:Antonin Dvorak - Symphony No. 9 From the New World - II. Largo.ogg", True),
    ("gallop-beethoven-seventh-finale", "File:Beethoven - Symphony No. 7 in A major, Op. 92 - IV. Allegro con brio.ogg", True),
    ("gallop-beethoven-seventh-finale", "File:Beethoven - Symphony No. 7 in A major, Op. 92 - II. Allegretto.ogg", False),
    ("gallop-infernal-galop", "File:Offenbach - Orpheus in the Underworld - Overture.ogg", True),
    ("gallop-mountain-king", "File:Peer Gynt Suite No. 1, Op. 46 - II. Aase's Death.ogg", False),
    ("lament-ase-death", "File:Peer Gynt Suite No. 1, Op. 46 - II. Aase's Death.ogg", True),
    ("doom-fifth-symphony", "File:Beethoven EgmontOvertureOp.84 LudwigVanBeethoven-EgmontOvertureOp.84.ogg", False),
    ("doom-egmont-overture", "File:Beethoven EgmontOvertureOp.84 LudwigVanBeethoven-EgmontOvertureOp.84.ogg", True),
    ("doom-fifth-symphony", "File:Ludwig van Beethoven - Symphonie 5 c-moll - 2. Andante con moto.ogg", False),
    ("doom-fifth-symphony", "File:Beethoven Symphony No. 5 - I. Allegro con brio.ogg", True),
    ("swagger-carmen-prelude", "File:Carmen - Prelude to Act 1.ogg", True),
    ("swagger-carmen-prelude", "File:Carmen - Prelude to Act 3.ogg", False),
    ("swagger-toreador", "File:Bizet - Carmen - Toreador Song (French, Musopen).ogg", True),
    ("swagger-la-donna-e-mobile", "File:La donna è mobile ZS V78-0918.wav", True),
    ("doom-dies-irae", "File:Verdi - Requiem - Lacrymosa.ogg", False),
    ("lament-funeral-march", "File:Frederic Chopin Piano Sonata No.2 in B flat minor Op35 - III Marche Funebre.ogg", True),
    ("gallop-infernal-galop", "File:Offenbach - Orpheus in the Underworld - Overture.ogg", True),
]


def matches(c: dict, title: str, cats: set | None = None) -> bool:
    """
    A file is the candidate's work: every must pattern in its title, and no never. (Being in one of the candidate's
    categories isn't enough: run 2 let "Category:Audio files of Carmen" hand the Carmen prelude to the Habanera.) A
    pinned file (the candidate's `pinned`) is its work by fiat, still licence-checked.
    """
    if title in pinned_titles(c):
        return True
    t = fold(title or "").replace("_", " ")
    if any(re.search(fold(n), t) for n in c.get("never", [])):
        return False
    return all(re.search(fold(m), t) for m in c["must"])


def pinned_titles(c: dict) -> list[str]:
    return [p["title"] for p in c.get("pinned", [])]


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


INFO = dict(prop="imageinfo|revisions", iiprop="url|extmetadata|sha1|size|mime|mediatype|user", rvprop="content|ids|timestamp", rvslots="main")


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
            "revisionTimestamp": rev.get("timestamp"), "user": info.get("user"), "refused": reasons, "_wikitext": wikitext, "_extmetadata": meta}


def scan(cands: list[dict], log: dict) -> dict[str, set]:
    """
    Every audio file in the SCAN categories and the candidates' own (list=categorymembers, files and subcategories, paced,
    at most SCAN_BUDGET requests), with the categories it was found in: titles only, so it's cheap; wikitext is fetched
    later, and only for the titles some candidate matches.
    """
    queue = list(SCAN) + [(cat, 1) for c in cands for cat in c.get("categories", [])]
    seen, files, requests = set(), {}, 0
    while queue and requests < SCAN_BUDGET:
        cat, depth = queue.pop(0)
        if cat in seen:
            continue
        seen.add(cat)
        cont = None
        while requests < SCAN_BUDGET:
            params = {"action": "query", "list": "categorymembers", "cmtitle": cat, "cmtype": "file|subcat", "cmlimit": 500}
            if cont:
                params["cmcontinue"] = cont
            data = api(params)
            requests += 1
            for m in data.get("query", {}).get("categorymembers", []):
                title = m["title"]
                if m.get("ns") == 14 and depth > 0:
                    queue.append((title, depth - 1))
                elif m.get("ns") == 6 and AUDIO.search(title):
                    files.setdefault(title, set()).add(cat)
            cont = data.get("continue", {}).get("cmcontinue")
            if not cont:
                break
    log["scan"] = {"categories": len(seen), "requests": requests, "audioFiles": len(files), "unscanned": len(queue)}
    return files


def find(c: dict, files: dict[str, set], used: set, log: list, near: dict) -> dict | None:
    """
    The candidate's recording: of the files that are its work (its titles, then scanned files that match, then a search if
    none did), the CC0 ones, best first: its own titles, then a named performer over none, then the biggest file. Every
    matching file that isn't CC0 goes in the near misses with why.
    """
    tried, good = [], []

    def consider(pages, rank):
        for j in map(judge, pages):
            if j["title"] in used or not matches(c, j["title"], files.get(j["title"], set())):
                continue
            tried.append({k: j[k] for k in ("title", "cc0", "licence", "refused")})
            if j["cc0"]:
                good.append((rank, 0 if performers(j, c).startswith("Unknown") else 1, j["size"] or 0, j))
            elif j["refused"] != ["no such file"]:
                near.setdefault(c["id"], []).append({"title": j["title"], "licence": j["licence"] or j["licenceShortName"], "why": j["refused"],
                                                     "page": j["descriptionUrl"]})

    pins = {p["title"]: p for p in c.get("pinned", [])}
    if pins:
        consider(pages_for(list(pins)), 3)
    if c["titles"]:
        consider(pages_for([t for t in c["titles"] if t not in pins]), 2)
    scanned = [t for t, cats in files.items() if t not in c["titles"] and t not in pins and t not in used and matches(c, t, cats)]
    for i in range(0, len(scanned), 20):
        consider(pages_for(scanned[i:i + 20]), 1)
    via = "scan" if good else None
    if not good:
        for q in c["search"]:
            consider(search_pages(q, lambda t: matches(c, t, set())), 0)
            if good:
                via = "search"
                break
    if good and any(r >= 2 for r, *_ in good):
        via = "pinned" if any(r == 3 for r, *_ in good) else "title"
    log.append({"id": c["id"], "via": via, "matched": len(tried), "tried": tried[:40],
                "pinned": [t for t in tried if t["title"] in pins] + [{"title": t, "cc0": False, "refused": ["not found on Commons, or taken"]}
                                                                    for t in pins if t not in {x["title"] for x in tried}]})
    if not good:
        return None
    good.sort(key=lambda g: (g[0], g[1], g[2]), reverse=True)
    best = good[0][3]
    # A pinned file can say where in it the hit is (the can-can at the end of the whole overture).
    best["_hit"] = pins.get(best["title"], {}).get("hit", c["hit"])
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
    before it, plus half of how close the second after it is to the loudest second in the range, plus half of how much
    louder that second is than the one before (to 6 dB), so a crash into the biggest bar beats a consonant in a quiet one. `dt audio music` then moves it onto the sharpest onset nearby (the
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
    before = [sum(broad[max(0, i - 100):i]) / max(1, i - max(0, i - 100)) for i in range(n)]
    for i in range(max(first, 30), min(last, n - 3) + 1):
        onset = sum(bright[i:i + 3]) / 3 - sum(bright[i - 30:i]) / 30
        # The second after it near the range's loudest, and louder than the second before (an arrival, not a let-off:
        # the first CI run put two hits where the music falls away).
        score = onset + 0.5 * (loud[i] - top) + 0.5 * max(-6.0, min(6.0, loud[i] - before[i]))
        if score > best:
            best, at = score, i
    return start + at / 100, {"score": round(best, 2), "range": [round(lo, 2), round(hi, 2)]}


def check_hit(clip: Path, hit: float) -> dict:
    """
    How the hit sits in the cut (we can't listen from CI): the 2 s after it against the 2 s before, and where the second
    after it ranks among the clip's seconds by loudness. A real arrival is louder after and near the top; "weak" is logged
    in the report for a person to listen to first.
    """
    db = block_db(pcm(clip, 0, 3600, 16000), 1600)  # 100 ms
    h = min(int(hit * 10), max(0, len(db) - 11))
    before = sum(db[max(0, h - 20):h]) / max(1, h - max(0, h - 20))
    after = sum(db[h:h + 20]) / max(1, len(db[h:h + 20]))
    loud = [sum(db[i:i + 10]) / 10 for i in range(max(1, len(db) - 10))]
    rank = sorted(loud, reverse=True).index(loud[min(h, len(loud) - 1)]) / len(loud)
    return {"jumpDb": round(after - before, 1), "loudnessRank": round(rank, 2), "weak": after - before < 1 or rank > 0.5}


def cut(source: Path, out: Path, start: float, length: float) -> dict:
    """The window, at -16 LUFS (loudnorm's two passes; linear where the true peak allows), mono Opus at 48 kHz."""
    window = ["-ss", f"{start:.3f}", "-t", f"{length:.3f}"]
    measure = ffmpeg(*window, "-i", str(source), "-ac", "1", "-af", f"loudnorm=I={LUFS}:TP={TRUE_PEAK}:LRA=11:print_format=json", "-f", "null", "-")
    stats = json.loads(measure.stderr[measure.stderr.rindex("{"):measure.stderr.rindex("}") + 1])
    norm = (f"loudnorm=I={LUFS}:TP={TRUE_PEAK}:LRA=11:measured_I={stats['input_i']}:measured_TP={stats['input_tp']}:"
            f"measured_LRA={stats['input_lra']}:measured_thresh={stats['input_thresh']}:offset={stats['target_offset']}:linear=true")
    fades = f"afade=t=in:d=0.05,afade=t=out:st={max(0.0, length - 1.0):.3f}:d=1"

    # Always through a limiter last, set so the *decoded* peak stays under -1.5 dBFS: loudnorm's true peak is for the PCM,
    # and Opus overshoots a hard transient (the offline test's noise crash decoded at +2 dBFS from a -2 dBFS limit). So
    # each encode is decoded and measured, and the limit lowered by the overshoot until it holds.
    def encode(chain: str, limit_db: float) -> None:
        lim = f"alimiter=limit={10 ** (limit_db / 20):.4f}:attack=2:release=60:level=0"
        ffmpeg(*window, "-i", str(source), "-ac", "1", "-af", f"{chain},{lim},aresample=48000,{fades}", "-ar", "48000", "-c:a", "libopus",
               "-b:a", f"{OPUS_KBPS}k", "-vbr", "on", "-application", "audio", "-map_metadata", "-1", "-fflags", "+bitexact",
               "-flags:a", "+bitexact", str(out))

    def held(chain: str) -> tuple[float, float]:
        limit = -2.0
        for _ in range(4):
            encode(chain, limit)
            top = peak_db(out)
            if top <= PEAK_DB:
                break
            limit -= top - PEAK_DB + 0.3
        return limit, top

    limit, top = held(norm)
    # loudnorm's dynamic mode (a wide-range piano, say) can land well under the target, and the game's gain would then push
    # the peaks over (run 3's Pathétique sonata). Measured short, it's encoded again with the rest of the gain into the
    # limiter, so it reaches -16 LUFS with its peaks held.
    got = measured(out)
    limited = None
    if got < LUFS - 0.3:
        limited = round(LUFS - got, 2)
        limit, top = held(f"{norm},volume={limited}dB")
        got = measured(out)
    return {"inputLufs": float(stats["input_i"]), "inputTruePeak": float(stats["input_tp"]), "normalization": stats.get("normalization_type"),
            "outputLufs": round(got, 2), "limiterGainDb": limited, "limitDb": round(limit, 2), "decodedPeakDb": round(top, 2)}


def peak_db(path: Path) -> float:
    """A file's highest sample as decoded, in dBFS."""
    a = pcm(path, 0, 3600, 48000)
    return 20 * math.log10(max(max(a, default=0.0), -min(a, default=0.0), 1e-9))


def measured(path: Path) -> float:
    """A file's integrated loudness (ffmpeg's ebur128, BS.1770), in LUFS."""
    r = ffmpeg("-i", str(path), "-af", "ebur128=framelog=quiet", "-f", "null", "-")
    found = re.findall(r"I:\s*(-?[\d.]+) LUFS", r.stderr)
    return float(found[-1]) if found else LUFS


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
    norm["hitCheck"] = check_hit(out, pre)
    return {"file": out.name, "hitGuess": round(pre, 3), "sourceSeconds": round(total, 3), "window": [round(hit - pre, 3), round(hit + post, 3)],
            "hitInSource": round(hit, 3), "analysis": why, **norm}


# ---- The intake.

def field(wikitext: str, names: str) -> list[str]:
    """Template fields (|author=, |performer=, ...) as written, up to the next field or the template's end."""
    # Only spaces after the "=": an empty field ("|author =" then a new line) is empty, not the next field's name (run 2
    # credited "|title =" as the performer of "La donna è mobile").
    found = re.finditer(r"\|[ \t]*(" + names + r")[ \t]*=[ \t]*(.*?)(?=\n[ \t]*\||\n[ \t]*\}\}|\|[ \t]*\w+[ \t]*=|\Z)", wikitext, re.I | re.S)
    return [v for v in (m.group(2).strip() for m in found) if v and not v.startswith("|")]


def unwiki(text: str) -> str:
    text = re.sub(r"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", r"\1", text)          # [[target|label]] -> label
    text = re.sub(r"\[(?:https?:)?//\S+\s*([^\]]*)\]", r"\1", text)          # [url label] -> label
    text = re.sub(r"https?://\S+", "", text)
    text = re.sub(r"\{\{\s*(?:creator|c|q):\s*([^}|]*)[^}]*\}\}", r"\1", text, flags=re.I)
    text = re.sub(r"\{\{[^{}]*\}\}", "", text)
    text = re.sub(r"'{2,}", "", plain(text))
    return re.sub(r"\s+", " ", text).strip(" .,;:-")


# A field that names a source, not a performer.
NOT_A_PERFORMER = re.compile(r"kungliga|bibliotek|library|open data|oppna data|own work|unknown|see below|wikimedia|internet archive|"
                             r"uploaded|^\W*$|public domain|this work|musopen\.org", re.I)


def performers(j: dict, c: dict) -> str:
    """
    Who played it, never the composer (the Commons "Artist" field often holds the composer, or a source blurb): the page's
    performer field, then its author or artist, then "performed by ..." in the description; a Musopen upload naming
    nobody is "Musopen (performers uncredited)"; otherwise "Unknown performers (uploaded by <user>)".
    """
    wikitext = j.get("_wikitext", "")
    composer = fold(c["composer"]).split()[-1]
    candidates = field(wikitext, "performer|performers|performed by|interpret|artist|author") + [j.get("artist", "")]
    m = re.search(r"(?:performed|played|sung|conducted) by ([^.;\n]+)", j.get("description", "") + " " + wikitext, re.I)
    if m:
        candidates.insert(0, m.group(1))
    for raw in candidates:
        name = unwiki(raw)
        if not name or len(name) > 120 or NOT_A_PERFORMER.search(name) or composer in fold(name) or re.match(r"(?i)user:", raw.strip("[ ")):
            continue
        return "Musopen (performers uncredited)" if fold(name) == "musopen" else name[0].upper() + name[1:]
    if re.search(r"musopen", j.get("credit", "") + j.get("artist", ""), re.I):
        return "Musopen (performers uncredited)"
    if re.search(r"kungliga biblioteket|national library of sweden", j.get("credit", "") + j.get("description", ""), re.I):
        return "Unknown performers (phonograph recording, National Library of Sweden)"
    return f"Unknown performers (uploaded by {j.get('user') or 'an unnamed user'})"


def clock(seconds: float) -> str:
    return f"{int(seconds // 60)}:{seconds % 60:04.1f}"


def licence_section(wikitext: str) -> str:
    m = re.search(r"==\s*\{\{\s*int:license-header\s*\}\}\s*==(.*?)(\n==[^=]|\Z)", wikitext, re.S | re.I)
    return (m.group(1) if m else "\n".join(l for l in wikitext.splitlines() if CC0.search(l) or PD_ONLY.search(l))).strip()[:2000]


def draft(c: dict, j: dict, sha256: str, cutinfo: dict) -> dict:
    day = time.strftime("%Y-%m-%d", time.gmtime())
    return {"id": c["id"], "file": cutinfo["file"], "work": c["work"], "composer": c["composer"], "year": c["year"], "mood": c["mood"],
            "performers": performers(j, c), "source": j["descriptionUrl"], "hitGuess": cutinfo["hitGuess"],
            "evidence": {"sourceSha256": sha256, "page": j["descriptionUrl"], "record": f"evidence/{c['id']}.json",
                         "note": f"The Wikimedia Commons file page dedicates this recording under CC0 1.0 ({{{{cc-zero}}}}, revision {j['revision']}, "
                                 f"fetched {day}); its licence metadata and wikitext are in evidence/{c['id']}.json, and the rendered page in the "
                                 f"intake's artifact. Seconds {cutinfo['window'][0]}-{cutinfo['window'][1]} of the original; the hit at "
                                 f"{clock(cutinfo['hitInSource'])} of it (found within {clock(cutinfo['analysis']['range'][0])}-"
                                 f"{clock(cutinfo['analysis']['range'][1])}), meant as {c['hit']['note']}."}}


def intake(args) -> int:
    for tool in ("ffmpeg", "ffprobe"):
        if not shutil.which(tool):
            print(f"{tool} isn't installed", file=sys.stderr)
            return 3
    SOURCES.mkdir(parents=True, exist_ok=True)
    evidence_dir = args.out / "evidence"
    evidence_dir.mkdir(parents=True, exist_ok=True)
    report = {"kept": [], "refused": [], "nearMisses": {}, "log": [], "scan": {},
              "fetched": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    drafts = []
    # E.6's own table first, then the rest; at most MAX_TRACKS (the repo carries every one).
    request = json.loads(strip_comments(args.request.read_text())) if args.request and args.request.exists() else {}
    only = set(args.only or [c for c in request.get("candidates", ["all"]) if c != "all"])
    unknown = only - {c["id"] for c in CANDIDATES}
    if unknown:
        print(f"no such candidates: {sorted(unknown)}", file=sys.stderr)
        return 4
    most = int(request.get("maxTracks", MAX_TRACKS))
    try:
        files = scan([c for c in CANDIDATES if not only or c["id"] in only], report["scan"])
    except OSError as e:
        print(json.dumps({"blocked": True, "host": "commons.wikimedia.org", "error": str(e)}))
        print("Commons can't be reached from here: run .github/workflows/music-intake.yml (E.6's fallback stays).", file=sys.stderr)
        return 2
    # Every candidate's best CC0 file first (E.6's own table, then the named extras, then the open pool, each in list
    # order), a file to one candidate only; then the picks, a mood at a time in turn so the pool stays balanced.
    used: set[str] = set()
    found: dict[str, list] = {m: [] for m in MOODS}
    order = sorted(CANDIDATES, key=lambda c: (bool(c.get("open")), not c.get("e6"), CANDIDATES.index(c)))
    for c in order:
        if only and c["id"] not in only:
            continue
        try:
            j = find(c, files, used, report["log"], report["nearMisses"])
        except OSError as e:
            print(json.dumps({"blocked": True, "host": "commons.wikimedia.org", "error": str(e)}))
            print("Commons can't be reached from here: run .github/workflows/music-intake.yml (E.6's fallback stays).", file=sys.stderr)
            return 2
        if j is None:
            if not c.get("open"):
                report["refused"].append({"id": c["id"], "why": "no CC0 recording found"})
            continue
        used.add(j["title"])
        found[c["mood"]].append((c, j))
    report["found"] = {m: [c["id"] for c, _ in found[m]] for m in MOODS}
    while len(drafts) < most and any(found.values()):
        for mood in MOODS:
            if len(drafts) >= most or not found[mood]:
                continue
            c, j = found[mood].pop(0)
            if (d := take_in(c, j, args, report, evidence_dir)) is not None:
                drafts.append(d)
    (SOURCES / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n")
    drafts_path = SOURCES / "drafts.json"
    drafts_path.write_text(json.dumps({"tracks": drafts}, indent=2, ensure_ascii=False) + "\n")
    print(json.dumps({k: report[k] for k in ("kept", "refused", "found", "scan")}, indent=2, ensure_ascii=False))
    log_table(report)
    summary(report)
    if args.no_finish:
        return 0
    # A full run is the whole set: what it didn't retake (a mislabel from before) goes. A run of --only some keeps the rest.
    return finish(args.dt, args.out, drafts_path, replace=not only)


def take_in(c: dict, j: dict, args, report: dict, evidence_dir: Path) -> dict | None:
    """Downloads a pick, checks it against Commons' SHA-1, archives its page, cuts it, and writes its evidence; its draft, or None."""
    c = {**c, "hit": j.get("_hit", c["hit"])}
    work = SOURCES / c["id"]
    work.mkdir(parents=True, exist_ok=True)
    source = work / Path(urllib.parse.unquote(urllib.parse.urlparse(j["url"]).path)).name
    try:
        sha256 = download(j["url"], source)
    except OSError as e:
        report["refused"].append({"id": c["id"], "title": j["title"], "why": f"download failed: {e}"})
        return None
    sha1 = hashlib.sha1(source.read_bytes()).hexdigest()
    if j["sha1"] and sha1 != j["sha1"]:
        report["refused"].append({"id": c["id"], "title": j["title"], "why": f"the download's SHA-1 {sha1} isn't Commons' {j['sha1']}"})
        return None
    (work / "page.html").write_text(archive_page(j["title"]))
    (work / "wikitext.txt").write_text(j["_wikitext"])
    (work / "extmetadata.json").write_text(json.dumps(j["_extmetadata"], indent=2, ensure_ascii=False) + "\n")
    try:
        cutinfo = take(c, source, args.out)
    except (ValueError, subprocess.CalledProcessError) as e:
        report["refused"].append({"id": c["id"], "title": j["title"], "why": str(e)})
        return None
    record = {k: v for k, v in j.items() if not k.startswith("_")}
    record |= {"id": c["id"], "work": c["work"], "composer": c["composer"], "sourceSha256": sha256, "fetched": report["fetched"],
               "licenceTemplates": sorted(set(m.group(1).lower() for m in CC0.finditer(j["_wikitext"]))),
               "licenceSection": licence_section(j["_wikitext"]), "information": information(j["_wikitext"]),
               # The page's wikitext as fetched (the performer parse is checked against it offline), up to 20 KB.
               "wikitext": j["_wikitext"][:20000],
               "performers": performers(j, c), "hitNote": c["hit"]["note"], "hitClock": clock(cutinfo["hitInSource"]), **cutinfo}
    text = json.dumps(record, indent=2, ensure_ascii=False) + "\n"
    (evidence_dir / f"{c['id']}.json").write_text(text)
    (work / "evidence.json").write_text(text)
    report["kept"].append({"id": c["id"], "title": j["title"], "performers": performers(j, c), "window": cutinfo["window"],
                           "hitInSource": cutinfo["hitInSource"], "hitClock": clock(cutinfo["hitInSource"]), "hitCheck": cutinfo["hitCheck"],
                           "note": c["hit"]["note"]})
    return draft(c, j, sha256, cutinfo)


def log_table(report: dict, out=sys.stderr) -> None:
    """The kept tracks, the refusals and the near misses, as lines for the job log (people read logs, not artifacts)."""
    print(f"\n== Kept {len(report['kept'])} (scan: {report.get('scan')})", file=out)
    for k in report["kept"]:
        chk = k["hitCheck"]
        print(f"  {k['id']:36} hit {k['hitClock']:>7} jump {chk['jumpDb']:+5.1f} dB rank {chk['loudnessRank']:.2f}"
              f"{'  WEAK' if chk['weak'] else ''}  | {k['performers'][:40]} | {k['title']}", file=out)
    print(f"== Refused {len(report['refused'])}", file=out)
    for r in report["refused"]:
        print(f"  {r['id']:36} {r.get('title', '')} : {r['why']}", file=out)
    print(f"== Near misses (matching files that aren't CC0): {sum(len(v) for v in report['nearMisses'].values())}", file=out)
    for cid, misses in report["nearMisses"].items():
        for m in misses[:6]:
            print(f"  {cid:36} {m['title']} : {m['licence'] or '-'} : {'; '.join(m['why'])}", file=out)
    print("== Pinned files", file=out)
    for entry in report["log"]:
        for t in entry.get("pinned", []):
            print(f"  {entry['id']:36} {t['title']} : {'CC0' if t['cc0'] else 'refused: ' + '; '.join(t['refused'])}", file=out)


def summary(report: dict, out=sys.stdout) -> None:
    """The short table for the end of the log: id | hit | jump | rank | WEAK | performers."""
    print(f"== Music intake: {len(report['kept'])} kept, {len(report['refused'])} refused", file=out)
    print("id | hit | jump dB | rank | weak | performers", file=out)
    for k in report["kept"]:
        chk = k["hitCheck"]
        print(f"{k['id']} | {k['hitClock']} | {chk['jumpDb']:+.1f} | {chk['loudnessRank']:.2f} | {'WEAK' if chk['weak'] else '-'} | {k['performers']}",
              file=out)


def strip_comments(text: str) -> str:
    """The request file is JSON with // comments, like everything in content/."""
    return "\n".join(l for l in text.splitlines() if not l.lstrip().startswith("//"))


def information(wikitext: str) -> str:
    """The page's {{Information}} (or {{Musopen}}...) block: description, author, source, as written."""
    m = re.search(r"\{\{\s*(information|musopen|artwork|sound)\b.*?\n\}\}", wikitext, re.I | re.S)
    return (m.group(0) if m else "")[:3000]


def finish(dt: str, out: Path, drafts_path: Path, replace: bool = False) -> int:
    cmd = dt.split() + ["audio", "music", "--drafts", str(drafts_path), "--folder", str(out)] + (["--replace"] if replace else [])
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
    fake = {"artist": "The offline test", "credit": "", "description": "", "descriptionUrl": "https://commons.wikimedia.org/wiki/File:Made_up.wav",
            "revision": 0, "_wikitext": "", "user": "Nobody"}
    d = draft(cand, fake, hashlib.sha256(src.read_bytes()).hexdigest(), info)
    (out / "evidence").mkdir()
    (out / "evidence" / f"{cand['id']}.json").write_text(json.dumps({"offlineTest": True, **info}, indent=2) + "\n")
    drafts_path = tmp / "drafts.json"
    drafts_path.write_text(json.dumps({"tracks": [d]}, indent=2) + "\n")
    result = {"tmp": str(tmp), "hitInSource": info["hitInSource"], "window": info["window"], "normalization": info["normalization"],
              "bytes": (out / d["file"]).stat().st_size}
    ok = abs(info["hitInSource"] - 41.3) < 0.15
    if not args.no_finish:
        code = finish(args.dt, out, drafts_path, replace=True)
        manifest = (out / "manifest.json").read_text() if (out / "manifest.json").exists() else ""
        body = json.loads(manifest[manifest.index("{"):]) if manifest else {"tracks": []}
        track = next((t for t in body["tracks"] if t["id"] == cand["id"]), None)
        result |= {"dt": code, "manifest": track}
        ok = ok and code == 0 and track is not None and abs(track["hit"] - info["hitGuess"]) <= REFINE + 0.01 \
            and abs(track["loudnessLufs"] + track["gainDb"] + (track.get("shortfallDb") or 0) - LUFS) < 0.06 \
            and (track.get("shortfallDb") or 0) <= 3 and (out / "CREDITS.md").exists()
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
    ap.add_argument("--summary", action="store_true", help="print the last intake's kept tracks (intake/_sources/music/report.json)")
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
    if args.summary:
        report = json.loads((SOURCES / "report.json").read_text())
        log_table(report, sys.stdout)
        summary(report)
        return 0
    if args.search:
        return search_report()
    return intake(args)


if __name__ == "__main__":
    sys.exit(main())
