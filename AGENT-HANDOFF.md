# AGENT-HANDOFF — `feature/prompt-lab-composers`

## What this branch adds

The **Psychology contribution** to the universal Prompt Lab (central console/registry in
rimworld-claude-dev-tools): pure, Verse-free composers for three Psychology LLM call sites, so the lab builds
the exact prompts the game sends without launching RimWorld. Branched off `main`.

- **NEW `Source/Prompts/`** (all Verse-free):
  - `PsychologyPrompt.cs` — the shared `{system, user}` struct.
  - `VoiceProfilePromptComposer.cs` — `Compose(name, traits, psychology, personality)`. **This derives the
    `voiceProfile` the Conversations dialogue prompt consumes (#44), so the register loop is now tunable
    end-to-end game-free.**
  - `BreakPromptComposer.cs` — `Compose(name, moodFraction, traits, burdens, candidates?)`; holds
    `DefaultCandidates` (the mental-break defName list, moved here and single-sourced — `SynapsePsychologyBreaks.BreakCandidates` now references it).
  - `ChildhoodMemoryPromptComposer.cs` — `Compose(roleLabel, name, gender, factionType, childhoodTitle,
    childhoodDesc, skillBonuses, disabledWork, extraContext)`.
- **CHANGED** call sites now delegate (behaviour-preserving):
  - `Source/Comps/SynapsePawnComp_BackstoryPrompts.cs` — `DeriveVoiceProfile` and `GenerateChildhoodMemory`.
  - `Source/API/SynapsePsychologyBreaks.cs` — `RequestBreakWarning` + `BreakCandidates` single-sourced.

The dev-tools PromptLab console `<Compile>`-links these pure files as families `psychology.voice`,
`psychology.break`, `psychology.childhood`.

## Not done (same pattern, follow-ups)
The other Psychology prompt sites — adulthood-memory + life-analysis (`SynapsePawnComp_BackstoryPrompts`),
clinical evaluation (`SynapsePsychologyEvaluation`), therapy summary, internal monologue + per-colonist event
memory (`SynapsePsychologyOpportunistic`), adulthood-selector, visitor childhood/adulthood
(`PsychologyPromptBuilder`) — follow the same extraction shape and can be added incrementally.

## Verify
- `dotnet build Source/RimSynapsePsychology.csproj -c Release` → succeeds (needs Core built; an isolated
  worktree needs Core junctioned at `..\..\Core`).
- Exercised end-to-end by the dev-tools lab against the live local model: voice (gruff-veteran → gruff/terse;
  clinical-scientist → flat/pedantic), break (volatile-bloodlust → Berserk + warning), childhood (tribal-miner
  → 155-word memory + tribal hometown).
