# Changelog

Full version history for RimSynapse - Psychology. The mod page and Workshop description show only the latest release; every earlier version is recorded here.

## v0.9.0 - Prisoner Minds and Linked Memories
- NEW - Psychological profiles for prisoners, slaves and guests: captives and visitors staying under your roof now think, evaluate and speak as themselves instead of sharing one blank register - the foundation warden conversations build on.
- Changed: event and death memories now key their subject on Core 0.9's canonical pawn id, so a colonist's memories about someone truly link up - chit-chat about a pawn consolidates with their later death, as it always should have.
- Requires RimSynapse Core v0.9.0.

## v0.7.1 - Gradual personality and smarter memory
- Fixed the headline bug: a colonist could gain Bloodlust and lose a steadfast trait just from repeatedly attacking an object (like a wrecked vehicle). Violence against inert objects is now clearly distinguished from harming the living and can no longer drive bloodthirsty personality changes.
- NEW - Gradual, multi-day personality shifts: traits no longer flip in a single day. Evidence builds as "pressure" over several days, is resisted by steadfast traits (which also protect themselves from removal), and only fires once sustained - and an evaluation that calls a shift "unlikely" can no longer flip a trait at all.
- NEW - Trait-change guardrails: only sanctioned traits can be added or removed (never, for example, Psychopath), at most one change per evaluation, with a cooldown between changes. Social trust/familiarity offsets are clamped to their per-eval range before applying.
- NEW - Mod settings sliders: trait-shift threshold and pressure decay, memory consolidation and reference thresholds, abandonment risk, suicide damage, opinion/trust blend, and an evaluation cadence (every N days) to cut LLM token cost.
- Fixed: AI-driven mental breaks now actually fire (the break-prediction request was previously commented out and left inert).
- Fixed: therapy-session summaries are now kept as durable long-term memories on both colonists instead of being computed and discarded.
- The daily evaluation now separates today's events from a lifetime of history and states lifetime violence against the living explicitly; the memory panel splits short vs long-term by real importance rather than a memory-type list.
- Requires Core v0.7.1; saves and settings carry over unchanged.

## v0.7.0 - Regions and Territories Compatibility
- Moves in step with RimSynapse Core v0.7.0.
- Requires Core v0.7.0; saves and settings carry over unchanged.

## v0.6.1
- Fixed - mod list metadata: the in-game mod list still showed v0.5.2 with no v0.6.0 notes. Version and changelog now agree in every place they are stated.
- Roadmap updated: 0.7 is now Regions and Territories compatibility - the groundwork the Factions work depends on. Everything after it shifts up one release.

## v0.6.0
- NEW - Ceremonies recover instead of vanishing: when the language model returns a ceremony record the mod cannot read, the situation is now handed to Core's agent to salvage rather than being dropped with a warning. (Requires Core's escalation setting to be enabled.)
- Requires RimSynapse Core v0.6.0. Your saves and settings carry over unchanged.
- Documentation: in-game wiki guides updated; "MCP endpoints" renamed to game tools, matching Core's native tool-calling engine.

## v0.5.2
- Fixed - ceremony records crashed: funerals and other ceremonies threw an error when the model answered without a written narrative. Incomplete responses are now skipped cleanly instead.
- Fixed - clearer error reporting: background AI callbacks now name the feature that failed rather than logging a bare error.
- Licence: now PolyForm Noncommercial 1.0.0. Free to use, modify and share for any noncommercial purpose.

## v0.5.1
- NEW - Memory Tab visibility filters: hides cluttering short-term social and conversation memories by default, adding a "Show Short Term Memories" checkbox toggle to show them.
- NEW - Dynamic Warden recruitment patches: re-engineered the prisoner recruitment success patch to link dynamically to `RecruitChance` via reflection, preventing compatibility crashes.
- Performance tuning: substantially optimized background memory processing logic.

## v0.4.0
- Updated to support RimSynapse Core v0.4.0 (Multi-provider routing and Image generation).
