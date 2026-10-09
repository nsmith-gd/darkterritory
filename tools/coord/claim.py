#!/usr/bin/env python3
"""Take a queue number and ARCHITECTURE §8 note numbers so nobody else can (note 521). A claim is a file on the repository's
`claims` branch, `queue/<n>.json` or `note/<n>.json`, made with GitHub's contents API, which refuses to make a file that's
already there. So of two agents reaching for one number at once, exactly one gets it and the other is told at once, before
any work or pull request; the loser takes the next. COORDINATION.md's queue row is still written as before, with the
numbers this gave.

Usage: tools/coord/claim.py --agent W1 --title "The director's inbox" [--notes 1] [--no-queue] [--branch b] [--repo o/r]
       tools/coord/claim.py --list          what's claimed on the branch
       tools/coord/claim.py --offline-test  the claiming against an in-memory GitHub
A token comes from GITHUB_TOKEN, GH_TOKEN or `gh auth token`. Agents without one take numbers the same way with their
GitHub tools (docs/COORDINATION.md, "Taking numbers").
"""
import argparse, base64, datetime, json, os, re, subprocess, sys, urllib.error, urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import check  # noqa: E402  (the docs' own numbers)

BRANCH = 'claims'
README = ('# Claims\n\nOne file per queue number (`queue/<n>.json`) and per ARCHITECTURE §8 note number (`note/<n>.json`), '
          'made by tools/coord/claim.py or by hand with the contents API, which refuses to make a file that exists: a number '
          'is whoever made its file first (docs/COORDINATION.md, note 521).\n')


class GitHub:
    """Just enough of the REST API: contents, and the Git Data calls that make the branch the first time."""

    def __init__(self, repo, token, send=None):
        self.repo, self.token = repo, token
        self.send = send or self._send

    def _send(self, method, path, body=None):
        req = urllib.request.Request(f'https://api.github.com/repos/{self.repo}{path}', method=method,
                                     data=None if body is None else json.dumps(body).encode(),
                                     headers={'Authorization': f'Bearer {self.token}', 'Accept': 'application/vnd.github+json',
                                              'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'dt-coord-claim'})
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                return r.status, json.loads(r.read() or b'{}')
        except urllib.error.HTTPError as e:
            try:
                return e.code, json.loads(e.read() or b'{}')
            except ValueError:
                return e.code, {}

    def listing(self, folder):
        """The numbers claimed under queue/ or note/; None when the branch isn't there yet."""
        status, body = self.send('GET', f'/contents/{folder}?ref={BRANCH}')
        if status == 404:
            status, _ = self.send('GET', f'/git/ref/heads/{BRANCH}')
            return None if status == 404 else []
        return sorted(int(m.group(1)) for e in body if (m := re.fullmatch(r'(\d+)\.json', e.get('name', ''))))

    def make_branch(self):
        """An orphan branch holding the README; someone else making it first is as good."""
        _, blob = self.send('POST', '/git/blobs', {'content': README, 'encoding': 'utf-8'})
        _, tree = self.send('POST', '/git/trees', {'tree': [{'path': 'README.md', 'mode': '100644', 'type': 'blob', 'sha': blob['sha']}]})
        _, commit = self.send('POST', '/git/commits', {'message': 'Claims: one file per number (note 521)', 'tree': tree['sha'], 'parents': []})
        status, _ = self.send('POST', '/git/refs', {'ref': f'refs/heads/{BRANCH}', 'sha': commit['sha']})
        if status not in (201, 422):
            raise SystemExit(f'claim: could not make the {BRANCH} branch ({status})')

    def create(self, path, record):
        """True if this call made the file; False if it was already there."""
        body = {'message': f'Claim {path} ({record["agent"]}: {record["title"]})', 'branch': BRANCH,
                'content': base64.b64encode((json.dumps(record, indent=1) + '\n').encode()).decode()}
        status, reply = self.send('PUT', f'/contents/{path}', body)
        if status == 201:
            return True
        if status == 422 or status == 409:
            return False
        raise SystemExit(f'claim: GitHub said {status} to {path}: {reply.get("message", "")}')


def docs_highest(root):
    """The highest queue and note numbers the docs already use."""
    coord = check.read(os.path.join(root, 'docs', 'COORDINATION.md'))
    queue = check.queue_numbers(coord)
    notes = check.all_note_numbers(root)
    free = check.held(coord)
    held_notes = [int(f[0]) - 1 for f in free] + [int(n) for f in free for n in re.findall(r'holds? (\d+)', f[1])]
    return max(queue + [0]), max(notes + held_notes + [0])


def take(gh, folder, after, record, tries=50):
    """The first free number above `after` (and above anything on the branch) that this call gets to make."""
    claimed = gh.listing(folder)
    if claimed is None:
        gh.make_branch()
        claimed = []
    n = max([after] + claimed) + 1
    for _ in range(tries):
        if gh.create(f'{folder}/{n}.json', record):
            return n
        n += 1  # someone made it a moment ago: the next one
    raise SystemExit(f'claim: {tries} numbers in a row were taken under {folder}/')


def claim(gh, root, agent, title, notes=1, queue=True, branch=''):
    top_queue, top_note = docs_highest(root)
    at = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
    record = {'agent': agent, 'title': title, 'branch': branch, 'at': at}
    out = {}
    if queue:
        out['queue'] = take(gh, 'queue', top_queue, record)
        record = {**record, 'queue': out['queue']}
    out['notes'] = [take(gh, 'note', top_note, record) for _ in range(notes)]
    return out


def offline_test():
    """Two agents claiming at once from the same docs get different numbers; the branch is made once."""
    files, refs = {}, {}

    def send(method, path, body=None):
        if method == 'GET' and path.startswith('/contents/'):
            folder = path.split('?')[0][len('/contents/'):]
            if BRANCH not in refs:
                return 404, {}
            names = [p.split('/')[-1] for p in files if p.startswith(folder + '/')]
            return (200, [{'name': n} for n in names]) if names else (404, {})
        if method == 'GET' and path == f'/git/ref/heads/{BRANCH}':
            return (200, {}) if BRANCH in refs else (404, {})
        if method == 'POST' and path in ('/git/blobs', '/git/trees', '/git/commits'):
            return 201, {'sha': f'sha{len(files) + len(refs)}'}
        if method == 'POST' and path == '/git/refs':
            if BRANCH in refs:
                return 422, {}
            refs[BRANCH] = body['sha']
            return 201, {}
        if method == 'PUT':
            p = path[len('/contents/'):]
            if BRANCH not in refs:
                return 404, {'message': 'Branch not found'}
            if p in files:
                return 422, {'message': '"sha" wasn\'t supplied.'}
            files[p] = json.loads(base64.b64decode(body['content']))
            return 201, {}
        return 400, {}

    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')
    gh = GitHub('me/game', 'token', send)
    top_queue, top_note = docs_highest(root)
    a = claim(gh, root, 'W1', 'one thing', notes=2)
    assert a == {'queue': top_queue + 1, 'notes': [top_note + 1, top_note + 2]}, a
    # The second agent read the docs before the first's claim landed in them: the branch still sends it past.
    b = claim(gh, root, 'D1', 'another')
    assert b == {'queue': top_queue + 2, 'notes': [top_note + 3]}, b
    # A race the listing can't see (both listed, then both created): the create refuses the second, which takes the next.
    files[f'queue/{top_queue + 3}.json'] = {'agent': 'E1'}
    real_listing = gh.listing
    gh.listing = lambda folder: [n for n in real_listing(folder) if folder != 'queue' or n < top_queue + 3]
    c = claim(gh, root, 'F1', 'raced', notes=0)
    assert c == {'queue': top_queue + 4, 'notes': []}, c
    assert files[f'queue/{top_queue + 1}.json']['agent'] == 'W1' and files[f'note/{top_note + 3}.json']['queue'] == top_queue + 2
    assert len(refs) == 1
    print(f'claim: offline test passed (from the docs\' queue #{top_queue} and note {top_note})')
    return 0


def token():
    for k in ('GITHUB_TOKEN', 'GH_TOKEN'):
        if os.environ.get(k):
            return os.environ[k]
    try:
        return subprocess.run(['gh', 'auth', 'token'], capture_output=True, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def origin_repo(root):
    try:
        url = subprocess.run(['git', '-C', root, 'remote', 'get-url', 'origin'], capture_output=True, text=True, check=True).stdout
    except (OSError, subprocess.CalledProcessError):
        return None
    m = re.search(r'github\.com[:/]([^/]+/[^/.\s]+)', url) or re.search(r'/git/([^/]+/[^/.\s]+)', url)
    return m.group(1) if m else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--agent')
    ap.add_argument('--title', default='')
    ap.add_argument('--notes', type=int, default=1)
    ap.add_argument('--no-queue', action='store_true')
    ap.add_argument('--branch', default='')
    ap.add_argument('--repo')
    ap.add_argument('--root', default=os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
    ap.add_argument('--list', action='store_true')
    ap.add_argument('--offline-test', action='store_true')
    a = ap.parse_args()
    if a.offline_test:
        return offline_test()
    repo, tok = a.repo or origin_repo(a.root), token()
    if not repo or not tok:
        print('claim: needs the repository (--repo owner/name) and a token (GITHUB_TOKEN, GH_TOKEN or gh). Without a token, take '
              'numbers with your GitHub tools as docs/COORDINATION.md "Taking numbers" says.', file=sys.stderr)
        return 2
    gh = GitHub(repo, tok)
    if a.list:
        print(json.dumps({'queue': gh.listing('queue') or [], 'note': gh.listing('note') or []}))
        return 0
    if not a.agent:
        ap.error('--agent is required')
    out = claim(gh, a.root, a.agent, a.title, a.notes, not a.no_queue, a.branch)
    print(json.dumps(out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
