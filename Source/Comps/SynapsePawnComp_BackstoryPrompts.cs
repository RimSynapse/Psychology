using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Models;
using RimSynapse.Utils;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.Comps
{
    /// <summary>
    /// LLM prompt construction and callbacks for the three-step colonist backstory pipeline:
    /// Step 1 (Childhood), Step 2 (Adulthood), Step 3 (Personality Profile).
    /// </summary>
    public partial class SynapsePawnComp
    {
        // ────────────────────────────────────────────────────────
        //  Step 1: Childhood Memory
        // ────────────────────────────────────────────────────────

        private void GenerateChildhoodMemory(Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            var childhood = pawn.story?.Childhood;
            string childhoodTitle = childhood?.title ?? "Unknown";
            string childhoodDesc = childhood?.description ?? "An unremarkable childhood.";

            string skillBonuses = FormatSkillGains(childhood);
            string disabledWork = FormatDisabledWork(childhood);

            string factionType = pawn.Faction?.def?.LabelCap ?? "Unknown";
            string factionContext = "";
            if (pawn.kindDef?.backstoryCategories != null && pawn.kindDef.backstoryCategories.Count > 0)
            {
                factionContext = $"\nBackstory Categories: {string.Join(", ", pawn.kindDef.backstoryCategories)}";
            }
            
            // Allow Core/Factions to inject extra context (like Ideology)
            string crossModContext = RimSynapse.SynapseCoreContext.GatherGenericContext(pawn, "BackstoryChildhood");

            string systemPrompt = @"You are writing a vivid third-person memory for a colonist in the RimWorld universe, as if the AI Storyteller is describing their childhood.
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

            string userMessage = $@"Colonist: {pawn.Name.ToStringShort}
Gender: {pawn.gender}
Faction Background: {factionType}
Childhood Backstory: ""{childhoodTitle}""
Vanilla Description: ""{childhoodDesc}""
Skill Bonuses from Childhood: {skillBonuses}
{(string.IsNullOrEmpty(disabledWork) ? "" : $"Disabled Work Types: {disabledWork}\n")}{factionContext}{crossModContext}
Write a vivid childhood memory grounded in these skills.";

            var options = new ChatOptions { priority = 1, requestName = "Childhood Backstory", targetName = pawn.Name.ToStringShort };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => OnChildhoodMemoryGenerated(result, pawn, coreComp),
                options
            );
        }

        private void OnChildhoodMemoryGenerated(ChatResult result, Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            if (!result.success)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed childhood memory for {pawn.Name.ToStringShort}: {result.error}");
                isGeneratingBackstory = false;
                ticksToGenerateBackstory = 5000;
                return;
            }

            try
            {
                string json = JsonHelper.ExtractJson(result.content);
                if (json == null) { RimSynapse.SynapseLogger.Warn("psychology", "[RimSynapse-Psychology] No JSON in childhood memory response."); isGeneratingBackstory = false; return; }

                var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (parsed == null || !parsed.ContainsKey("Memory")) { isGeneratingBackstory = false; return; }

                string memoryText = parsed["Memory"].ToString();
                var tags = new List<string> { "Childhood", "Origin" };
                if (parsed.ContainsKey("Tags") && parsed["Tags"] is Newtonsoft.Json.Linq.JArray arr)
                {
                    tags = arr.Select(t => t.ToString()).ToList();
                    if (!tags.Contains("Childhood")) tags.Insert(0, "Childhood");
                }

                if (parsed.ContainsKey("Hometown"))
                {
                    coreComp.hometown = parsed["Hometown"].ToString();
                }

                long childTick = SynapseDateHelper.GetChildhoodMemoryTick(pawn);
                coreComp.memories.Add(new WeightedMemory
                {
                    summary = memoryText,
                    weight = 3.0f,
                    baseWeight = 3.0f,
                    decayRate = 0f,
                    tags = tags,
                    memoryType = "BackstoryChildhood",
                    absTick = childTick,
                    gameTick = (int)(childTick - SynapseDateHelper.GetAdjustmentTick())
                });

                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Childhood memory generated for {pawn.Name.ToStringShort} ({memoryText.Length} chars).");

                GenerateAdulthoodMemory(pawn, coreComp);
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to parse childhood memory: {ex.Message}");
                isGeneratingBackstory = false;
                ticksToGenerateBackstory = 5000;
            }
        }

        // ────────────────────────────────────────────────────────
        //  Step 2: Adulthood Memory
        // ────────────────────────────────────────────────────────

        private void GenerateAdulthoodMemory(Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            var adulthood = pawn.story?.Adulthood;
            string adulthoodTitle = adulthood?.title ?? "Unknown";
            string adulthoodDesc = adulthood?.description ?? "An uneventful adult life.";

            string skillBonuses = FormatSkillGains(adulthood);
            string disabledWork = FormatDisabledWork(adulthood);

            var childhoodMemory = coreComp.memories.LastOrDefault(m => m.memoryType == "BackstoryChildhood");
            string childhoodContext = childhoodMemory != null
                ? $"\nChildhood Memory (already generated — maintain continuity):\n\"{childhoodMemory.summary}\""
                : "";
                
            // Allow Core/Factions to inject extra context
            string crossModContext = RimSynapse.SynapseCoreContext.GatherGenericContext(pawn, "BackstoryAdulthood");

            string systemPrompt = @"You are writing a vivid third-person memory for a colonist in the RimWorld universe, as if the AI Storyteller is describing their adulthood.
This memory is from their ADULTHOOD. It should be a specific, concrete scene — not a summary.

RULES:
- Write 100-200 words, third person (using their name or ""he/she"", never ""I"" or ""my"")
- This is a SINGLE vivid memory, not a life summary
- Ground the memory in the skill bonuses: if they got +6 Shooting, describe the experience that gave them that skill
- If work types are disabled, hint at WHY (trauma, injury, philosophical opposition)
- If a childhood memory is provided, maintain narrative continuity (same character, consistent tone)
- The memory should mark a turning point or defining moment in their adult life
- RimWorld setting: frontier planets, caravans, sieges, research labs, tribal wars

You MUST respond in valid JSON:
{
  ""Memory"": ""The day Josema first...(100-200 words)..."",
  ""Tags"": [""Adulthood"", ""Combat"", ""Survival""],
  ""EmotionalTone"": ""determined""
}";

            string userMessage = $@"Colonist: {pawn.Name.ToStringShort}
Gender: {pawn.gender}
Adulthood Backstory: ""{adulthoodTitle}""
Vanilla Description: ""{adulthoodDesc}""
Skill Bonuses from Adulthood: {skillBonuses}
{(string.IsNullOrEmpty(disabledWork) ? "" : $"Disabled Work Types: {disabledWork}\n")}{childhoodContext}{crossModContext}

Write a vivid adulthood memory grounded in these skills.";

            var options = new ChatOptions { priority = 2, requestName = "Adulthood Backstory", targetName = pawn.Name.ToStringShort };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => OnAdulthoodMemoryGenerated(result, pawn, coreComp),
                options
            );
        }

        private void OnAdulthoodMemoryGenerated(ChatResult result, Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            if (!result.success)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed adulthood memory for {pawn.Name.ToStringShort}: {result.error}");
                FinalizeBackstory(pawn, coreComp);
                return;
            }

            try
            {
                string json = JsonHelper.ExtractJson(result.content);
                if (json == null) { RimSynapse.SynapseLogger.Warn("psychology", "[RimSynapse-Psychology] No JSON in adulthood memory response."); FinalizeBackstory(pawn, coreComp); return; }

                var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (parsed == null || !parsed.ContainsKey("Memory")) { FinalizeBackstory(pawn, coreComp); return; }

                string memoryText = parsed["Memory"].ToString();
                var tags = new List<string> { "Adulthood" };
                if (parsed.ContainsKey("Tags") && parsed["Tags"] is Newtonsoft.Json.Linq.JArray arr)
                {
                    tags = arr.Select(t => t.ToString()).ToList();
                    if (!tags.Contains("Adulthood")) tags.Insert(0, "Adulthood");
                }

                long adultTick = SynapseDateHelper.GetAdulthoodMemoryTick(pawn);
                coreComp.memories.Add(new WeightedMemory
                {
                    summary = memoryText,
                    weight = 3.0f,
                    baseWeight = 3.0f,
                    decayRate = 0f,
                    tags = tags,
                    memoryType = "BackstoryAdulthood",
                    absTick = adultTick,
                    gameTick = (int)(adultTick - SynapseDateHelper.GetAdjustmentTick())
                });

                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Adulthood memory generated for {pawn.Name.ToStringShort} ({memoryText.Length} chars).");
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to parse adulthood memory: {ex.Message}");
            }

            GeneratePersonalityProfile(pawn, coreComp);
        }

        // ────────────────────────────────────────────────────────
        //  Step 3: Personality Profile (using memories as context)
        // ────────────────────────────────────────────────────────

        private void GeneratePersonalityProfile(Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            string traits = string.Join(", ", pawn.story?.traits?.allTraits?.Select(t => t.Label) ?? Enumerable.Empty<string>());

            var childhoodMem = coreComp.memories.LastOrDefault(m => m.memoryType == "BackstoryChildhood");
            var adulthoodMem = coreComp.memories.LastOrDefault(m => m.memoryType == "BackstoryAdulthood");

            string memoriesContext = "";
            if (childhoodMem != null) memoriesContext += $"Childhood Memory:\n\"{childhoodMem.summary}\"\n\n";
            if (adulthoodMem != null) memoriesContext += $"Adulthood Memory:\n\"{adulthoodMem.summary}\"\n\n";
            
            // Allow Core/Factions to inject extra context
            string crossModContext = RimSynapse.SynapseCoreContext.GatherGenericContext(pawn, "PersonalityProfile");

            string systemPrompt = @"You are analyzing the psychology of a RimWorld colonist based on their life memories.
Given their childhood memory, adulthood memory, and current personality traits, synthesize a psychological profile.

OUTPUT FORMAT:
1. [PERSONALITY] — A 2-3 sentence personality summary (third person). How do they come across? What drives them? What are they afraid of?
2. [ARCHETYPES] — Exactly 3 psychological archetypes on one line, comma-separated:
   - Jungian Type (MBTI: e.g., ENFP, INTJ, INFJ, ESTP)
   - Core Archetype (e.g., Caregiver, Outlaw, Creator, Sage, Ruler, Hero, Explorer, Jester)
   - Temperament (e.g., Sanguine, Melancholic, Phlegmatic, Choleric)
3. [FIRST_IMPRESSION] — A detailed 2-3 sentence narrative memory of the pawn's arrival at the colony. Written in the third person perspective (using their name or ""he/she"", never ""I"" or ""my""). This should connect their background, childhood dreams, and aspirations to their landing context (e.g. waking from cryosleep).

You MUST respond in valid JSON:
{
  ""Personality"": ""She is a quiet, resourceful survivor who..."",
  ""JungianType"": ""INTJ"",
  ""CoreArchetype"": ""Explorer"",
  ""Temperament"": ""Melancholic"",
  ""FirstImpression"": ""Fred stepped out of cryosleep after decades in the void. Though this harsh desert outpost was far from the lush forest worlds of his childhood dreams, he was resolved to make it his home and build something lasting.""
}";

            string userMessage = $@"Colonist: {pawn.Name.ToStringShort}
Gender: {pawn.gender}
Age: {pawn.ageTracker?.AgeBiologicalYears ?? 0}
Current Traits: {traits}

{memoriesContext}{crossModContext}
Synthesize their psychological profile.";

            var options = new ChatOptions { priority = 3, requestName = "Psychological Profile", targetName = pawn.Name.ToStringShort };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => OnPersonalityProfileGenerated(result, pawn, coreComp),
                options
            );
        }

        private void OnPersonalityProfileGenerated(ChatResult result, Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            if (result.success)
            {
                try
                {
                    string json = JsonHelper.ExtractJson(result.content);
                    if (json != null)
                    {
                        var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (parsed != null)
                        {
                            if (parsed.TryGetValue("Personality", out object personalityObj))
                            {
                                coreComp.personalitySummary = personalityObj.ToString();
                            }

                            coreComp.llmTraits.Clear();
                            if (parsed.TryGetValue("JungianType", out object jungian))
                                coreComp.llmTraits.Add($"Jungian Type: {jungian}");
                            if (parsed.TryGetValue("CoreArchetype", out object archetype))
                                coreComp.llmTraits.Add($"Core Archetype: {archetype}");
                            if (parsed.TryGetValue("Temperament", out object temperament))
                                coreComp.llmTraits.Add($"Temperament: {temperament}");

                            if (parsed.TryGetValue("FirstImpression", out object impression))
                            {
                                coreComp.memories.Add(new WeightedMemory
                                {
                                    summary = impression.ToString(),
                                    weight = 4.0f,
                                    baseWeight = 4.0f,
                                    decayRate = 0.005f,
                                    tags = new List<string> { "Arrival", "FirstImpression" },
                                    memoryType = "Arrival",
                                    absTick = SynapseDateHelper.GetAdjustmentTick(),
                                    gameTick = 0
                                });
                            }

                            BuildDynamicBackstory(pawn, coreComp);

                            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Personality profile synthesized for {pawn.Name.ToStringShort}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to parse personality profile: {ex.Message}");
                }
            }

            // Step 4: Daily Psychological Review
            float mood = pawn.needs?.mood?.CurLevelPercentage ?? 0.5f;
            var recentEvents = coreComp.memories
                .Where(m => Find.TickManager.TicksAbs - m.absTick < 60000)
                .ToList();
                
            RimSynapse.Psychology.API.SynapsePsychology.QueueDailyPsychologyReview(pawn, mood, recentEvents, (success) => {
                FinalizeBackstory(pawn, coreComp);
            }, true);
        }
    }
}
