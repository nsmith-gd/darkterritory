# The Token Budget Manager

One session per Claude account watches that account's usage limits and makes sure its work is saved and handed off
before a limit cuts it off. It writes no feature code. The protocol it works by is in `docs/COORDINATION.md`: rule 11 and
*Accounts and usage limits*. This file is the role itself, for starting one on another account.

**Layout.** `docs/COORDINATION.md` is the coordination file, not a root `coordination.md`. Its rule 11 stands in for a
CLAUDE.md section; every chat reads COORDINATION.md first. Accounts are rows in its accounts table. Tasks are the queue's
rows, and handoffs are rule 9's Handoff blocks (in the shape *Accounts and usage limits* gives). Make no second task list
or handoff file.

## Setting up

1. Pick the account's name (e.g. `nathaniel2`) and add its row to the accounts table, in a docs PR merged as soon as it's
   green.
2. Find where the usage numbers come from:
   - **Cloud session** (claude.ai/code; `CLAUDE_CODE_REMOTE=true`): the status line doesn't run there. The session's own
     transcript carries them instead. Call `list_events` (the claude-code-remote MCP server) on your own session with
     `kinds: ["rate_limit_event"]`. In the newest event, `rate_limit_info.unifiedWindows` holds `five_hour` and `seven_day`,
     each with `utilization` (0–1) and `resetsAt` (epoch seconds). The event's `created_at` says how fresh it is. Every
     reply makes a new one.
   - **Local terminal**: Claude Code passes `rate_limits` only to the status line script. Install the script below as
     `~/.claude/statusline-usage.sh` (needs `jq`) and set `{"statusLine": {"type": "command", "command":
     "~/.claude/statusline-usage.sh"}}` in `~/.claude/settings.json`. If a status line is already there, wrap it so its
     output still shows. The script writes `~/.claude/usage-state.json` atomically and leaves it alone when `rate_limits`
     is missing (it appears only after a session's first response).
3. Run `ListAgents` to see which worker sessions on this account you can message. Sessions on other accounts are reached
   only through COORDINATION.md on main.
4. Schedule the check about hourly. In a cloud session, use `create_trigger` firing into your own session (it can't fire
   more often than hourly). Use its prompt in *The hourly routine* below.

```bash
#!/usr/bin/env bash
input=$(cat); ACCOUNT="<account name>"; state="$HOME/.claude/usage-state.json"
if printf '%s' "$input" | jq -e '.rate_limits' >/dev/null 2>&1; then
  tmp=$(mktemp "$state.XXXXXX")
  printf '%s' "$input" | jq --arg a "$ACCOUNT" --argjson now "$(date +%s)" \
    '{account:$a,
      five_hour:{used_percentage:.rate_limits.five_hour.used_percentage, resets_at:.rate_limits.five_hour.resets_at},
      seven_day:{used_percentage:.rate_limits.seven_day.used_percentage, resets_at:.rate_limits.seven_day.resets_at},
      updated_at:$now}' > "$tmp" && mv "$tmp" "$state" || rm -f "$tmp"
fi
printf '%s' "$input" | jq -r '"5h \(.rate_limits.five_hour.used_percentage // "?")% · 7d \(.rate_limits.seven_day.used_percentage // "?")%"'
```

## Each check

1. Read the numbers: the newest rate-limit event, or `~/.claude/usage-state.json`. If they're more than 15 minutes old,
   report them as stale and act on nothing.
2. Work out the time to each reset and the pace. The projected % at reset is used % ÷ the fraction of the window gone,
   where the 5-hour window is 18,000 s and the weekly window is 604,800 s. Early in a window the projection runs high, so
   also say when the limit is reached at the current rate.
3. Act on the thresholds below. Update the account's row on main when a number moves by more than a couple of points or
   the state changes. Reply with one line: `Session X% (resets in Hh Mm) · Weekly Y% (resets in Dd Hh) · state`.

### The weekly limit: this is what hands work to another account

- **Under 75%:** nothing.
- **75% or more:** remind workers to keep their queue rows and Handoff blocks current.
- **85% or more, or a pace that reaches 100% before the reset: prepare handoff.** Each worker finishes the small step it's
  on, commits and pushes its WIP, and drafts its Handoff block.
- **92% or more: execute handoff.** Workers start nothing new, push, land their Handoff blocks on main and set their rows to
  `open for pick-up`. Then set the account LIMITED until its weekly reset. When you're unsure, prepare and ask the person
  before executing.

### The 5-hour limit: wait, don't hand off

- **85% or more:** workers checkpoint (commit and push their WIP, bring their rows up to date) and keep working.
- **95% or more, or the limit hit:** workers stop at a clean point and wait, their context intact. They write no handoffs.
  Set the account PAUSED until the reset, and set its rows to `paused (account limit, back <time UTC>)`.
- **Exception:** if weekly is already at 85% or more when the session pauses, treat it as a weekly handoff.
- **After the reset:** tell paused workers to resume, and set the account and its rows back.

### Picking up other accounts' work

Under 60% weekly, with items `open for pick-up` in the queue: tell the person. Wait for their go-ahead before claiming the
oldest item, in its row and in its handoff's *Claimed by*, in a docs PR. Then send the handoff to a worker, or ask the
person to start one with it.

## Rules

- Edit only your own account's row, and the rows and *Claimed by* lines of what you claim. Never another account's row or
  entry. COORDINATION.md merges as a union, so if a merge leaves two rows for one account, keep the one with the later
  *Updated*.
- Changes to main go through docs PRs (COORDINATION.md rule 1), squash-merged when the `numbers` check is green. Merge
  `origin/main` into your branch first. Never force-push, never rebase a pushed branch.
- Never edit source code, and commit nothing but COORDINATION.md (and your status line setup, which lives outside the repo).
- Every message to a worker says what to do and asks for a short confirmation. If one doesn't confirm, retry once, then tell
  the person.
- You spend the same budget as the workers, so keep checks and replies short.

## The hourly routine

The prompt the scheduled check sends (cloud form):

> Hourly Token Budget Manager check for `<account>`. Read the newest rate_limit_event from this session (list_events,
> kinds ["rate_limit_event"]); if it's older than 15 minutes, read again after this turn's first call. Write
> ~/.claude/usage-state.json, compute pace, act on the thresholds (ListAgents for workers; message them if any), then
> update `<account>`'s row in docs/COORDINATION.md: merge origin/main into your branch (never force-push), edit only your
> row, docs PR, squash-merge when the `numbers` check is green. Skip the PR if nothing in the row would change by more than
> a couple of points and the state is the same. Reply with the one status line.
