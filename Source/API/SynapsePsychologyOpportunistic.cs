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

            // Target the pawns actually tied to the episode (Core#88), not 3 random colonists. Each tier
            // colors the memory differently; uninvolved pawns get nothing.
            var targets = new List<KeyValuePair<Pawn, string>>();
            AddTargetTier(targets, pastEvent.involvedPawnIds, "involved");
            AddTargetTier(targets, pastEvent.witnessPawnIds, "witness");
            AddTargetTier(targets, pastEvent.afterEffectPawnIds, "aftermath");

            // Impersonal/legacy events (quests, arrivals) with no involvement: a small colony sample so
            // colony-wide news is still remembered — but personal events no longer hit random bystanders.
            if (targets.Count == 0)
            {
                foreach (var p in Find.CurrentMap.mapPawns.FreeColonists.OrderBy(_ => Rand.Value).Take(2))
                    targets.Add(new KeyValuePair<Pawn, string>(p, "colony"));
            }
            if (targets.Count == 0) return false;
            if (targets.Count > 5) targets = targets.Take(5).ToList();  // bound fan-out on mass events

            bool isEpisode = pastEvent.occurrenceCount > 1;
            string episodeLine = isEpisode
                ? "This was a SUSTAINED episode (it happened many times over a period) — write ONE reflective memory of the whole ordeal, not a play-by-play.\n"
                : "";

            string systemPrompt = @"You are the AI storyteller writing each listed RimWorld colonist's OWN memory of an event.
For EACH colonist, write a third-person memory using THEIR stated pronouns (never 'I'/'my'), colored by their ROLE:
- involved: first-hand — they were in it.
- witness: they saw it happen to others.
- aftermath: they only dealt with the consequences later (e.g. tended the wounded, buried the dead) and never saw the event itself.
- colony: a general colony member reacting to the news.

INSTRUCTIONS:
1. EVALUATIVE NARRATION: blend their traits, mood, needs and burdens — do not just write melodrama. A veteran killer is not traumatized by another death; a first kill is life-altering; a psychopath fixates on practicalities over grief.
2. DYNAMIC LENGTH — match the length to how momentous this was for THIS pawn. A defining, epic ordeal earns a full, vivid multi-sentence memory; a trivial event a single brief line. Do NOT pad a minor event and do NOT truncate a major one. There is no fixed sentence limit.
3. WEIGHT/DECAY: defining events → Weight 0.8-1.0, Decay 0.0-0.05; significant → Weight 0.5-0.8, Decay 0.05-0.15; minor → Weight 0.05-0.5, Decay 0.15-0.5. (0.0-1.0 scale.)
" + episodeLine + @"
You MUST respond strictly in valid JSON:
{
  ""Memories"": [
    { ""PawnId"": ""ThingID_Here"", ""Summary"": ""..."", ""Tags"": [""...""], ""Weight"": 0.6, ""Decay"": 0.15 }
  ]
}";

            string sevStr = pastEvent.severity.ToString();
            string timesStr = isEpisode ? $" (occurred ~{pastEvent.occurrenceCount} times)" : "";
            string userMessage = $"Event [{sevStr}]{timesStr}: {pastEvent.eventDescription}\nColony Status at the time: {pastEvent.colonySnapshot}\n\nColonists:\n";
            foreach (var kv in targets)
            {
                Pawn pawn = kv.Key;
                var pCoreComp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
                float threshold = RimSynapsePsychologyMod.Settings != null ? RimSynapsePsychologyMod.Settings.sensitivityThreshold : 0.5f;
                string lifetimeBurdens = pCoreComp != null ? pCoreComp.GetTopMemoryBurdens(3, threshold) : "None";
                int lifetimeKills = pawn.records?.GetAsInt(RecordDefOf.KillsHumanlikes) ?? 0;
                string snapshot = pastEvent.pawnSnapshots != null && pastEvent.pawnSnapshots.ContainsKey(pawn.ThingID)
                    ? pastEvent.pawnSnapshots[pawn.ThingID]
                    : "Unknown";

                userMessage += $"- Name: {pawn.Name.ToStringShort}, ID: {pawn.ThingID}, Role: {kv.Value}, Pronouns: {PronounsFor(pawn)}\n  Status at the time: {snapshot}\n  Lifetime Kills: {lifetimeKills}\n  Current Psychological Burdens: {lifetimeBurdens}\n\n";
            }

            var pawns = targets.Select(t => t.Key).ToList();
            var options = new ChatOptions { priority = 8, requestName = "Opportunistic Memory", targetName = pastEvent.mcpTag ?? "Episode" };

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
                                            decayRate = Convert.ToSingle(memDict["Decay"]), // class-driven decay applies the multiplier at decay time
                                            tags = tagsList,
                                            memoryType = "EventReflection",
                                            subjectPawnIds = pastEvent.involvedPawnIds != null
                                                ? new List<string>(pastEvent.involvedPawnIds)
                                                : new List<string>(),
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

        /// <summary>Resolve an involvement-tier id list to live colonist pawns, tagged with their role,
        /// de-duplicating pawns already added by an earlier (higher-priority) tier.</summary>
        private static void AddTargetTier(List<KeyValuePair<Pawn, string>> targets, List<string> ids, string role)
        {
            if (ids == null) return;
            foreach (var id in ids)
            {
                Pawn p = PawnById(id);
                if (p == null || !p.IsColonist || p.Dead) continue;
                if (targets.Any(t => t.Key == p)) continue;
                targets.Add(new KeyValuePair<Pawn, string>(p, role));
            }
        }

        private static Pawn PawnById(string thingId)
        {
            if (string.IsNullOrEmpty(thingId) || Find.Maps == null) return null;
            foreach (var m in Find.Maps)
            {
                var p = m.mapPawns?.AllPawns?.FirstOrDefault(x => x.ThingID == thingId);
                if (p != null) return p;
            }
            return null;
        }

        private static string PronounsFor(Pawn p)
        {
            if (p == null) return "they/them";
            switch (p.gender)
            {
                case Gender.Female: return "she/her";
                case Gender.Male: return "he/him";
                default: return "they/them";
            }
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
            if (RimSynapse.SynapseCoreProviders.IsResident(p)) return true;
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


