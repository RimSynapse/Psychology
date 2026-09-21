# HANDOFF — `feature/relationship-compass` (#72 Relationship Compass · #24 · #23)

**Written:** 2026-09-15 · **From:** prior session (Opus 4.8) · **Repo:** RimSynapse.Psychology (depends on Core)

---

## 1. Status snapshot

| Fact | Value |
|---|---|
| Branch | `feature/relationship-compass` |
| Local HEAD | `181be68` — *relationship compass on the Social Network tab with labeled dots* |
| **Unpushed** | **`181be68` is local-only.** `origin/feature/relationship-compass` is at `d9d66a1`. The other 12 commits are already on the remote. |
| Ahead of `release/0.10.0` | **13 commits** (the whole #72 feature + #23 familiarity milestones) |
| Merged? | **No.** Not in `release/0.10.0`, not in `main`. `git branch --contains 181be68` lists only this branch. |
| Working tree | Clean |

**First action for whoever picks this up:** `git push` (to publish `181be68`) — nothing is lost, but the newest commit exists only on this machine.

---

## 2. What the feature is

The full **#72 Relationship Compass**: a two-axis model of colonist relationships —
**warmth** (liking) × **trust** (reliance), each −100..100 — plus familiarity (depth),
compulsion control (an emotional brake), personality compatibility, a nightly LLM
relationship eval, and the **befriend → convert → recruit** ideology loop. Also lands
**#23** (familiarity milestone events) and serves **#24** (social-network visualization).

**Locked architecture principle (do not violate):** the **LLM acts as a gate /
susceptibility determination** (slow, nightly, disposition-level); **C# drives the
hour-to-hour mechanical changes** within those gates; **vanilla executes the final
action** (convert/recruit). LLM = "is this pawn susceptible?"; C# = the continuous drift;
vanilla = the actual certainty/resistance change.

The 13 commits (newest first):
```
181be68 compass on Social Network tab + labeled dots (#72/#24)   <-- UNPUSHED
d9d66a1 compass visualization (originally Profile tab) (#72/#24)
ad1db5f two-axis milestones — friendship/rivalry/reconciliation (#72 P4)
b34e3cb recruitment softening — closes befriend->convert->recruit (#72)
6474492 faith conversion — LLM susceptibility gate + C# certainty driver (#72)
fb48c6e ideology as a major compatibility axis + conversion hinge (#72)
cbfeaf9 nightly relationship eval — prompt + per-pawn chaining (#72 P4b)
9685ff3 relationship-review scaffold — gated rivalry/betrayal (#72 P4a)
08b1d51 personality compatibility + desensitise/hypersensitise (#72 P3)
d251991 emotional hooks — interactions on right axes, combat trust (#72 P2b)
36da529 compulsion control (emotional brake) + directed awards (#72)
c4b5285 compass foundation — warmth axis + tending trust (#72)
d22a564 Familiarity Milestone Events (#23)
```

---

## 3. Validation status — READ BEFORE LANDING

### ✅ Headlessly validated (automated tests + debug actions) — no human playtest needed
- **Compass geometry** — unit test `Psychology_CompassView_PlotAndQuadrant` (PlotPoint mapping, clamping, all 5 quadrant classifications) in `Source.Tests/RelationshipCompassCases.cs`.
- **Compass UI + labeled dots (`181be68`)** — **confirmed live in this session**: seeded via the `Relationships: seed compass demo + open (Tool)` debug action; window drew with **zero exceptions across frames**, and `read_window` returned the compass header, the four quadrant corner labels (Friends/Allies/Fond-wary/Enemies), the `warmth →` axis, and per-colonist **name labels** (Candice→Friends, Madam→Allies) with the detail list scrolling below. This is the only *new* code since the last validated state.
- **Compulsion control, personality compatibility, directed/shared-victory/tend-per-session trust awards, two-axis milestones, familiarity milestones (#23)** — each shipped with a `[DebugAction]` dump AND an in-game test case in `RelationshipCompassCases.cs`; validated at build time.

### ⚠️ Wants a real in-game playthrough (cannot be asserted headlessly)
The **LLM-gated behaviors**. Their C# paths are exercised by debug dumps, but the *quality
of the LLM determination* only emerges with a live model over several in-game nights:
- **Nightly relationship eval** — do verdicts read sensibly against real pawn histories?
- **Gated rivalry / betrayal** — fires only when it should?
- **The full befriend → convert → recruit loop** end-to-end (faith conversion
  susceptibility → certainty drift → recruitment softening), spanning multiple nights and
  both axes + ideology.

**Recommendation:** land the mechanically-verified work, treat the LLM loop as a
post-merge playtest item on `release/0.10.0` (needs Ideology active + a live/local model).

---

## 4. ⚠️ Environment gotcha — modlist contention (will bite you)

Something in this environment **keeps rewriting `C:\RimWorldDevData\Config\ModsConfig.xml`**
during launch to a `skylights`-based list that **excludes `rimsynapse.core` /
`rimsynapse.psychology`**. Symptom: the Psychology in-game test suite reports
`Toolkit_TestAssembliesDiscovered FAIL — no TestAssemblies folder` because the RimSynapse
mods aren't loaded. In this session `configure_active_mods` did not stick against it.

**The deploy gate also blocks the test runner** if ANY deployed `Mods/` copy is stale —
even sibling mods not in the test modlist (Conversations, Factions, LivingWorld,
Regions-and-Territories, WorldNews). This session all mods were freshly deployed to clear
it (Core/Psychology + those five), so deployed binaries should currently be in sync.

**Correct packageIds** (verified against installed folders):
`rimsynapse.core` → `Mods/Core`, `rimsynapse.psychology` → `Mods/Psychology`.

**Workaround that has worked before:** re-configure the modlist to
`[harmony, rimworld + Royalty/Ideology/Biotech/Anomaly/Odyssey, rimsynapse.core,
rimsynapse.psychology, archdukejim.rimagentic]` immediately before EACH run, and read
`SUMMARY passed=N failed=0` straight from `Player.log` as authoritative if the harness
says "could not parse". Ideology must be active for the conversion/recruit loop.

**Screenshots don't work here** (nut-js temp-dir / virtual-desktop isolation). Validate UI
via `read_window` / `get_open_windows` + a zero-exception `read_rimworld_log` instead.

---

## 5. Debug actions for this feature (category "RimSynapse", via `run_debug_action`)
`execute_game_tool` arg key is **`arguments`** (not `args`); pawn-targeted actions take `pawnName`.
- `Relationships: seed compass demo + open (Tool)` — seeds one relationship per quadrant on nearest colonists, opens the window on the Social tab
- `Relationships: compulsion control dump` · `compatibility dump` · `conversion drift dump` · `recruitment softening dump`
- `Relationships: run nightly review (Tool)` · `shared victory bonds fighters` · `tend builds trust per session`
- `Relationships: Validate two-axis milestones (#72) (Log)`

---

## 6. Key files
- `Source/API/SynapseRelationshipCompass.cs` — pure geometry/colour (unit-testable; the dialog paints).
- `Source/UI/Dialog_PawnPsychology_Tabs.cs` — `DrawRelationshipCompass(...)`, `DrawSocialNetworkTab` (compass as fixed header above the scrolling list), the labeled-dot drawing.
- `Source/UI/Dialog_PawnPsychology.cs` — `Dialog_PawnPsychology(Pawn, bool openSocial)` overload.
- `Source/Utils/SynapseDebugActions.cs` — `SeedCompassDemo`.
- `Source/API/` — `SynapseRelationships.cs`, `SynapseCompulsion.cs`, `SynapseCompatibility.cs`, `SynapseRelationshipReview.cs`, `SynapsePsychologyRelationshipReview.cs` (nightly eval), `SynapseConversion.cs`, `SynapseRecruitment.cs`, `SynapseFamiliarityMilestones.cs` / `SynapseRelationshipMilestones.cs`.
- `Source.Tests/RelationshipCompassCases.cs` — all compass/relationship in-game tests.

---

## 7. Next actions (in order)
1. **`git push`** — publish `181be68` (currently local-only).
2. Decide: **land now** or **playtest the LLM loop first**.
3. To land → run the **`feature-complete`** skill: merge into `release/0.10.0`, prune the branch, close #72 / #23 / #24, then **rebase the sibling feature branches** (`feature/leader-context-hooks`, `feature/prompt-lab-composers`, `feature/therapy-auto-modes`) onto the new base.
4. For the LLM-loop playtest: Ideology active + live model, run the nightly-review / conversion / recruitment debug dumps over several in-game days and sanity-check verdicts.
