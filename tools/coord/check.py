#!/usr/bin/env python3
"""The coordination docs' numbers (ARCHITECTURE §8 note 521): a pull request may not bring in a queue number or an
ARCHITECTURE §8 note number that's already taken, or a second "next free number" line. Concurrent appends to these files
merge on their own (.gitattributes: merge=union), so two agents' claims can both land without a conflict to say so; this
says so instead.

Usage: tools/coord/check.py [--base DIR]   (DIR holds the base branch's docs/COORDINATION.md and docs/ARCHITECTURE.md;
                                           only what's new against them fails, so old collisions don't block anyone)
       tools/coord/check.py --next         the next free queue number and note number, as main's docs say
"""
import argparse, collections, json, os, re, sys

def queue_numbers(text):
    """Every row's number in COORDINATION.md's queue table."""
    m = re.search(r'^## The queue\n(.*?)^## ', text, re.S | re.M)
    body = m.group(1) if m else ''
    return [int(n) for n in re.findall(r'^\| (\d+) \|', body, re.M)]

def note_numbers(text):
    """Every ARCHITECTURE §8 note's number (a line that starts a note: `514. **`)."""
    i = text.find('\n## 8.')
    return [int(n) for n in re.findall(r'^(\d{1,4})\. \*\*', text[i:] if i >= 0 else text, re.M)]

def held(text):
    """The note numbers the "next free number" line says are held, and its next free."""
    lines = re.findall(r'^The next free number is \*\*(\d+)\*\*(.*)$', text, re.M)
    return lines

def duplicates(xs):
    return sorted(n for n, c in collections.Counter(xs).items() if c > 1)

def read(path):
    with open(path, encoding='utf-8') as f:
        return f.read()

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--base')
    ap.add_argument('--next', action='store_true')
    ap.add_argument('--root', default='.')
    a = ap.parse_args()
    coord = read(os.path.join(a.root, 'docs/COORDINATION.md'))
    arch = read(os.path.join(a.root, 'docs/ARCHITECTURE.md'))
    queue, notes, free = queue_numbers(coord), note_numbers(arch), held(coord)
    if a.next:
        held_notes = [int(n) for n in re.findall(r'holds? (\d+)', ' '.join(f[1] for f in free))]
        top_note = max([int(f[0]) - 1 for f in free] + notes + held_notes + [0])
        print(json.dumps({'queue': max(queue + [0]) + 1, 'note': top_note + 1}))
        return 0
    problems = []
    if len(free) != 1:
        problems.append(f'COORDINATION.md has {len(free)} "The next free number is" lines (two claims merged together: keep one, '
                        'with every holding in it)')
    old_queue = old_notes = set()
    if a.base:
        old_queue = set(duplicates(queue_numbers(read(os.path.join(a.base, 'docs/COORDINATION.md')))))
        old_notes = set(duplicates(note_numbers(read(os.path.join(a.base, 'docs/ARCHITECTURE.md')))))
    for n in duplicates(queue):
        if n not in old_queue:
            problems.append(f'queue #{n} is two items: renumber the newer one to the next free queue number')
    for n in duplicates(notes):
        if n not in old_notes:
            problems.append(f'ARCHITECTURE §8 note {n} is two notes: renumber the newer one to the next free note number')
    # A queue row outside the table (a merge that put rows after the table's end) reads as text, not as an item.
    m = re.search(r'^## The queue\n(.*?)^## ', coord, re.S | re.M)
    if m:
        lines = m.group(1).split('\n')
        in_table = False
        for line in lines:
            if line.startswith('| # |'):
                in_table = True
            elif in_table and line.strip() and not line.startswith('|'):
                in_table = False
            elif not in_table and re.match(r'^\| \d+ \|', line):
                problems.append(f'a queue row outside the queue table: "{line[:60]}…"')
    for p in problems:
        print(f'coord: {p}', file=sys.stderr)
    print(json.dumps({'queue': len(queue), 'notes': len(notes), 'problems': problems}))
    return 1 if problems else 0

if __name__ == '__main__':
    sys.exit(main())
