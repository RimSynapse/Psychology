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
    /// LLM-driven adulthood backstory assignment for colony-born pawns.
    /// 
    /// When a pawn reaches biological age 20 and has no adulthood backstory
    /// (story.Adulthood == null), this system:
    ///   1. Gathers their accumulated memories
    ///   2. Filters valid BackstoryDef candidates
    ///   3. Sends to LLM to pick the best-fitting one
    ///   4. Applies it, unassigns conflicting work, notifies the player
    /// </summary>
    public partial class SynapsePawnComp
    {
        private bool hasCheckedAdulthood = false;
        private bool isSelectingAdulthood = false;

        /// <summary>
        /// Called from CompTickRare. Checks if this colonist has aged into adulthood
        /// without a backstory and triggers LLM selection.
        /// </summary>
        private void CheckAdulthoodBackstoryNeeded(Pawn pawn)
        {
            if (hasCheckedAdulthood || isSelectingAdulthood) return;
            if (pawn.story?.Adulthood != null) { hasCheckedAdulthood = true; return; }
            if (pawn.ageTracker.AgeBiologicalYears < 20) return;
            if (pawn.Faction != Faction.OfPlayer) return;

            // This pawn is 20+ with no adulthood backstory — trigger LLM selection
            hasCheckedAdulthood = true;
            isSelectingAdulthood = true;

            var coreComp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (coreComp == null || !SynapseClient.IsOnline) { isSelectingAdulthood = false; return; }

            SelectAdulthoodBackstory(pawn, coreComp);
        }

        private void SelectAdulthoodBackstory(Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            // Gather all valid adulthood backstories for this pawn's kind
            var candidates = GetAdulthoodCandidates(pawn);
            if (candidates.Count == 0) { isSelectingAdulthood = false; return; }

            // Format memories for the LLM
            string memoriesContext = FormatMemoriesForSelection(coreComp);

            // Format candidates list
            string candidatesList = FormatCandidatesList(candidates);

            string traits = pawn.story?.traits?.allTraits != null
                ? string.Join(", ", pawn.story.traits.allTraits.Select(t => t.LabelCap))
                : "None";

            string childhoodTitle = pawn.story?.Childhood?.title ?? "Unknown";
            string hometownContext = !string.IsNullOrEmpty(coreComp.hometown) ? $"\nHometown: {coreComp.hometown}" : "";

            string systemPrompt = @"You are a psychologist assessing which adulthood identity best fits a colonist who just turned 20.
Based on their childhood backstory, accumulated life memories, and personality traits, select the BEST fitting adulthood backstory from the numbered list.

RULES:
- Choose the backstory that most naturally follows from their lived experience and memories
- Consider their skill development — a pawn who spent years healing should become a medic, not a soldier
- Each candidate shows its skill bonuses and any work it DISABLES
- Be aware: choosing a backstory that disables work they currently do WILL affect gameplay
- After selecting, write a brief 100-word third-person memory of the moment they embraced this identity (using their name or ""he/she"", never ""I"" or ""my"")

You MUST respond in valid JSON:
{
  ""ChosenNumber"": 3,
  ""Reasoning"": ""Their memories of tending to injured colonists and studying under Dr. Kara..."",
  ""Memory"": ""Fred realized this was who he was meant to be...(100 words)..."",
  ""Tags"": [""Adulthood"", ""Identity"", ""Transition""]
}";

            string userMessage = $@"Colonist: {pawn.Name.ToStringShort}
Age: {pawn.ageTracker.AgeBiologicalYears}
Gender: {pawn.gender}
Childhood Backstory: ""{childhoodTitle}""
Traits: {traits}{hometownContext}

=== LIFE MEMORIES ===
{memoriesContext}

=== CANDIDATE ADULTHOOD BACKSTORIES ===
{candidatesList}

Select the best-fitting adulthood backstory by number.";

            var options = new ChatOptions { priority = 5, requestName = "Select Adulthood Backstory", targetName = pawn.Name.ToStringShort }; // High priority — this is a milestone event

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => OnAdulthoodSelected(result, pawn, coreComp, candidates),
                options
            );
        }

        private void OnAdulthoodSelected(ChatResult result, Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp, List<BackstoryDef> candidates)
        {
            isSelectingAdulthood = false;

            if (!result.success)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to select adulthood backstory for {pawn.Name.ToStringShort}: {result.error}");
                hasCheckedAdulthood = false; // Allow retry
                return;
            }

            try
            {
                string json = JsonHelper.ExtractJson(result.content);
                if (json == null) { RimSynapse.SynapseLogger.Warn("psychology", "[RimSynapse-Psychology] No JSON in adulthood selection response."); hasCheckedAdulthood = false; return; }

                var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (parsed == null || !parsed.ContainsKey("ChosenNumber")) { hasCheckedAdulthood = false; return; }

                int chosenIdx = Convert.ToInt32(parsed["ChosenNumber"]) - 1; // 1-indexed in prompt
                if (chosenIdx < 0 || chosenIdx >= candidates.Count)
                {
                    RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] LLM chose invalid backstory index {chosenIdx + 1} for {pawn.Name.ToStringShort}.");
                    hasCheckedAdulthood = false;
                    return;
                }

                BackstoryDef chosen = candidates[chosenIdx];

                // Track which work types were enabled before
                var previouslyEnabled = pawn.workSettings?.WorkGiversInOrderNormal?.Select(wg => wg.def.workType).Distinct().ToList();

                // Apply the backstory
                pawn.story.Adulthood = chosen;

                // Check for newly disabled work types and handle conflicts
                var disabledWork = chosen.workDisables;
                var conflictingJobs = new List<string>();

                if (disabledWork != WorkTags.None && previouslyEnabled != null)
                {
                    foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if ((workType.workTags & disabledWork) != WorkTags.None)
                        {
                            if (pawn.workSettings != null && pawn.workSettings.GetPriority(workType) > 0)
                            {
                                conflictingJobs.Add(workType.labelShort ?? workType.defName);
                                pawn.workSettings.SetPriority(workType, 0); // Disable conflicting work
                            }
                        }
                    }
                }

                // Store the transition memory
                if (parsed.ContainsKey("Memory"))
                {
                    string memoryText = parsed["Memory"].ToString();
                    var tags = new List<string> { "Adulthood", "Identity", "Transition" };
                    if (parsed.ContainsKey("Tags") && parsed["Tags"] is Newtonsoft.Json.Linq.JArray arr)
                        tags = arr.Select(t => t.ToString()).ToList();

                    long nowTick = SynapseDateHelper.GetCurrentAbsTick();
                    coreComp.memories.Add(new WeightedMemory
                    {
                        summary = memoryText,
                        weight = 3.0f,
                        baseWeight = 3.0f,
                        decayRate = 0f,
                        tags = tags,
                        memoryType = "BackstoryAdulthood",
                        absTick = nowTick,
                        gameTick = Find.TickManager.TicksGame
                    });
                }

                // Notify the player
                string letterText = $"{pawn.Name.ToStringShort} has come of age and embraced a new identity: {chosen.title}.\n\n";
                if (parsed.ContainsKey("Reasoning"))
                    letterText += parsed["Reasoning"].ToString() + "\n\n";
                if (conflictingJobs.Any())
                    letterText += $"<b>Work affected:</b> {pawn.Name.ToStringShort} can no longer do: {string.Join(", ", conflictingJobs)}. Their work assignments have been updated.";

                Find.LetterStack.ReceiveLetter(
                    $"{pawn.Name.ToStringShort} comes of age",
                    letterText,
                    LetterDefOf.NeutralEvent,
                    pawn
                );

                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Adulthood backstory assigned for {pawn.Name.ToStringShort}: {chosen.title}. Conflicts: {string.Join(", ", conflictingJobs)}");
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to apply adulthood backstory: {ex.Message}");
                hasCheckedAdulthood = false; // Allow retry
            }
        }

        // ────────────────────────────────────────────────────────
        //  Helpers for adulthood selection
        // ────────────────────────────────────────────────────────

        private List<BackstoryDef> GetAdulthoodCandidates(Pawn pawn)
        {
            // Get all adulthood backstories
            var allAdult = DefDatabase<BackstoryDef>.AllDefsListForReading
                .Where(b => b.slot == BackstorySlot.Adulthood)
                .ToList();

            // Filter by pawn's backstory categories if available
            if (pawn.kindDef?.backstoryCategories != null && pawn.kindDef.backstoryCategories.Count > 0)
            {
                var catFiltered = allAdult.Where(b =>
                    b.spawnCategories != null &&
                    b.spawnCategories.Any(sc => pawn.kindDef.backstoryCategories.Contains(sc))
                ).ToList();

                if (catFiltered.Count > 5) allAdult = catFiltered;
            }

            // Take a reasonable subset — shuffle and pick ~12-15 candidates
            allAdult.Shuffle();
            return allAdult.Take(15).ToList();
        }

        private string FormatMemoriesForSelection(RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            if (coreComp.memories.Count == 0) return "No memories recorded.";

            var sb = new System.Text.StringBuilder();
            foreach (var mem in coreComp.memories.OrderBy(m => m.absTick))
            {
                string typeLabel = mem.memoryType ?? "Memory";
                sb.AppendLine($"[{typeLabel}] {mem.summary}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string FormatCandidatesList(List<BackstoryDef> candidates)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < candidates.Count; i++)
            {
                var b = candidates[i];
                string skills = "None";
                if (b.skillGains != null && b.skillGains.Count > 0)
                {
                    skills = string.Join(", ", b.skillGains.Select(sg =>
                    {
                        string sign = sg.amount >= 0 ? "+" : "";
                        return $"{sign}{sg.amount} {sg.skill.label}";
                    }));
                }

                string disabled = b.workDisables != WorkTags.None
                    ? $" | Disables: {b.workDisables}"
                    : "";

                sb.AppendLine($"{i + 1}. \"{b.title}\" — {b.description?.TrimEnd() ?? "No description."}");
                sb.AppendLine($"   Skills: {skills}{disabled}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}


