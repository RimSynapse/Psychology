namespace RimSynapse.Psychology.Prompts
{
    /// <summary>
    /// PURE, Verse-free composer for the childhood backstory-memory prompt: a vivid third-person memory +
    /// hometown from the colonist's childhood backstory, skills and disabled work. Authored once here; the
    /// game-free Prompt Lab links this file. (The adulthood-memory / life-analysis prompts in the same comp
    /// follow this exact shape and can be extracted the same way.)
    /// </summary>
    public static class ChildhoodMemoryPromptComposer
    {
        public const string SystemPrompt = @"You are writing a vivid third-person memory for a colonist in the RimWorld universe, as if the AI Storyteller is describing their childhood.
This memory is from their CHILDHOOD. It should be a specific, concrete scene — not a summary.

RULES:
- Write 100-200 words, third person (using their name or ""he/she"", never ""I"" or ""my"")
- This is a SINGLE vivid memory, not a life summary
- Ground the memory in the skill bonuses: if they got +4 Mining, describe WHY through experience (e.g., ""Josema spent years chipping limestone..."")
- If work types are disabled, hint at WHY (trauma, cultural taboo, physical limitation)
- The memory should feel personal and emotionally resonant — a moment they'd actually remember
- RimWorld setting: frontier planets, crashlanded survivors, tribal societies, harsh conditions
- You MUST also generate a ""Hometown"" — their place of origin. This should match their background:
  - Outlander/Settler → a named settlement or outpost (e.g., ""Kharstead"", ""Port Valen"")
  - Tribal → a geographic feature, camp, or caravan route (e.g., ""the Redstone caravan"", ""the marshlands east of Sleeping Ridge"")
  - Pirate → a ship, station, or raider den (e.g., ""the Rust Fang"", ""Scrapheap Station"")
  - Imperial → a named city or estate (e.g., ""the Stellarch's court at Novium"")
  - If their backstory implies they moved a lot or are orphaned, something vague is fine (""the roads between nowhere"")

You MUST respond in valid JSON:
{
  ""Memory"": ""Josema remembered the first time he...(100-200 words)..."",
  ""Hometown"": ""Kharstead"",
  ""Tags"": [""Origin"", ""Childhood"", ""Mining""],
  ""EmotionalTone"": ""bittersweet""
}";

        /// <param name="roleLabel">The colony-role label the game prefixes (e.g. "Colonist").</param>
        /// <param name="extraContext">Already-formatted trailing context (faction categories + cross-mod
        /// context), appended verbatim as the game does.</param>
        public static PsychologyPrompt Compose(string roleLabel, string name, string gender, string factionType,
            string childhoodTitle, string childhoodDesc, string skillBonuses, string disabledWork, string extraContext)
        {
            string disabledClause = string.IsNullOrEmpty(disabledWork) ? "" : $"Disabled Work Types: {disabledWork}\n";
            string user = $@"{roleLabel}: {name}
Gender: {gender}
Faction Background: {factionType}
Childhood Backstory: ""{childhoodTitle}""
Vanilla Description: ""{childhoodDesc}""
Skill Bonuses from Childhood: {skillBonuses}
{disabledClause}{extraContext}
Write a vivid childhood memory grounded in these skills.";
            return new PsychologyPrompt { system = SystemPrompt, user = user };
        }
    }
}
