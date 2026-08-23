namespace RimSynapse.Psychology.Prompts
{
    /// <summary>
    /// PURE, Verse-free composer for the Psychology voice-profile prompt — the one that DERIVES the
    /// <c>voiceProfile</c> the Conversations dialogue prompt then consumes (so tuning it is directly tied to
    /// the Conversations #44 register work). Given a colonist's traits + psychology + personality it produces
    /// the exact system+user pair <c>DeriveVoiceProfile</c> sends. Authored once here; the game-free Prompt Lab
    /// links this file, so the lab can't drift from the game.
    /// </summary>
    public static class VoiceProfilePromptComposer
    {
        public const string SystemPrompt = @"From a colonist's personality, define how they SPEAK so their dialogue sounds distinct. Respond in valid JSON:
{ ""style"": ""how they talk: sentence length, diction, humour, verbal tics, what they avoid saying"",
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
