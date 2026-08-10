using System;
using System.Linq;
using System.Collections.Generic;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;
using RimSynapse.Models;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Nightly clinical evaluation: Queues the pawn's daily mood, memories, and statistics
    /// for LLM processing to update their long-term psychological profile.
    /// </summary>
    public static partial class SynapsePsychology
    {
        /// <summary>
        /// Triggered when the pawn goes to sleep. Queues their daily events and average mood
        /// for LLM processing to update long-term context modifiers and break severity.
        /// </summary>
        /// <summary>
        /// Stage 0 (design §5.2): select only "today's" events — memories within the last day —
        /// from whatever the caller passed. A window of one in-game day (60,000 ticks) matches the
        /// filters the UI and backstory callers already applied; centralising it here means a caller
        /// that hands over the full memory bank no longer floods the evaluator with lifetime history.
        /// </summary>
        public static System.Collections.Generic.List<RimSynapse.Models.WeightedMemory> SelectTodaysEvents(
            System.Collections.Generic.List<RimSynapse.Models.WeightedMemory> events, long nowAbsTick, int windowTicks = 60000)
        {
            if (events == null) return new System.Collections.Generic.List<RimSynapse.Models.WeightedMemory>();
            return events.Where(m => m != null && (nowAbsTick - m.absTick) < windowTicks).ToList();
        }

        /// <summary>
        /// Stage 0 (design §5.1/§6): state lifetime violence against LIVING creatures explicitly so the
        /// evaluator cannot read object-bashing as bloodlust. Mechanoid kills are deliberately excluded —
        /// they are not living.
        /// </summary>
        public static string DescribeLifetimeViolence(int humanlikeKills, int animalKills)
        {
            int living = humanlikeKills + animalKills;
            return $"Lifetime violence against the living: {humanlikeKills} humanlike, {animalKills} animal ({living} living kills total)";
        }

        /// <summary>Human-readable accumulated trait trajectory for the eval prompt (Stage 2).</summary>
        public static string DescribeTraitPressures(RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            if (coreComp == null || coreComp.traitPressures == null || coreComp.traitPressures.Count == 0) return "None";
            var parts = new List<string>();
            foreach (var kvp in coreComp.traitPressures)
            {
                var tp = kvp.Value;
                parts.Add($"{kvp.Key}: {tp.pressure:0.00} toward {tp.direction ?? "?"} (peak {tp.peak:0.00})");
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Who the psychology system spends LLM calls on (#39): people the colony has a relationship with —
        /// colonists, prisoners and slaves of the colony, and quest lodgers who actually stay. NOT enemy
        /// pawns or passing traders/visitors. Cheap enough to run per-humanlike in TickRare.
        /// </summary>
        public static bool IsEligibleForReview(Pawn pawn)
        {
            if (pawn == null || !pawn.RaceProps.Humanlike || pawn.Dead) return false;
            return pawn.IsColonist || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony || pawn.IsQuestLodger();
        }

        public static void QueueDailyPsychologyReview(Pawn pawn, float averageMood, System.Collections.Generic.List<RimSynapse.Models.WeightedMemory> dailyEvents, Action<bool> onComplete = null, bool isOpportunistic = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Chokepoint: never spend a clinical-review request on someone the colony has no relationship
            // with (a raider, a passing trader). Covers every caller (nightly, opportunistic, debug).
            if (!IsEligibleForReview(pawn)) { onComplete?.Invoke(false); return; }

            var pawnComp = pawn.TryGetComp<SynapsePawnComp>();
            var coreComp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (pawnComp == null || coreComp == null) return;

            string systemPrompt = @"You are a clinical psychologist in the RimWorld universe writing a formal medical evaluation.
Based on this colonist's average mood today, their recent memories, their survival skills, and the state of the colony, assess the following 9 categories (write 1-2 sentences each), and then provide a final Summary:
- Mood (Emotional baseline, volatility, and general disposition)
- Interpersonal (Attachment style, sociability, relationships with others)
- Trauma (Lingering psychological effects of past horrors, deaths, or recent brutal raids)
- Cognitive (Paranoia, grandiosity, irrational fears, or hyper-fixations)
- Motivations (What keeps them going: duty, survival, greed, ideology?)
- Identity (How they view themselves and their role within the colony)
- Morality (Capacity for cruelty versus compassion: e.g., handling prisoners, organ harvesting)
- Authority (Rebelliousness versus obedience to colony leadership and drafted orders)
- Addiction (Psychological reliance on substances or specific comforts)
- Summary (The final overarching clinical assessment and prognosis)

You MUST analyze the 'Tags' attached to their recent memories. If you see recurring themes (e.g. 'Food', 'Starving', 'Safety'), you must explicitly address this growing concern in your evaluation.

You must also provide an 'AbandonmentRiskScore' (0-100) representing how likely they are to permanently abandon the colony (or rebel if a slave). High survival skills and low satisfaction increase this risk.

Finally, output a 'SocialAdjustments' object reflecting changes in their Trust (-100 to +100) and Familiarity (0 to 100) with other colonists based on recent events. Use the colonist's short name as the key. Trust offsets should be between -15 and +15. Familiarity offsets should be positive (0 to +10).

Additionally, assess whether recent experiences are building toward a personality change. Do NOT apply changes directly — personality shifts accumulate over MULTIPLE days and a single day rarely fires one. Instead return:
- 'PersonalityShiftLikelihood': one of ""none"" | ""low"" | ""moderate"" | ""high"" — your overall judgement of whether ANY shift is warranted right now. If ""none"" or ""low"", today contributes no pressure toward any change.
- 'PersonalityShiftAssessment': an array of per-candidate-trait evidence for TODAY ONLY. Each entry: { ""trait"": <RimWorld trait defName>, ""direction"": ""add"" | ""remove"", ""dailyPressure"": 0.0-1.0 (how strong TODAY's evidence is), ""rationale"": <one sentence> }. Leave the array empty if nothing is building.
IMPORTANT — violence vs. the living: traits that reflect a taste for killing or cruelty (e.g. 'Bloodlust') require evidence of harming LIVING creatures (humanlikes or animals). Destroying inanimate objects, mining, or repeatedly attacking wrecked machinery is NOT violence against the living and must NEVER, on its own, justify adding 'Bloodlust' or removing protective/steadfast traits. If today's activity was primarily against objects, set dailyPressure near 0 and likelihood ""none""/""low"".
IMPORTANT — trait resistance: respect the resistance values below. Protected traits must NOT be removed without overwhelming, sustained evidence.
The colonist currently has these AI-added traits (added by you previously): {DYNAMIC_TRAITS}. Their current traits with resistance: {TRAIT_RESISTANCE}. Accumulated trait pressure so far (trajectory across days): {TRAIT_PRESSURES}.

You MUST respond strictly in valid JSON format. Do not include markdown formatting or extra text.
{
  ""Mood"": ""1-2 sentences..."",
  ""Interpersonal"": ""1-2 sentences..."",
  ""Trauma"": ""1-2 sentences..."",
  ""Cognitive"": ""1-2 sentences..."",
  ""Motivations"": ""1-2 sentences..."",
  ""Identity"": ""1-2 sentences..."",
  ""Morality"": ""1-2 sentences..."",
  ""Authority"": ""1-2 sentences..."",
  ""Addiction"": ""1-2 sentences..."",
  ""Summary"": ""1-2 sentences..."",
  ""AbandonmentRiskScore"": 0,
  ""PersonalityShiftLikelihood"": ""none"",
  ""PersonalityShiftAssessment"": [
    { ""trait"": ""Bloodlust"", ""direction"": ""add"", ""dailyPressure"": 0.15, ""rationale"": ""Repeated combat, but only against an inert vehicle - weak signal."" }
  ],
  ""SocialAdjustments"": {
    ""ColonistName1"": {
      ""trustOffset"": 2.5,
      ""familiarityOffset"": 1.0
    }
  }
}";

            string dlcInstructions = "";
            if (ModsConfig.RoyaltyActive)
            {
                dlcInstructions += "\n- Royalty DLC: If the colonist recently gained a title or cast psycasts, evaluate if this develops a royal entitlement complex, class friction, or mental fatigue.";
            }
            if (ModsConfig.IdeologyActive)
            {
                dlcInstructions += "\n- Ideology DLC: If the colonist recently witnessed a ritual or conversion, evaluate if this triggers a crisis of faith or deepens their fanaticism/zealotry.";
            }
            if (ModsConfig.BiotechActive)
            {
                dlcInstructions += "\n- Biotech DLC: If the colonist gave birth, was vat-grown, or received gene enhancements, reflect their physical/genetic identity dysphoria or parentage complexes.";
            }
            if (ModsConfig.AnomalyActive)
            {
                dlcInstructions += "\n- Anomaly DLC: If the colonist has void/entity exposure tags in their memories, let it warp their cognitive profile towards void obsession or paranoia.";
            }
            if (pawn.Map != null && pawn.Map.Biome != null && pawn.Map.Biome.defName.IndexOf("Space", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                dlcInstructions += "\n- Save Our Ship 2: The colonist is currently in orbit/space. Reflect the psychological impact of cosmic isolation and void melancholy.";
            }

            if (!string.IsNullOrEmpty(dlcInstructions))
            {
                systemPrompt = systemPrompt.Replace("You MUST analyze the 'Tags' attached to their recent memories.",
                    "You MUST analyze the 'Tags' attached to their recent memories." + dlcInstructions);
            }

            // Stage 0 (design §5.2): the evaluator must be able to tell today from a lifetime. Only
            // memories from the last day count as "today"; long-standing burdens are surfaced separately
            // below via GetTopMemoryBurdens. Filtering here fixes every caller at once — including the
            // opportunistic profile eval, which passes the entire memory bank.
            var todaysEvents = SelectTodaysEvents(dailyEvents, Find.TickManager.TicksAbs);
            string recentEvents = todaysEvents.Count == 0
                ? "No significant memories today."
                : string.Join("\n", todaysEvents.Select(e => $"- {e.summary} [Tags: {string.Join(", ", e.tags)}]"));

            // Deduped activity summary with target kinds (Core #72) — object-bashing reads as non-lethal.
            string todaysActivity = coreComp.GetRecentJobsSummary();

            float threshold = RimSynapsePsychologyMod.Settings != null ? RimSynapsePsychologyMod.Settings.sensitivityThreshold : 0.5f;
            string lifetimeBurdens = coreComp.GetTopMemoryBurdens(5, threshold);

            int colonySize = pawn.Map?.mapPawns?.FreeColonistsCount ?? 1;
            int melee = pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            int shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            int cooking = pawn.skills?.GetSkill(SkillDefOf.Cooking)?.Level ?? 0;
            int medicine = pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            
            int lifetimeKills = pawn.records?.GetAsInt(RecordDefOf.KillsHumanlikes) ?? 0;
            int lifetimeAnimalKills = pawn.records?.GetAsInt(RecordDefOf.KillsAnimals) ?? 0;
            string lifetimeViolence = DescribeLifetimeViolence(lifetimeKills, lifetimeAnimalKills);
            float damageTaken = pawn.records?.GetValue(RecordDefOf.DamageTaken) ?? 0f;
            float timeAsColonist = (pawn.records?.GetValue(RecordDefOf.TimeAsColonistOrColonyAnimal) ?? 0f) / 60000f; // in days

            // Explicit classification — "Guest" is reached by an actual lodger check, never as a
            // catch-all for "everything else" (#39). Ineligible pawns are gated out before this point.
            string statusText = pawn.IsColonist ? "Colonist"
                : (pawn.IsPrisoner ? "Prisoner"
                : (pawn.IsSlave ? "Slave"
                : (pawn.IsQuestLodger() ? "Guest" : "Colony affiliate")));
            NeedDef suppressionDef = DefDatabase<NeedDef>.GetNamedSilentFail("Suppression");
            string suppression = (pawn.IsSlave && suppressionDef != null) ? $"\nSuppression: {pawn.needs?.TryGetNeed(suppressionDef)?.CurLevelPercentage:P0}" : "";

            // Initialize missing colonists in social network
            if (pawn.Map != null)
            {
                foreach (Pawn p in pawn.Map.mapPawns.FreeColonists)
                {
                    if (p != pawn && !pawnComp.socialNetwork.ContainsKey(p.GetUniqueLoadID()))
                    {
                        pawnComp.socialNetwork[p.GetUniqueLoadID()] = new RimSynapse.Psychology.Models.SocialRecord();
                    }
                }
            }

            string socialNetworkStr = "None";
            if (pawnComp.socialNetwork.Count > 0)
            {
                var allPawns = (pawn.Map?.mapPawns?.AllPawnsSpawned ?? Enumerable.Empty<Pawn>()).Concat(Find.WorldPawns.AllPawnsAliveOrDead);
                var socialLines = new List<string>();
                foreach (var kvp in pawnComp.socialNetwork)
                {
                    Pawn target = allPawns.FirstOrDefault(p => p.GetUniqueLoadID() == kvp.Key);
                    if (target != null)
                    {
                        int affinity = target.relations != null ? pawn.relations.OpinionOf(target) : 0;
                        socialLines.Add($"- {target.Name.ToStringShort} ({target.gender}): Trust {kvp.Value.trust:F0}, Familiarity {kvp.Value.familiarity:F0}, Affinity {affinity}");
                    }
                }
                if (socialLines.Count > 0)
                {
                    socialNetworkStr = string.Join("\n", socialLines);
                }
            }

            string dynamicTraitsStr = "None";
            if (pawnComp.dynamicTraits.Count > 0)
            {
                dynamicTraitsStr = string.Join(", ", pawnComp.dynamicTraits.Select(t => $"{t.traitDef.defName} (Added {((Find.TickManager.TicksGame - t.tickAdded)/60000f):F1} days ago for: {t.reason})"));
            }
            systemPrompt = systemPrompt.Replace("{DYNAMIC_TRAITS}", dynamicTraitsStr);

            // Stage 2: feed the trait-resistance dimension and the accumulated cross-day trajectory.
            systemPrompt = systemPrompt.Replace("{TRAIT_RESISTANCE}", SynapseTraitPolicy.DescribeResistanceForPrompt(pawn));
            systemPrompt = systemPrompt.Replace("{TRAIT_PRESSURES}", DescribeTraitPressures(coreComp));

            string userMessage = $@"Patient Name: {pawn.Name.ToStringShort}
Gender: {pawn.gender}
Status: {statusText}
Colony Size: {colonySize}
Time as Colonist: {timeAsColonist:F1} days
Survival Stats: Melee {melee}, Shooting {shooting}, Medicine {medicine}, Damage Taken: {damageTaken:F0}
{lifetimeViolence}
Average Mood Today: {averageMood:F2}{suppression}
Long-standing psychological burdens (weighted): {lifetimeBurdens}
Current Social Network (Trust, Familiarity, Affinity):
{socialNetworkStr}
Today's Activity (deduped, with target kinds): {todaysActivity}
Today's Events:
{recentEvents}";

            var options = new ChatOptions { priority = isOpportunistic ? -1 : 0, requestName = "Daily Psychology Review", targetName = pawn.Name.ToStringShort };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => ParseEvaluationResult(result, pawn, pawnComp, onComplete, sw),
                options
            );
        }
    }
}

