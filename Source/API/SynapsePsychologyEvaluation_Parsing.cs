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
    /// Response parsing and callback handlers for clinical evaluation results.
    /// Handles social adjustments, abandonment risk, trait changes, and therapy summaries.
    /// </summary>
    public static partial class SynapsePsychology
    {
        private static void ParseEvaluationResult(ChatResult result, Pawn pawn, SynapsePawnComp pawnComp, Action<bool> onComplete, System.Diagnostics.Stopwatch sw)
        {
            if (result.success)
            {
                try
                {
                    string json = result.content.Trim();
                    if (json.StartsWith("```json")) json = json.Substring(7);
                    if (json.StartsWith("```")) json = json.Substring(3);
                    if (json.EndsWith("```")) json = json.Substring(0, json.Length - 3);
                    json = json.Trim();

                    var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (parsed != null)
                    {
                        // Extract the overall likelihood first — it gates the per-trait assessment,
                        // and dictionary iteration order is not guaranteed.
                        string shiftLikelihood = parsed.TryGetValue("PersonalityShiftLikelihood", out var likeObj)
                            ? likeObj?.ToString() : null;

                        foreach (var kvp in parsed)
                        {
                            if (kvp.Key == "SocialAdjustments" && kvp.Value is Newtonsoft.Json.Linq.JObject socialObj)
                            {
                                ApplySocialAdjustments(pawn, pawnComp, socialObj);
                            }
                            else if (kvp.Key == "AbandonmentRiskScore")
                            {
                                ApplyAbandonmentRisk(pawn, Convert.ToInt32(kvp.Value));
                            }
                            else if (kvp.Key == "PersonalityShiftAssessment" && kvp.Value is Newtonsoft.Json.Linq.JArray assessmentArr)
                            {
                                ApplyPersonalityShiftAssessment(pawn, pawnComp, shiftLikelihood, assessmentArr);
                            }
                            else if (kvp.Key == "TraitChanges")
                            {
                                // Legacy schema — the engine no longer applies instant flips; ignore, keep for record.
                                pawnComp.medicalProfile[kvp.Key] = kvp.Value?.ToString() ?? "";
                            }
                            else
                            {
                                pawnComp.medicalProfile[kvp.Key] = kvp.Value.ToString();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to parse JSON clinical assessment for {pawn.Name.ToStringShort}: {ex.Message}\nContent: {result.content}");
                }
                
                onComplete?.Invoke(true);
            }
            else
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to generate clinical assessment for {pawn.Name.ToStringShort}: {result.error}");
                onComplete?.Invoke(false);
            }
            sw.Stop();
        }

        private static void ApplySocialAdjustments(Pawn pawn, SynapsePawnComp pawnComp, Newtonsoft.Json.Linq.JObject socialObj)
        {
            var allPawns = (pawn.Map?.mapPawns?.AllPawnsSpawned ?? Enumerable.Empty<Pawn>()).Concat(Find.WorldPawns.AllPawnsAliveOrDead);
            foreach (var property in socialObj.Properties())
            {
                string targetName = property.Name;
                var offsets = property.Value as Newtonsoft.Json.Linq.JObject;
                if (offsets != null)
                {
                    Pawn targetPawn = allPawns.FirstOrDefault(p => p.Name != null && p.Name.ToStringShort.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                    if (targetPawn != null)
                    {
                        string loadId = targetPawn.GetUniqueLoadID();
                        if (!pawnComp.socialNetwork.ContainsKey(loadId))
                        {
                            pawnComp.socialNetwork[loadId] = new RimSynapse.Psychology.Models.SocialRecord();
                        }
                        
                        float trustOff = (float?)offsets["trustOffset"] ?? 0f;
                        float famOff = (float?)offsets["familiarityOffset"] ?? 0f;

                        // Guardrail (#46): clamp each offset to the prompted per-eval range BEFORE applying,
                        // so a single over-eager response can't swing a relationship past the per-day cap
                        // (the cumulative -100..100 / 0..100 clamp still applies on top).
                        trustOff = UnityEngine.Mathf.Clamp(trustOff, -15f, 15f);
                        famOff = UnityEngine.Mathf.Clamp(famOff, 0f, 10f);

                        pawnComp.socialNetwork[loadId].trust = UnityEngine.Mathf.Clamp(pawnComp.socialNetwork[loadId].trust + trustOff, -100f, 100f);
                        pawnComp.socialNetwork[loadId].familiarity = UnityEngine.Mathf.Clamp(pawnComp.socialNetwork[loadId].familiarity + famOff, 0f, 100f);
                    }
                }
            }
        }

        private static void ApplyAbandonmentRisk(Pawn pawn, int riskScore)
        {
            if (riskScore > 90 && pawn.IsColonist && !pawn.Downed && !pawn.InMentalState)
            {
                SynapseGameComponent.Enqueue(() =>
                {
                    var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("Synapse_MentalState_AbandonColony");
                    if (stateDef != null)
                    {
                        pawn.mindState.mentalStateHandler.TryStartMentalState(stateDef, "Psychological evaluation", true);
                    }
                });
            }
        }

        // Stage 2 tuning — Stage 3 surfaces these as settings.
        private const float ShiftThreshold = 1.0f;
        private const int MaxTraitChangesPerEval = 1;
        private const long TraitChangeCooldownTicks = 3 * 60000; // 3 in-game days between actual changes

        /// <summary>
        /// Fold today's per-trait evidence into the multi-day pressure accumulator (design §5.7). Changes
        /// fire only when sustained pressure crosses the (resistance-raised) threshold, subject to the
        /// consistency gate, a whitelist, a per-eval cap, and a cooldown. Never an instant one-day flip.
        /// </summary>
        private static void ApplyPersonalityShiftAssessment(Pawn pawn, SynapsePawnComp pawnComp, string likelihood,
            Newtonsoft.Json.Linq.JArray assessment)
        {
            var coreComp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (coreComp == null || assessment == null) return;

            long now = Find.TickManager?.TicksAbs ?? 0L;
            int changesThisEval = 0;
            var trajectory = new List<string>();

            foreach (var token in assessment)
            {
                if (!(token is Newtonsoft.Json.Linq.JObject obj)) continue;

                string trait = (string)obj["trait"];
                if (string.IsNullOrEmpty(trait)) continue;
                string direction = ((string)obj["direction"] ?? "add").ToLowerInvariant();
                float dailyPressure = (float?)obj["dailyPressure"] ?? 0f;
                string rationale = (string)obj["rationale"] ?? "";

                // Consistency gate: an "unlikely"/none overall likelihood floors today's evidence to 0.
                dailyPressure = SynapseTraitPolicy.GateDailyPressure(likelihood, dailyPressure);
                dailyPressure = UnityEngine.Mathf.Clamp(dailyPressure, 0f, 1f);

                float resistance = SynapseTraitPolicy.ResistanceFactor(trait);
                float pressure = coreComp.AccumulateTraitPressure(trait, dailyPressure, direction, resistance, now);

                // Protected traits require overwhelming, sustained pressure before removal.
                float threshold = ShiftThreshold;
                if (direction == "remove" && SynapseTraitPolicy.IsProtected(trait)) threshold *= 2f;

                trajectory.Add($"{trait} {pressure:0.00}/{threshold:0.0} → {direction}");

                bool cooldownOk = pawnComp.lastTraitChangeTick < 0 || (now - pawnComp.lastTraitChangeTick) >= TraitChangeCooldownTicks;
                if (pressure < threshold || changesThisEval >= MaxTraitChangesPerEval || !cooldownOk) continue;

                // Whitelist guardrail: the AI may only add/remove sanctioned traits (never e.g. Psychopath).
                if (!SynapseTraitPolicy.IsWhitelisted(trait))
                {
                    RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Blocked non-whitelisted trait change '{trait}' for {pawn.Name?.ToStringShort}.");
                    continue;
                }

                bool add = direction == "add";
                string reason = $"Sustained multi-day pressure ({pressure:0.00}) crossed the threshold. {rationale}".Trim();
                SynapseGameComponent.Enqueue(() => SynapsePsychology.ApplyTraitDirective(pawn, trait, add, reason));
                coreComp.ResetTraitPressure(trait);
                pawnComp.lastTraitChangeTick = now;
                changesThisEval++;
            }

            pawnComp.medicalProfile["PersonalityTrajectory"] = trajectory.Count > 0 ? string.Join("; ", trajectory) : "stable";
        }

        public static void SummarizeTherapySession(Pawn initiator, Pawn target, List<string> chatLog)
        {
            if (chatLog == null || chatLog.Count == 0) return;

            string fullTranscript = string.Join("\n", chatLog);
            string systemPrompt = @"You are a clinical psychologist summarizing a therapy session between two colonists.
Analyze the transcript and extract the key psychological insights, breakthroughs, or recurring themes.
Return a brief, profound 2-3 sentence summary that will be stored permanently as context for future interactions between these two pawns.
Do not include markdown or formatting, just the plain text summary.";

            string userMessage = $@"Initiator (Counselor): {initiator.NameShortColored}
Target (Patient): {target.NameShortColored}

Transcript:
{fullTranscript}";

            var options = new ChatOptions { priority = -1, requestName = "Therapy Summary", targetName = $"{initiator.NameShortColored} -> {target.NameShortColored}" };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => 
                {
                    if (result.success)
                    {
                        string summary = result.content.Trim();
                    }
                    else
                    {
                        RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to summarize therapy session: {result.error}");
                    }
                },
                options
            );
        }
    }
}
