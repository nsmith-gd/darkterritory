#!/usr/bin/env python3
"""The Look Review's stills, packed per subject (note 967).

The Look Review (claude.ai/artifact/MgW84RexYLg3JVBC52XoXm) shows one card per subject: its newest round large, older
rounds folded under History. Its images live in still packs the page fetches as a card nears the screen
(`<img data-pack="TOKEN:id">` -> stills/still-pack-TOKEN.json), two per subject: TOKEN = the subject (its newest round,
what the director judges) and TOKEN-old (everything under History, fetched only when it's opened). That keeps the page
itself near 1 MB and well inside a version's 512 files, and the first image of a card arrives with one small fetch.

A round may still be posted the easy way, its images inline (`data:` JPEG) or as `stills/<name>.jpg` files; this
re-packs the whole page, so whoever finds the page past a few MB (or near 400 files) runs it before publishing:

  1. Read the live page with the Artifact tool, then every still it uses (`paths`, out_dir DIR): the packs and any
     stills/*.jpg it names, so DIR/stills/ holds them.
  2. python3 tools/review/look_review.py --page PAGE.html --files DIR --out OUT [--ui]
     (--ui: the verdict cards' CSS and script, tools/review/look_review/, put in or brought up to date)
  3. Publish OUT/index.html to the same url with OUT/files.json as `files` (the new packs, and null for the packs and
     stills it replaced).

--check only reports the page's size, its images by kind and what a re-pack would write.
"""
import argparse
import base64
import hashlib
import json
import os
import re
import sys

IMG = re.compile(r'<img\b[^>]*>')
SRC_DATA = re.compile(r'\ssrc="data:image/[a-z]+;base64,([^"]+)"')
SRC_FILE = re.compile(r'\ssrc="(stills/[^"]+)"')
PACK = re.compile(r'\sdata-pack="([^":]+):([^"]+)"')


def load_packs(files):
    packs = {}
    d = os.path.join(files, 'stills')
    for name in sorted(os.listdir(d)) if os.path.isdir(d) else []:
        m = re.fullmatch(r'still-pack-(.+)\.json', name)
        if m:
            with open(os.path.join(d, name)) as f:
                packs[m.group(1)] = json.load(f)
    return packs


def captures_span(page):
    a = page.index('id="panel-captures"')
    b = page.index('id="panel-log"')
    return a, b


def sections(page, panel, cls, prefix):
    """Each `<section class=cls>` in a panel: (start, end, token), in page order; the last runs to the panel's end."""
    a = page.index(f'id="panel-{panel}"')
    b = page.find('<div class="panel"', a + 1)
    b = len(page) if b < 0 else b
    starts = [m.start() for m in re.finditer(f'<section class="{cls}"', page[a:b])]
    out = []
    for i, s in enumerate(starts):
        s += a
        e = a + starts[i + 1] if i + 1 < len(starts) else b
        sid = re.match(f'<section class="{cls}" id="([^"]+)"', page[s:]).group(1)
        out.append((s, e, re.sub(f'^{prefix}', '', sid)))
    return out


def subjects(page):
    return sections(page, 'captures', 'subject', 'cap-')


def current_spans(section):
    """The spans of a subject section that are its newest round: the latest version of each article whose round
    (its notes box's data-version) is the first article's. Everything else in the section is History."""
    arts = [(m.start(), m.end(), m.group(0)) for m in re.finditer(r'<article class="shot[^"]*" id="[^"]+">.*?</article>', section, re.S)]
    if not arts:
        return []
    def rnd(body):
        m = re.search(r'data-version="([^"]*)"', body)
        return m.group(1) if m else ''
    first = rnd(arts[0][2])
    spans = []
    for s, e, body in arts:
        if rnd(body) != first:
            continue
        h = body.find('<details class="history">')
        spans.append((s, s + (h if h >= 0 else len(body))))
    return spans


def repack(page, files, packs):
    out_packs = {}
    missing = []
    def b64_of(tag):
        m = PACK.search(tag)
        if m:
            v = packs.get(m.group(1), {}).get(m.group(2))
            if v is None:
                missing.append(m.group(0).strip())
            return v
        m = SRC_DATA.search(tag)
        if m:
            return m.group(1)
        m = SRC_FILE.search(tag)
        if m:
            p = os.path.join(files, m.group(1))
            if not os.path.exists(p):
                missing.append(m.group(1))
                return None
            with open(p, 'rb') as f:
                return base64.b64encode(f.read()).decode()
        return None

    # Captures: the subject's newest round, and its History. Animations: a model's packed clips (most are anim/*.webp
    # files, left as they are).
    spans = [(s, e, t, True) for s, e, t in subjects(page)] + [(s, e, 'clips-' + t, False) for s, e, t in sections(page, 'animations', 'model', 'anim-')]
    pieces, last = [], 0
    for s, e, token, split in sorted(spans):
        sec = page[s:e]
        cur = current_spans(sec) if split else [(0, len(sec))]
        def bucket(pos):
            return token if any(a <= pos < b for a, b in cur) else token + '-old'
        def sub(m):
            tag = m.group(0)
            v = b64_of(tag)
            if v is None:
                return tag
            key = hashlib.sha1(v.encode()).hexdigest()[:14]
            t = bucket(m.start())
            out_packs.setdefault(t, {})[key] = v
            tag = SRC_DATA.sub('', tag)
            tag = SRC_FILE.sub('', tag)
            tag = PACK.sub('', tag)
            return tag[:-1].rstrip('/').rstrip() + f' data-pack="{t}:{key}">'
        pieces.append(page[last:s])
        pieces.append(IMG.sub(sub, sec))
        last = e
    pieces.append(page[last:])
    return ''.join(pieces), out_packs, missing


HERE = os.path.dirname(os.path.abspath(__file__))
CSS_MARK = '/* Verdicts (note 967): one card per subject'
JS_MARK = '<script>\n/* Verdicts (note 967;'


def bars(panel):
    return (f'<div class="vfilters-wrap"><div class="vfilters" data-panel="{panel}" role="group" aria-label="Show">'
            '<button type="button" data-f="awaiting" aria-pressed="true">Awaiting you <b></b></button>'
            '<button type="button" data-f="change" aria-pressed="false">Changes asked <b></b></button>'
            '<button type="button" data-f="redo" aria-pressed="false">Redo <b></b></button>'
            '<button type="button" data-f="approve" aria-pressed="false">Approved <b></b></button>'
            '<button type="button" data-f="all" aria-pressed="false">All <b></b></button>'
            '</div><p class="vfilters-empty" hidden></p></div>\n')


def with_ui(page):
    """The verdict cards (tools/review/look_review/verdict.css, verdict.js) put into the page, or brought up to date in it."""
    with open(os.path.join(HERE, 'look_review', 'verdict.css')) as f:
        css = f.read()
    with open(os.path.join(HERE, 'look_review', 'verdict.js')) as f:
        js = '<script>\n' + f.read() + '</script>\n'
    if CSS_MARK in page:
        a = page.index(CSS_MARK); b = page.index('</style>', a)
        page = page[:a] + css + page[b:]
        a = page.index(JS_MARK); b = page.index('</script>\n', a) + len('</script>\n')
        return page[:a] + js + page[b:]
    page = page.replace('  --mono: "JetBrains Mono", ui-monospace, Menlo, Consolas, monospace;\n',
                        '  --mono: "JetBrains Mono", ui-monospace, Menlo, Consolas, monospace;\n  --ok: #8fbf8a;\n  --warn: #d9c26a;\n  --bad: #e07a5f;\n', 1)
    i = page.index('</style>\n<div class="wrap">')
    page = page[:i] + css + page[i:]
    page = re.sub(r'<p class="lede">.*?</p>', lambda m: (
        '<p class="lede">Captures from the headless renderer (software Vulkan, 1280×720), one card per subject with its <strong>newest</strong> shot on top. '
        '<strong>Approve</strong> it, or ask for a <strong>Change</strong> or a <strong>Redo</strong>; the filters show what is waiting for you. '
        'Drag across a <strong>before/after</strong> to compare. Each shot lists the <code>dt</code> command that reproduces it. '
        'Target: the 2008–2012 benchmarks (BioShock 2, Silent Hill 4, Dead Space, RE Revelations).</p>'), page, count=1, flags=re.S)
    page = re.sub(r'  <header class="round-head"><h2>Every capture, by what it shows</h2><p class="note">.*?</p></header>\n', lambda m: (
        '  <header class="round-head"><h2>Every subject, its newest round first</h2><p class="note">One card per subject: its newest shot large, '
        'the rest of that round beside it, and every earlier round folded under History. Under the newest shot, <strong>Approve</strong> it, '
        'ask for a <strong>Change</strong> or a <strong>Redo</strong> (both with a note). The art team acts on your verdict and posts a new latest, '
        'which comes back here as <em>New since your verdict</em>. Notes boxes under every shot still take free notes.</p></header>\n' + bars('captures')),
        page, count=1, flags=re.S)
    m = re.search(r'<div class="panel" id="panel-animations"[^>]*>\n  <header class="round-head">\n.*?</header>\n', page, re.S)
    page = page[:m.end()] + bars('animations') + page[m.end():]
    page = page.replace('Every clip has a notes box under it, and each model has one for the model as a whole.</p>',
                        'Each model takes a verdict as a whole, and each clip its own (Approve, Change, Redo); every clip still has a notes box '
                        'under it, and each model one for the model as a whole.</p>', 1)
    page = re.sub(r'(aria-controls="panel-captures" aria-selected="true">Captures<span class="count">)\d+',
                  lambda m: m.group(1) + str(len(subjects(page))), page, count=1)
    page = page.replace("    el.closest('details')?.setAttribute('open', '');\n",
                        "    for (let d = el.closest('details'); d; d = d.parentElement?.closest('details')) d.open = true;\n", 1)
    page = re.sub(r"/\* The older rounds. stills live in stills/still-pack-N\.json \(a few files, not hundreds: one version may hold 512\),",
                  '/* The stills live in stills/still-pack-TOKEN.json, two per subject (its newest round, and TOKEN-old for History; note 967, tools/review/look_review.py),', page, count=1)
    i = page.index('<script>\n/* Tabs and deep links')
    return page[:i] + js + page[i:]


def report(page, label):
    a, b = captures_span(page)
    cap = page[a:b]
    print(json.dumps({
        'page': label, 'bytes': len(page.encode()),
        'inline': len(SRC_DATA.findall(cap)), 'files': len(SRC_FILE.findall(cap)), 'packed': len(PACK.findall(cap)),
        'subjects': len(subjects(page)),
    }))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--page', required=True)
    ap.add_argument('--files', help='where the live page\'s stills/ were saved')
    ap.add_argument('--out')
    ap.add_argument('--check', action='store_true')
    ap.add_argument('--ui', action='store_true', help='also put in (or update) the verdict cards from tools/review/look_review/')
    a = ap.parse_args()
    with open(a.page) as f:
        page = f.read()
    report(page, a.page)
    if a.check or not a.out:
        return
    packs = load_packs(a.files)
    new, out_packs, missing = repack(with_ui(page) if a.ui else page, a.files, packs)
    if missing:
        sys.exit('not found, nothing written: ' + ', '.join(sorted(set(missing))[:20]))
    os.makedirs(os.path.join(a.out, 'stills'), exist_ok=True)
    # History packs over ~3 MB are cut in order (TOKEN-old, TOKEN-old2, ...): opening a History fetches what it shows.
    for t in [t for t in out_packs if t.endswith('-old')]:
        chunks, cur, size = [], {}, 0
        for k, v in out_packs[t].items():
            if cur and size + len(v) > 3_000_000:
                chunks.append(cur); cur, size = {}, 0
            cur[k] = v; size += len(v)
        chunks.append(cur)
        if len(chunks) > 1:
            del out_packs[t]
            for i, c in enumerate(chunks):
                out_packs[t + (str(i + 1) if i else '')] = c
            new = re.sub(f'data-pack="{re.escape(t)}:([0-9a-f]+)"',
                         lambda m: f'data-pack="{next(n for n, c in out_packs.items() if n.startswith(t) and m.group(1) in c)}:{m.group(1)}"', new)
    with open(os.path.join(a.out, 'index.html'), 'w') as f:
        f.write(new)
    files = {}
    for t, p in sorted(out_packs.items()):
        path = f'stills/still-pack-{t}.json'
        with open(os.path.join(a.out, path), 'w') as f:
            json.dump(p, f, separators=(',', ':'))
        files[path] = os.path.join(a.out, path)
    # What the old page held and the new one doesn't name any more: removed on publish.
    old = {f'stills/still-pack-{t}.json' for t in packs} | set(SRC_FILE.findall(page))
    for path in sorted(old - set(files)):
        files[path] = None
    with open(os.path.join(a.out, 'files.json'), 'w') as f:
        json.dump(files, f, indent=1)
    report(new, os.path.join(a.out, 'index.html'))
    sizes = sorted((os.path.getsize(v), k) for k, v in files.items() if v)
    print(json.dumps({'packs': len(sizes), 'removed': sum(1 for v in files.values() if v is None),
                      'largest': [[k, n] for n, k in sizes[-3:]], 'total': sum(n for n, _ in sizes)}))


if __name__ == '__main__':
    main()
