# Changelog

Full version history for RimSynapse - Psychology. The mod page and Workshop description show only the latest release; every earlier version is recorded here.

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
