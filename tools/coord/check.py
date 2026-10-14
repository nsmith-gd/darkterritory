#!/usr/bin/env python3
"""The coordination docs' numbers (ARCHITECTURE §8 note 521): a pull request may not bring in a queue number or an
ARCHITECTURE §8 note number that's already taken, a number someone else claimed on the `claims` branch (tools/coord/claim.py),
or a second "next free number" line. Concurrent appends to these files merge on their own (.gitattributes: merge=union), so
two agents' claims can both land without a conflict to say so; this says so instead. With §8 split into docs/notes/
(tools/coord/notes.py), its index is checked too.

Usage: tools/coord/check.py [--base DIR | --base-ref REF] [--claims-ref REF]
           DIR holds the base branch's docs (COORDINATION.md, ARCHITECTURE.md, notes/), or REF names the base commit: only
           what's new against it fails, so old collisions don't block anyone. --claims-ref (e.g. origin/claims): the claims
           branch, whose numbers a new queue row must not take from another agent.
       tools/coord/check.py --next [--claims-ref REF]   the next free queue number and note number
"""
import argparse, collections, json, os, re, subprocess, sys


def queue_table(text):
    m = re.search(r'^## The queue\n(.*?)^## ', text, re.S | re.M)
    return m.group(1) if m else ''


def queue_numbers(text):
    """Every row's number in COORDINATION.md's queue table."""
    return [int(n) for n in re.findall(r'^\| (\d+) \|', queue_table(text), re.M)]


def queue_rows(text):
    """Each queue row: number -> (agent id, note numbers), from its cells (| # | item | agent | branch | note | status |)."""
    rows = {}
    for line in queue_table(text).split('\n'):
        m = re.match(r'^\| (\d+) \|', line)
        if not m:
            continue
        cells = [c.strip() for c in line.strip().strip('|').split('|')]
        if len(cells) < 6:
            continue
        agent = re.search(r'[A-Z]{1,2}\d+', cells[2])
        notes = [int(n) for n in re.findall(r'\b(\d{3,4})\b', cells[-2])]
        rows.setdefault(int(m.group(1)), (agent.group(0) if agent else '', notes))
    return rows


def note_numbers(text):
    """Every ARCHITECTURE §8 note still written in the file itself (a line that starts a note: `514. **`)."""
    i = text.find('\n## 8.')
    body = text[i:] if i >= 0 else text
    j = body.find('<!-- notes index')
    k = body.find('<!-- end of the notes index -->')
    if j >= 0 and k > j:
        body = body[:j] + body[k:]
    return [int(n) for n in re.findall(r'^(\d{1,4})\. \*\*', body, re.M)]


def file_notes(names):
    return [int(m.group(1)) for n in names if (m := re.fullmatch(r'(\d{1,4})\.md', os.path.basename(n)))]


def all_note_numbers(root):
    """§8's notes wherever they are: written in ARCHITECTURE.md, or files in docs/notes/ (note 521)."""
    notes_dir = os.path.join(root, 'docs', 'notes')
    names = os.listdir(notes_dir) if os.path.isdir(notes_dir) else []
    return note_numbers(read(os.path.join(root, 'docs', 'ARCHITECTURE.md'))) + file_notes(names)


def held(text):
    """The "next free number" line(s): the number and the holdings after it."""
    return re.findall(r'^The next free number is \*\*(\d+)\*\*(.*)$', text, re.M)


def duplicates(xs):
    return sorted(n for n, c in collections.Counter(xs).items() if c > 1)


def read(path):
    with open(path, encoding='utf-8') as f:
        return f.read()


def git(*args, root='.'):
    try:
        return subprocess.run(['git', '-C', root, *args], capture_output=True, text=True, check=True).stdout
    except (OSError, subprocess.CalledProcessError):
        return None


class Base:
    """The base's docs, from a directory or a commit."""

    def __init__(self, root, directory=None, ref=None):
        self.root, self.dir, self.ref = root, directory, ref

    def text(self, rel):
        if self.dir:
            p = os.path.join(self.dir, rel)
            return read(p) if os.path.exists(p) else ''
        return git('show', f'{self.ref}:{rel}', root=self.root) or ''

    def notes(self):
        if self.dir:
            d = os.path.join(self.dir, 'docs', 'notes')
            names = os.listdir(d) if os.path.isdir(d) else []
        else:
            names = (git('ls-tree', '--name-only', self.ref, 'docs/notes/', root=self.root) or '').split()
        return note_numbers(self.text('docs/ARCHITECTURE.md')) + file_notes(names)


def claims(root, ref):
    """The claims branch's numbers and who made each: {'queue': {n: agent}, 'note': {n: agent}}; empty when it isn't there."""
    out = {'queue': {}, 'note': {}}
    if not ref:
        return out
    listing = git('ls-tree', '--name-only', '-r', ref, root=root)
    for path in (listing or '').split():
        m = re.fullmatch(r'(queue|note)/(\d+)\.json', path)
        if m:
            try:
                agent = json.loads(git('show', f'{ref}:{path}', root=root) or '{}').get('agent', '')
            except ValueError:
                agent = ''
            out[m.group(1)][int(m.group(2))] = agent
    return out


def same_agent(a, b):
    """W1 and W1.2 are one owner (an agent's agents count as its own, COORDINATION.md)."""
    return a.split('.')[0] == b.split('.')[0]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--base')
    ap.add_argument('--base-ref')
    ap.add_argument('--claims-ref')
    ap.add_argument('--next', action='store_true')
    ap.add_argument('--root', default='.')
    a = ap.parse_args()
    coord = read(os.path.join(a.root, 'docs/COORDINATION.md'))
    queue, notes, free = queue_numbers(coord), all_note_numbers(a.root), held(coord)
    taken = claims(a.root, a.claims_ref)
    if a.next:
        held_notes = [int(n) for n in re.findall(r'holds? (\d+)', ' '.join(f[1] for f in free))]
        top_note = max([int(f[0]) - 1 for f in free] + notes + held_notes + list(taken['note']) + [0])
        print(json.dumps({'queue': max(queue + list(taken['queue']) + [0]) + 1, 'note': top_note + 1}))
        return 0
    problems = []
    if len(free) > 1:
        problems.append(f'COORDINATION.md has {len(free)} "The next free number is" lines (two claims merged together: keep one, '
                        'with every holding in it)')
    old_queue = old_notes = set()
    old_rows = {}
    if a.base or a.base_ref:
        base = Base(a.root, a.base, a.base_ref)
        base_coord = base.text('docs/COORDINATION.md')
        old_queue = set(duplicates(queue_numbers(base_coord)))
        old_notes = set(duplicates(base.notes()))
        old_rows = queue_rows(base_coord)
    for n in duplicates(queue):
        if n not in old_queue:
            problems.append(f'queue #{n} is two items: renumber the newer one to the next free queue number')
    for n in duplicates(notes):
        if n not in old_notes:
            problems.append(f'ARCHITECTURE §8 note {n} is two notes: renumber the newer one to the next free note number')
    # A new row's numbers must be its own agent's, where the claims branch has them (tools/coord/claim.py).
    for n, (agent, row_notes) in queue_rows(coord).items():
        if n in old_rows or not agent:
            continue
        if (owner := taken['queue'].get(n)) and not same_agent(owner, agent):
            problems.append(f'queue #{n} was claimed by {owner} on the claims branch, not {agent}: take your own (tools/coord/claim.py)')
        for k in row_notes:
            if (owner := taken['note'].get(k)) and not same_agent(owner, agent):
                problems.append(f'note {k} (queue #{n}) was claimed by {owner} on the claims branch, not {agent}: take your own')
    # A queue row outside the table (a merge that put rows after the table's end) reads as text, not as an item.
    m = re.search(r'^## The queue\n(.*?)^## ', coord, re.S | re.M)
    if m:
        in_table = False
        for line in m.group(1).split('\n'):
            if line.startswith('| # |'):
                in_table = True
            elif in_table and line.strip() and not line.startswith('|'):
                in_table = False
            elif not in_table and re.match(r'^\| \d+ \|', line):
                problems.append(f'a queue row outside the queue table: "{line[:60]}…"')
    # §8 split into docs/notes/: its index is current and nothing's written in §8 itself (tools/coord/notes.py).
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import notes as notes_tool
    problems += notes_tool.problems(a.root)
    for p in problems:
        print(f'coord: {p}', file=sys.stderr)
    print(json.dumps({'queue': len(queue), 'notes': len(notes), 'problems': problems}))
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
