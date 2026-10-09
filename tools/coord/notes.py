#!/usr/bin/env python3
"""ARCHITECTURE §8 as one file per note (note 521). §8 had grown to 520 notes and 1.6 MB, more than any agent can read, and
every agent appending to the end of one file is where most merge conflicts came from. Each note now lives in
docs/notes/<number>.md, written exactly as it was in §8 (it starts `<number>. **Title.**`), and §8 is an index of them that
this tool writes. An agent reads the notes it needs; two agents taking one number write one file, which git refuses to
merge on its own (not a silent duplicate).

Usage: tools/coord/notes.py split     move every note written in ARCHITECTURE.md §8 into docs/notes/, and write the index
       tools/coord/notes.py index     write §8's index from docs/notes/ (after adding or retitling a note)
       tools/coord/notes.py check     the index lists every note file and nothing else, and no note is written in §8 itself
       tools/coord/notes.py show N…   print notes N… (`show 515 516`)
       tools/coord/notes.py --offline-test
Options: --root DIR (the repository; default: this checkout)
"""
import argparse, os, re, sys, tempfile

HEADING = '## 8. Open technical questions'
NOTE = re.compile(r'^(\d{1,4})\. \*\*', re.M)
BEGIN = '<!-- notes index: written by tools/coord/notes.py index; a line per file in docs/notes/ -->'
END = '<!-- end of the notes index -->'
PREAMBLE = """Each note is a file of its own in [docs/notes/](notes/) (note 521): read the ones your work touches
(`tools/coord/notes.py show 515`, or open the file). To add one, write `docs/notes/<number>.md` starting
`<number>. **Title.**`, with the number you claimed (docs/COORDINATION.md), and run `tools/coord/notes.py index`. A note
written here instead is moved out by `tools/coord/notes.py split`; the Coordination check asks for it."""


def paths(root):
    return os.path.join(root, 'docs', 'ARCHITECTURE.md'), os.path.join(root, 'docs', 'notes')


def section(text):
    """§8's start and end in ARCHITECTURE.md (its body runs to the next level-2 heading or the end)."""
    i = text.find('\n' + HEADING + '\n')
    if i < 0:
        raise SystemExit(f'notes: no "{HEADING}" heading in ARCHITECTURE.md')
    start = i + len(HEADING) + 2
    m = re.search(r'^## ', text[start:], re.M)
    return start, start + m.start() if m else len(text)


def written(body):
    """The notes written out in §8's body: (number, text) in the order they stand, and whatever isn't a note or the index."""
    lines, notes, other, cur = body.split('\n'), [], [], None
    in_index = False
    for line in lines:
        if line.strip() == BEGIN:
            in_index, cur = True, None
            continue
        if line.strip() == END:
            in_index = False
            continue
        if in_index:
            continue
        m = NOTE.match(line)
        if m:
            cur = [int(m.group(1)), [line]]
            notes.append(cur)
        elif cur is not None and (line.startswith(' ') or line == ''):
            cur[1].append(line)
        elif line.strip():
            cur = None
            other.append(line)
    return [(n, '\n'.join(t).rstrip() + '\n') for n, t in notes], other


def title(text):
    """The note's bold title, short enough for an index line."""
    m = re.match(r'^\d{1,4}\. \*\*(.+?)\*\*', text)
    t = (m.group(1) if m else text.split('\n', 1)[0]).strip().rstrip('.:')
    if len(t) > 140:
        t = t[:140].rsplit(' ', 1)[0] + '…'
    return t


def files(notes_dir):
    """Every note file: number -> text."""
    out = {}
    if os.path.isdir(notes_dir):
        for name in os.listdir(notes_dir):
            m = re.fullmatch(r'(\d{1,4})\.md', name)
            if m:
                with open(os.path.join(notes_dir, name), encoding='utf-8') as f:
                    out[int(m.group(1))] = f.read()
    return out


def index_lines(notes):
    return [f'- [{n}. {title(t)}](notes/{n}.md)' for n, t in sorted(notes.items())]


def write_index(arch, notes_dir, extra=()):
    with open(arch, encoding='utf-8') as f:
        text = f.read()
    start, end = section(text)
    _, other = written(text[start:end])
    other = [l for l in other if l not in PREAMBLE.split('\n')]
    body = '\n'.join(['', PREAMBLE, '', *other, *([''] if other else []), BEGIN, *index_lines(files(notes_dir)), END, ''])
    text = text[:start] + body + ('\n' + text[end:] if end < len(text) else '')
    with open(arch, 'w', encoding='utf-8') as f:
        f.write(text)


def split(root):
    arch, notes_dir = paths(root)
    with open(arch, encoding='utf-8') as f:
        text = f.read()
    start, end = section(text)
    notes, _ = written(text[start:end])
    have = files(notes_dir)
    os.makedirs(notes_dir, exist_ok=True)
    clash = []
    seen = {}
    for n, t in notes:
        if n in seen and seen[n] != t:
            clash.append(f'note {n} is written twice in ARCHITECTURE.md §8 with different text: renumber the newer one')
            continue
        seen[n] = t
        if n in have and have[n] != t:
            clash.append(f'note {n} is in ARCHITECTURE.md §8 and in docs/notes/{n}.md, and they differ: '
                         f'put the change into docs/notes/{n}.md (or renumber the newer note) and delete it from §8')
    if clash:
        for c in clash:
            print(f'notes: {c}', file=sys.stderr)
        return 1
    for n, t in seen.items():
        with open(os.path.join(notes_dir, f'{n}.md'), 'w', encoding='utf-8') as f:
            f.write(t)
    write_index(arch, notes_dir)
    print(f'notes: {len(seen)} moved into docs/notes/, {len(files(notes_dir))} indexed')
    return 0


def problems(root):
    arch, notes_dir = paths(root)
    with open(arch, encoding='utf-8') as f:
        text = f.read()
    start, end = section(text)
    body = text[start:end]
    if BEGIN not in body:
        return []  # not split yet: the notes are still written in §8, as they were before note 521
    out = []
    notes, _ = written(body)
    for n, _t in notes:
        out.append(f'note {n} is written in ARCHITECTURE.md §8: run tools/coord/notes.py split to move it into docs/notes/')
    have = files(notes_dir)
    listed = [int(m) for m in re.findall(r'\]\(notes/(\d{1,4})\.md\)', body[body.index(BEGIN):])]
    for n in sorted(set(have) - set(listed)):
        out.append(f'docs/notes/{n}.md is not in §8\'s index: run tools/coord/notes.py index')
    for n in sorted(set(listed) - set(have)):
        out.append(f'§8\'s index lists note {n}, but docs/notes/{n}.md is missing')
    for n, t in have.items():
        m = NOTE.match(t)
        if not m or int(m.group(1)) != n:
            out.append(f'docs/notes/{n}.md must start "{n}. **Title.**"')
    return out


def offline_test():
    """The split and the check on a small ARCHITECTURE.md, in a scratch directory."""
    with tempfile.TemporaryDirectory() as root:
        os.makedirs(os.path.join(root, 'docs'))
        arch = os.path.join(root, 'docs', 'ARCHITECTURE.md')
        doc = ('# Ballast\n\n## 7. Not in scope\nNothing.\n\n' + HEADING + '\n'
               '1. **Standalone Quest** (Android) at launch? PCVR is assumed.\n'
               '2. **Brake fade on the flat**: spec B.5.\n    - **A detail** under it.\n\n        indented more\n\n'
               '515. **Record and replay a night (W1, queue #252).** Every night recorded.\n    - **Verified:** tests.\n')
        with open(arch, 'w', encoding='utf-8') as f:
            f.write(doc)
        assert problems(root) == [], 'an unsplit file is fine as it stands'
        assert split(root) == 0
        have = files(os.path.join(root, 'docs', 'notes'))
        assert sorted(have) == [1, 2, 515], have
        assert have[2] == '2. **Brake fade on the flat**: spec B.5.\n    - **A detail** under it.\n\n        indented more\n', repr(have[2])
        text = open(arch, encoding='utf-8').read()
        assert '## 7. Not in scope\nNothing.' in text and '- [515. Record and replay a night (W1, queue #252)](notes/515.md)' in text, text
        assert problems(root) == [], problems(root)
        assert split(root) == 0 and open(arch, encoding='utf-8').read() == text, 'splitting again changes nothing'
        # An agent adds a note the old way (or a merge from an old branch brings one): the check asks for the split.
        with open(arch, 'a', encoding='utf-8') as f:
            f.write('550. **A new note.** Its text.\n')
        assert any('note 550 is written in ARCHITECTURE.md' in p for p in problems(root)), problems(root)
        assert split(root) == 0 and problems(root) == [] and 550 in files(os.path.join(root, 'docs', 'notes'))
        # Two merged indexes (union keeps both lines, out of order) still pass: the check is on the set, not the order.
        lines = open(arch, encoding='utf-8').read().split('\n')
        i = lines.index(BEGIN)
        lines[i + 1], lines[i + 2] = lines[i + 2], lines[i + 1]
        open(arch, 'w', encoding='utf-8').write('\n'.join(lines))
        assert problems(root) == [], problems(root)
        # A file added without the index, or one that names another number.
        with open(os.path.join(root, 'docs', 'notes', '551.md'), 'w', encoding='utf-8') as f:
            f.write('552. **Wrong number.**\n')
        p = problems(root)
        assert any('551.md is not in' in x for x in p) and any('must start "551.' in x for x in p), p
        # The same note changed on an old branch after the split: the split refuses to guess.
        with open(arch, 'a', encoding='utf-8') as f:
            f.write('515. **Record and replay a night (W1, queue #252).** Every night recorded, and more.\n')
        assert split(root) == 1
    print('notes: offline test passed')
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('what', nargs='?', choices=['split', 'index', 'check', 'show'])
    ap.add_argument('numbers', nargs='*', type=int)
    ap.add_argument('--root', default=os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
    ap.add_argument('--offline-test', action='store_true')
    a = ap.parse_args()
    if a.offline_test:
        return offline_test()
    arch, notes_dir = paths(a.root)
    if a.what == 'split':
        return split(a.root)
    if a.what == 'index':
        write_index(arch, notes_dir)
        return 0
    if a.what == 'check':
        p = problems(a.root)
        for x in p:
            print(f'notes: {x}', file=sys.stderr)
        return 1 if p else 0
    if a.what == 'show':
        have = files(notes_dir)
        if not have:
            with open(arch, encoding='utf-8') as f:
                text = f.read()
            s, e = section(text)
            have = dict(written(text[s:e])[0])
        for n in a.numbers:
            print(have.get(n, f'(no note {n})\n'))
        return 0
    ap.print_help()
    return 2


if __name__ == '__main__':
    sys.exit(main())
