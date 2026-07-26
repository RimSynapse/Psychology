using System;
using System.Linq;
using System.Collections.Generic;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;
using RimSynapse.Models;
using RimSynapse.Utils;
using RimSynapse.Psychology.Models;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Opportunistic background tasks: Memory generation from PastEvents,
    /// visitor backstory generation, and importance filtering.
    /// </summary>
    public static partial class SynapsePsychology
    {
        /// <summary>
        /// Triggered by the Core framework when the LLM queue is idle.
        /// Generates a low-priority background memory for 1-3 colonists based on the oldest PastEvent.
        /// </summary>
        public static bool TriggerOpportunisticMemory()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return false;

            var coreComp = Find.World.GetComponent<SynapseCoreWorldComponent>();
            if (coreComp == null || !coreComp.TryDequeuePastEvent(out PastEvent pastEvent)) return false;

            // Pick up to 3 colonists to generate memories for
            var pawns = Find.CurrentMap.mapPawns.FreeColonists.OrderBy(_ => Rand.Value).Take(3).ToList();
            if (pawns.Count == 0) return false;

            string systemPrompt = @"You are the AI storyteller evaluating the psychology and memories of the specified RimWorld colonists.
A significant event just occurred. For EACH colonist, write a third-person evaluative memory (using their name or 'he/she', never 'I' or 'my') describing how they likely felt at the time the event occurred.

INSTRUCTIONS:
1. EVALUATIVE NARRATION: Do not just write melodrama. Blends their personal traits, current needs (e.g., hunger, mood), and environment to evaluate their state:
   - If they are starving or in danger, their worry about survival might dull their grief over a friend's death.
   - If life is exceptionally comfortable (high mood/wealth), they might find recovery from tragedy easier.
   - If they have traits like Psychopath, they might focus on cold rain or practical matters rather than emotional loss.
2. EVENT SCALE & LENGTH:
   - MAJOR EVENTS (e.g., death of a spouse/close relative, first kill, a colonist joining, legendary craft, major raid): Generate a detailed 2-3 sentence narrative memory. Set Weight between 3.0 and 5.0, and Decay between 0.0 and 0.005 (or 0.0 for permanent defining memories).
   - STANDARD/MINOR EVENTS: Generate a brief, single-sentence memory (10-15 words). Set Weight between 0.1 and 1.5, and Decay between 0.05 and 0.3.
3. DESENSITIZATION: Consider their lifetime statistics. A veteran killer is not traumatized by another death, but their first kill is life-altering.

You MUST respond strictly in valid JSON format:
{
  ""Memories"": [
    {
      ""PawnId"": ""ThingID_Here"",
      ""Summary"": ""Though the loss of his friend weighed on Fred, his immediate starvation and the falling cold rain occupied his mind, leaving him feeling strangely detached from the tragedy."",
      ""Tags"": [""Death"", ""Grief"", ""Starvation""],
      ""Weight"": 1.2,
      ""Decay"": 0.05
    }
  ]
}";

            string userMessage = $"Event: {pastEvent.eventDescription}\nColony Status at the time: {pastEvent.colonySnapshot}\n\nTarget Pawns:\n";
            foreach (var pawn in pawns)
            {
                var pCoreComp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
                float threshold = RimSynapsePsychologyMod.Settings != null ? RimSynapsePsychologyMod.Settings.sensitivityThreshold : 0.5f;
                string lifetimeBurdens = pCoreComp != null ? pCoreComp.GetTopMemoryBurdens(3, threshold) : "None";
                
                int lifetimeKills = pawn.records?.GetAsInt(RecordDefOf.KillsHumanlikes) ?? 0;
                
                string snapshot = pastEvent.pawnSnapshots != null && pastEvent.pawnSnapshots.ContainsKey(pawn.ThingID) 
                    ? pastEvent.pawnSnapshots[pawn.ThingID] 
                    : "Unknown";
                    
                userMessage += $"- Name: {pawn.Name.ToStringShort}, ID: {pawn.ThingID}\n  Status at the time: {snapshot}\n  Lifetime Kills: {lifetimeKills}\n  Current Psychological Burdens: {lifetimeBurdens}\n\n";
            }

            // Use priority -1 so it stays at the absolute bottom of the queue and yields to real events
            var options = new ChatOptions { priority = 8, requestName = "Opportunistic Memory", targetName = "Multiple Pawns" };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => 
                {
                    if (result.success)
                    {
                        try
                        {
                            string json = JsonHelper.ExtractJson(result.content);
                            if (json == null) { RimSynapse.SynapseLogger.Warn("psychology", "[RimSynapse-Psychology] No JSON found in memory response."); return; }

                            var parsed = JsonConvert.DeserializeObject<Dictionary<string, List<Dictionary<string, object>>>>(json);
                            if (parsed != null && parsed.ContainsKey("Memories"))
                            {
                                foreach (var memDict in parsed["Memories"])
                                {
                                    string pawnId = memDict["PawnId"].ToString();
                                    Pawn targetPawn = pawns.FirstOrDefault(p => p.ThingID == pawnId);
                                    if (targetPawn != null)
                                    {
                                        var tagsList = new List<string>();
                                        if (memDict.ContainsKey("Tags") && memDict["Tags"] is Newtonsoft.Json.Linq.JArray arr)
                                        {
                                            tagsList = arr.Select(t => t.ToString()).ToList();
                                        }

                                        AddMemory(targetPawn, new WeightedMemory
                                        {
                                            summary = memDict["Summary"].ToString(),
                                            weight = Convert.ToSingle(memDict["Weight"]),
                                            baseWeight = Convert.ToSingle(memDict["Weight"]),
                                            decayRate = Convert.ToSingle(memDict["Decay"]) * RimSynapsePsychologyMod.Settings.memoryDecayMultiplier,
                                            tags = tagsList,
                                            memoryType = "EventReflection",
                                            absTick = SynapseDateHelper.GameTickToAbsTick(pastEvent.gameTick),
                                            gameTick = pastEvent.gameTick
                                        });
                                    }
                                }
                                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Opportunistic memories generated for {pastEvent.eventDescription}.");
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to parse JSON memories: {ex.Message}\nContent: {result.content}");
                        }
                    }
                },
                options
            );
            return true;
        }
        /// <summary>
        /// Triggered by the Core framework when the LLM queue is idle.
        /// Generates memory-first backstories for important non-colonist visitors.
        /// Visitors get 1-2 memories (one per backstory they have) + hometown, no personality synthesis.
        /// </summary>
        public static bool TriggerOpportunisticVisitorBackstory()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return false;

            var targetPawn = Find.CurrentMap.mapPawns.AllPawnsSpawned
                .Where(p => p.RaceProps.Humanlike && !p.IsColonist && !p.Faction?.IsPlayer == true && IsImportantPawn(p) && NeedsBackstory(p))
                .RandomElementWithFallback();

            if (targetPawn == null) return false;

            var coreComp = targetPawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (coreComp == null) return false;

            string factionName = targetPawn.Faction?.Name ?? "Outlander";
            string factionType = targetPawn.Faction?.def?.LabelCap ?? "Faction";

            // Start with childhood if available, otherwise go straight to adulthood
            if (targetPawn.story?.Childhood != null)
            {
                GenerateVisitorChildhoodMemory(targetPawn, coreComp, factionName, factionType);
            }
            else if (targetPawn.story?.Adulthood != null)
            {
                GenerateVisitorAdulthoodMemory(targetPawn, coreComp, factionName, factionType);
            }
            else
            {
                return false; // No backstory data at all
            }
            return true;
        }




        /// <summary>
        /// Triggered by the Core framework when the LLM queue is idle.
        /// Scans for a colonist who is flagged for a daily journal update and evaluates their profile.
        /// </summary>
        public static bool TriggerOpportunisticProfileEvaluation()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return false;

            // Find a colonist awaiting a journal update
            var targetPawn = Find.CurrentMap.mapPawns.FreeColonists
                .Where(p => {
                    var comp = p.TryGetComp<SynapsePawnComp>();
                    return comp != null && comp.isAwaitingJournalUpdate;
                })
                .RandomElementWithFallback();

            if (targetPawn == null) return false;

            var pawnComp = targetPawn.TryGetComp<SynapsePawnComp>();
            var coreComp = targetPawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            
            if (pawnComp == null || coreComp == null) return false;

            var memories = coreComp.memories;
            float averageMood = pawnComp.savedAverageMood;
            int currentDay = GenDate.DaysPassed;

            // Unset the flag immediately so we don't spam them if the queue fails
            pawnComp.isAwaitingJournalUpdate = false;

            // Route to existing API evaluation method, but override priority to -1
            // (QueueDailyPsychologyReview uses PromptAsync which accepts an options object, we need to modify QueueDailyPsychologyReview slightly to support options or priority -1)
            // Wait, I will just call it and it will enqueue. The opportunistic manager triggers this when the queue is ALREADY empty, so priority doesn't matter as much, it will execute instantly.
            // Actually, we should pass options. Let's look at QueueDailyPsychologyReview in SynapsePsychologyEvaluation.cs to ensure it uses options.
            // Oh, I haven't modified QueueDailyPsychologyReview yet. I will do that next.
            
            QueueDailyPsychologyReview(targetPawn, averageMood, memories, (success) => {
                if (success)
                {
                    pawnComp.lastJournalUpdateDay = currentDay;
                }
                else 
                {
                    // If it failed, flag them again so it retries later
                    pawnComp.isAwaitingJournalUpdate = true;
                }
            }, true); // true = isOpportunistic
            
            return true;
        }


        /// <summary>
        /// Formats skill gains from a BackstoryDef into a readable string.
        /// e.g., "+4 Mining, +2 Crafting, -3 Social"
        /// </summary>
        private static string FormatSkillGains(BackstoryDef backstory)
        {
            if (backstory?.skillGains == null || backstory.skillGains.Count == 0) return "None";
            var parts = new List<string>();
            foreach (var sg in backstory.skillGains)
            {
                string sign = sg.amount >= 0 ? "+" : "";
                parts.Add($"{sign}{sg.amount} {sg.skill.label}");
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Formats disabled work types from a BackstoryDef.
        /// </summary>
        private static string FormatDisabledWork(BackstoryDef backstory)
        {
            if (backstory == null || backstory.workDisables == WorkTags.None) return "";
            var disabled = new List<string>();
            foreach (WorkTags tag in Enum.GetValues(typeof(WorkTags)))
            {
                if (tag == WorkTags.None) continue;
                if ((backstory.workDisables & tag) != 0) disabled.Add(tag.ToString());
            }
            return disabled.Count > 0 ? string.Join(", ", disabled) : "";
        }

        private static bool IsImportantPawn(Pawn p)
        {
            if (p.Faction != null && p.Faction.leader == p) return true;
            if (p.IsPrisonerOfColony) return true;
            var coreComp = p.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (coreComp != null && coreComp.isResident) return true;
            if (RimSynapse.Expansions.Royalty.PsychologyRoyaltyIntegration.IsNoble(p)) return true;
            if (p.relations != null && p.relations.FamilyByBlood.Any(r => r.Faction == Faction.OfPlayer || r.IsPrisonerOfColony)) return true;
            return false;
        }

        public static bool TriggerRelationshipEvaluation()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return false;

            var colonists = Find.CurrentMap.mapPawns.FreeColonists.ToList();
            if (colonists.Count < 2) return false;

            // Find a valid pair
            Pawn pawnA = null;
            Pawn pawnB = null;
            SocialRecord sharedRecord = null;

            foreach (var p in colonists.OrderBy(_ => Rand.Value))
            {
                var comp = p.TryGetComp<SynapsePawnComp>();
                if (comp == null) continue;

                var candidateIds = comp.socialNetwork.Keys.Where(k => 
                    comp.socialNetwork[k].familiarity >= 25f && 
                    comp.socialNetwork[k].relationshipMemories.Count < 5).ToList();
                
                if (candidateIds.Count == 0) continue;

                string targetId = candidateIds.RandomElement();
                Pawn target = colonists.FirstOrDefault(c => c.GetUniqueLoadID() == targetId);
                
                if (target != null)
                {
                    pawnA = p;
                    pawnB = target;
                    sharedRecord = comp.socialNetwork[targetId];
                    break;
                }
            }

            if (pawnA == null || pawnB == null) return false;

            string systemPrompt = @"You are the internal monologue of a colonist on a RimWorld.
Evaluate your relationship with the specified pawn based on your traits, their traits, your familiarity level (0-100), and your trust level (-100 to 100).
Write a single, highly personal 'Relationship Memory' (1-2 sentences) summarizing exactly how you feel about them. 
This might be recited at their funeral or your wedding, so make it deeply personal, referencing trust, betrayals, or shared burdens.

You MUST respond strictly in valid JSON format:
{
  ""Memory"": ""I used to think Val was just a stuck-up noble, but after she dragged me out of that mech cluster, I'd trust her with my life.""
}";

            var compA = pawnA.GetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            var compB = pawnB.GetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            string burdensA = compA != null ? compA.GetTopMemoryBurdens(3, 0.5f) : "None";
            string burdensB = compB != null ? compB.GetTopMemoryBurdens(3, 0.5f) : "None";

            string userMessage = $"Your Name: {pawnA.Name.ToStringShort} (Gender: {pawnA.gender})\nYour Traits: {pawnA.story?.traits?.allTraits.Select(t => t.Label).ToCommaList() ?? "None"}\nYour Burdens: {burdensA}\n\n" +
                                 $"Their Name: {pawnB.Name.ToStringShort} (Gender: {pawnB.gender})\nTheir Traits: {pawnB.story?.traits?.allTraits.Select(t => t.Label).ToCommaList() ?? "None"}\nTheir Burdens: {burdensB}\n\n" +
                                 $"Familiarity (0-100): {sharedRecord.familiarity:F0}\nTrust (-100 to 100): {sharedRecord.trust:F0}";

            var options = new ChatOptions { priority = 3, requestName = "Relationship Evaluation", targetName = $"{pawnA.Name.ToStringShort} -> {pawnB.Name.ToStringShort}" };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => 
                {
                    if (result.success)
                    {
                        try
                        {
                            string json = JsonHelper.ExtractJson(result.content);
                            if (json == null) return;

                            var parsed = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(json);
                            if (parsed != null && parsed.ContainsKey("Memory"))
                            {
                                string memory = parsed["Memory"];
                                var pAComp = pawnA.GetComp<SynapsePawnComp>();
                                if (pAComp != null)
                                {
                                    string bId = pawnB.GetUniqueLoadID();
                                    if (!pAComp.socialNetwork.ContainsKey(bId)) pAComp.socialNetwork[bId] = new SocialRecord();
                                    pAComp.socialNetwork[bId].relationshipMemories.Add(memory);
                                    
                                    RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Generated relationship memory for {pawnA.Name.ToStringShort} regarding {pawnB.Name.ToStringShort}.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to parse relationship evaluation: {ex.Message}");
                        }
                    }
                },
                options);

            return true;
        }
    }
}


