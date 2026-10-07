# Agent logs

Every agent keeps its own log here, `docs/log/<agent id>.md`, so no two agents ever write the same file. Append at the
bottom, newest last, and push it with your work (a docs-only commit is fine). Each entry is one line:

    2026-10-06 23:00 UTC · A1 · what you did or decided, with the PR, branch or commit

- **Timestamps are UTC** (`date -u "+%Y-%m-%d %H:%M"`), at the moment of the entry.
- **Log:** starting or claiming an item; each PR opened, merged or closed; a CI failure and its fix; decisions a reviewer
  should know about, such as a spec reading or a tuning change; handing work to another agent; being blocked, and on what.
- **Agent ids:** the session's letter from docs/COORDINATION.md and a number (A1, B1), and a dotted suffix for agents
  it launches (A1.1, A1.2). An id is never reused for someone else.
