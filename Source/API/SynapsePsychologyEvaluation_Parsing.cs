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

                        // Each evaluation is a fresh snapshot — clear prior keys so retired categories
                        // (the old 9-section schema) don't linger in medicalProfile alongside the new 5.
                        pawnComp.medicalProfile?.Clear();

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
                            else if (kvp.Key == "TraitJudgment" && kvp.Value is Newtonsoft.Json.Linq.JArray judgmentArr)
                            {
                                ApplyTraitJudgment(pawn, pawnComp, judgmentArr);
                            }
                            else if (kvp.Key == "Voice" && kvp.Value is Newtonsoft.Json.Linq.JObject voiceObj)
                            {
                                // Daily voice tracking: the review re-derives the pawn's speaking voice from
                                // today's psychological picture (continuity-guarded in the prompt), so trait
                                // shifts, breaks and mood arcs surface in how they talk.
                                var voiceCore = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
                                if (voiceCore != null
                                    && Comps.VoiceProfileBuilder.TryReadVoice(voiceObj, out string vStyle, out string vPace, out string vTimbre, out string vSample))
                                {
                                    Comps.VoiceProfileBuilder.ApplyVoiceProfile(pawn, voiceCore, vStyle, vPace, vTimbre, vSample);
                                }
                            }
                            else if (kvp.Key == "PersonalityShiftAssessment" || kvp.Key == "TraitChanges")
                            {
                                // Legacy schemas — the measured engine drives changes now; ignore, keep for record.
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
            float threshold = RimSynapsePsychologyMod.Settings?.abandonmentThreshold ?? 90f;
            if (riskScore > threshold && pawn.IsColonist && !pawn.Downed && !pawn.InMentalState)
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
        /// Phase 2: store the LLM's per-candidate JUDGEMENT (in/out of character) and NARRATION (flavour) for
        /// the changes the MEASURED engine is building. The LLM no longer accumulates pressure or fires
        /// changes itself — that would double-write the store the engine drives. It only advises: the engine
        /// consults these at fire time to narrate (flavour) and, if enabled, push back on out-of-character
        /// changes. Candidate ids not currently under pressure are rejected (guards hallucinated ids, #60).
        /// </summary>
        private static void ApplyTraitJudgment(Pawn pawn, SynapsePawnComp pawnComp, Newtonsoft.Json.Linq.JArray judgments)
        {
            if (pawnComp == null || judgments == null) return;
            if (pawnComp.traitJudgments == null)
                pawnComp.traitJudgments = new System.Collections.Generic.Dictionary<string, RimSynapse.Psychology.Models.TraitJudgmentRecord>();
            var coreComp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            int now = Find.TickManager?.TicksGame ?? 0;
            var summary = new List<string>();

            foreach (var token in judgments)
            {
                if (!(token is Newtonsoft.Json.Linq.JObject obj)) continue;
                string candidateId = (string)obj["candidateId"];
                if (string.IsNullOrEmpty(candidateId)) continue;
                if (coreComp?.traitPressures != null && !coreComp.traitPressures.ContainsKey(candidateId))
                    continue; // reject ids the engine isn't actually building
                string verdict = ((string)obj["verdict"] ?? "uncertain").Trim().ToLowerInvariant();
                string flavor = (string)obj["flavor"] ?? "";
                pawnComp.traitJudgments[candidateId] = new RimSynapse.Psychology.Models.TraitJudgmentRecord
                {
                    verdict = verdict, flavor = flavor, tick = now
                };
                summary.Add($"{candidateId}: {verdict}");
            }

            // Drop judgements for candidates whose pressure has since decayed away.
            if (coreComp?.traitPressures != null)
            {
                var stale = new List<string>();
                foreach (var k in pawnComp.traitJudgments.Keys)
                    if (!coreComp.traitPressures.ContainsKey(k)) stale.Add(k);
                foreach (var k in stale) pawnComp.traitJudgments.Remove(k);
            }

            pawnComp.medicalProfile["PersonalityTrajectory"] = summary.Count > 0 ? string.Join("; ", summary) : "stable";
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
                        if (!string.IsNullOrEmpty(summary))
                        {
                            // Persist on the main thread: a durable long-term memory on BOTH pawns,
                            // linked to the other by id, so it feeds future interactions and shows in
                            // the long-term memory tier (#51 — it used to be computed then discarded).
                            SynapseGameComponent.Enqueue(() =>
                            {
                                long now = Find.TickManager?.TicksAbs ?? 0L;
                                SynapsePsychology.AddMemory(initiator, BuildTherapyMemory(target, summary, now));
                                SynapsePsychology.AddMemory(target, BuildTherapyMemory(initiator, summary, now));
                                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Stored therapy summary for {initiator.Name?.ToStringShort} & {target.Name?.ToStringShort}.");
                            });
                        }
                    }
                    else
                    {
                        RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to summarize therapy session: {result.error}");
                    }
                },
                options
            );
        }

        /// <summary>
        /// Build the durable therapy-summary memory an owner keeps about the other participant.
        /// Long-term (never decays) and linked by the other's id so relational queries find it.
        /// </summary>
        public static WeightedMemory BuildTherapyMemory(Pawn other, string summary, long nowTick)
        {
            return new WeightedMemory
            {
                summary = $"Therapy session with {other?.Name?.ToStringShort ?? "someone"}: {summary}",
                weight = 0.8f,
                baseWeight = 0.8f,
                isLongTerm = true,
                tags = new List<string> { "Therapy", "Counseling" },
                subjectPawnIds = other != null ? new List<string> { other.GetUniqueLoadID() } : new List<string>(),
                memoryType = "Therapy",
                absTick = nowTick
            };
        }
    }
}
