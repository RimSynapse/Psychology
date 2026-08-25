namespace RimSynapse.Psychology.Prompts
{
    /// <summary>
    /// PURE, Verse-free composer for the Psychology voice-profile prompt — the one that DERIVES the
    /// <c>voiceProfile</c> the Conversations dialogue prompt then consumes (so tuning it is directly tied to
    /// the Conversations #44 register work). Given a colonist's traits + psychology + personality it produces
    /// the exact system+user pair <c>DeriveVoiceProfile</c> sends. Authored once here; the game-free Prompt Lab
    /// links this file, so the lab can't drift from the game.
    ///
    /// Register contract (Conversations #44): the style text is read VERBATIM by a small dialogue model,
    /// which imitates the register OF THE DESCRIPTION more than the voice it describes. A style written in
    /// analyst's language ("dismisses emotional appeals as inefficiency") begets clinical dialogue for an
    /// ordinary farmer. So the style must be written in plain neighbor-words, and the profile must carry a
    /// SAMPLE LINE of actual speech — small models copy an example far more reliably than they interpret a
    /// description.
    /// </summary>
    public static class VoiceProfilePromptComposer
    {
        /// <summary>The register rules for a voice's "style" and "sample" fields — shared VERBATIM by every
        /// prompt that produces a Voice object (the one-shot derive below and the daily clinical review), so
        /// the rules cannot drift between them. See the class summary for why the register matters.</summary>
        public const string StyleAndSampleRules =
@"Rules for ""style"":
- Write it in plain words a neighbor would use: ""short and blunt"", ""rambles when nervous"", ""teases everyone"", ""swears at tools"". NEVER analyst's language — no words like 'discourse', 'abstract', 'emotional appeals', 'processes', 'concepts', 'efficiency'.
- These are frontier folk — farmers, cooks, fighters. Reserve a bookish or technical way of speaking for a pawn whose background truly is one (a scientist, an engineer, a professor) — and even then it is HOW A PERSON TALKS, not how a paper reads.
- Keep it under 15 words.

""sample"" is one line they might actually say out loud on an ordinary day — everyday spoken words in THEIR voice, under 15 words. It is the single strongest signal of how they sound; make it carry the style.";

        public const string SystemPrompt = @"Define how this RimWorld colonist SPEAKS out loud, so their dialogue sounds distinct.

" + StyleAndSampleRules + @"

Respond in valid JSON:
{ ""style"": ""short and blunt; dry jokes; never talks about feelings"",
  ""sample"": ""Fence won't mend itself. Hand me the hammer and quit gawking."",
  ""pace"": ""slow|measured|fast"",
  ""timbre"": ""warm|gruff|bright|flat|breathy|clipped"" }";

        public static PsychologyPrompt Compose(string name, string traits, string psychology, string personality)
        {
            string user = $@"Colonist: {name}
Traits: {traits}
Psychology: {psychology}
Personality: {personality}";
            return new PsychologyPrompt { system = SystemPrompt, user = user };
        }
    }
}
