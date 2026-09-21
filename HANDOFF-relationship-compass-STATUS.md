# STATUS CORRECTION — `feature/relationship-compass` is LANDED

**Written:** 2026-09-19 · **From:** Conversations session (Opus 5) · **Repo:** RimSynapse.Psychology
**Supersedes:** `HANDOFF-relationship-compass.md` (written 2026-09-15) — that document is
**stale**. Its "Status snapshot" and "Next actions" sections describe a world that no longer
exists. Read this file instead; do not act on sections 1 or 7 of the old one.

---

## TL;DR

The #72 Relationship Compass feature **was pushed, merged, and the branch pruned on
2026-09-16** — the day after that handoff was written. Issues #72, #23 and #24 are closed.
There is no unpushed commit, no unmerged branch, and no rebase left to do.

The **only** item from the old handoff still genuinely open is the **LLM-loop playtest**
(old §3 "⚠️ Wants a real in-game playthrough"), which was always scoped as a post-merge item.

---

## What the old handoff claims vs. what the repo says

| Old handoff claim (2026-09-15) | Verified reality (2026-09-19) |
|---|---|
| "`181be68` is local-only — **first action: `git push`**" | Pushed. `181be68` is an ancestor of both `release/0.10.0` and `origin/release/0.10.0`. |
| "Merged? **No.** Not in `release/0.10.0`, not in `main`." | **Merged into `release/0.10.0`** via `1e9124e` (2026-09-16), *"Merge feature/relationship-compass into release/0.10.0 (#72, #23, #24)"*. Correctly **not** in `main` — 0.10.0 is unreleased, main stays at 0.9.3 until ship-it. |
| "Branch is 13 commits ahead of `release/0.10.0`" | Branch `feature/relationship-compass` **no longer exists**, locally or on origin — pruned by feature-complete. |
| "Next: close #72 / #23 / #24" | All three **CLOSED**. |
| "Next: rebase siblings `feature/leader-context-hooks`, `feature/prompt-lab-composers`, `feature/therapy-auto-modes`" | **Done.** All three contain `1e9124e`. |

### Commands used to verify (re-run these rather than trusting either document)

```bash
git merge-base --is-ancestor 181be68 origin/release/0.10.0 && echo CONTAINS
git log -1 --format='%h %ad %s' --date=short origin/release/0.10.0
git rev-list --left-right --count release/0.10.0...origin/release/0.10.0   # -> 0  0
gh issue view 72 --json state; gh issue view 23 --json state; gh issue view 24 --json state
```

---

## Current branch topology (Psychology, as of 2026-09-19)

```
main                         9206f95  (0.9.3 — does NOT contain the compass; correct)
release/0.10.0               1e9124e  == origin, in sync, 0 ahead / 0 behind   <-- the compass base
  feature/leader-context-hooks   1e9124e  (rebased, no own commits yet)
  feature/prompt-lab-composers   1e9124e  (rebased, no own commits yet; worktree C:/github/worktrees/Psychology/prompt-lab)
  feature/therapy-auto-modes     d0026bd  (2 commits ahead — #17 therapy work, ACTIVE)
```

`release/0.9.2` and `release/0.9.3` are local leftovers whose upstreams are `gone` — prunable.

---

## The one thing actually left: the LLM-loop playtest

The merge commit itself records this, so it is not a surprise:

> Mechanically verified (build-green + debug actions + in-game test cases): two-axis model,
> trust hooks, compatibility, milestones (Phase 4 runtime PASS), compass UI.
> **LLM-loop QUALITY** (nightly eval verdicts, gated rivalry/betrayal, full conversion/
> recruitment chain) **is a post-merge playtest item — needs Ideology + a live model.**

Nothing in the repo records whether that playtest has since been run. If it hasn't, it wants
Ideology active, a live/local model, and several in-game nights, driving these debug actions
(category `RimSynapse`, via `run_debug_action`; `execute_game_tool` arg key is `arguments`,
not `args`):

- `Relationships: run nightly review (Tool)` — do verdicts read sensibly against real histories?
- `Relationships: conversion drift dump` — susceptibility gate → certainty drift
- `Relationships: recruitment softening dump` — closes befriend → convert → recruit
- `Relationships: seed compass demo + open (Tool)` — seeds a relationship per quadrant, opens the window

This is a *quality* judgement, not a pass/fail assertion — it cannot be closed headlessly.

---

## Still-valid environment notes from the old handoff (keep these)

These two are environment facts, not feature facts, and survive the old document:

1. **Modlist contention.** Something rewrites `C:\RimWorldDevData\Config\ModsConfig.xml`
   during launch to a `skylights`-based list that **excludes `rimsynapse.core` /
   `rimsynapse.psychology`**, so the in-game suite reports
   `Toolkit_TestAssembliesDiscovered FAIL — no TestAssemblies folder`. Re-configure the
   modlist immediately before EACH run and read `SUMMARY passed=N failed=0` from `Player.log`
   as authoritative. Also: the deploy gate blocks the runner if ANY deployed `Mods/` copy is
   stale, including sibling mods not in the test modlist.
2. **Screenshots don't work here** (nut-js temp-dir / virtual-desktop isolation). Validate UI
   via `read_window` / `get_open_windows` plus a zero-exception `read_rimworld_log`.

---

## ⚠️ Note for whoever reads this: this repo has concurrent sessions

`feature/therapy-auto-modes` gained commit `d0026bd` **during** the few minutes it took to
verify the facts above. Another session is actively working in this checkout. Before acting on
any branch state in either handoff, re-run `git status` / `git log -1` yourself — and note the
dev-tools deploy/test loop can itself check out a different branch in a dependency repo
mid-build, so re-check `HEAD` after a build too.

---

## Suggested cleanup

`HANDOFF-relationship-compass.md` is tracked in git (committed in `48190d8`). Once this
correction has been read, that file should be deleted — it is a live tripwire for any agent
that opens it cold — and this file with it, once the playtest item is either done or moved to
a GitHub issue where it belongs.
