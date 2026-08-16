using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Comps;
using RimSynapse.Models;
using RimSynapse.Psychology;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.Settings;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// The skill-driven trait engine's deterministic core: run once per day at the rest edge. It folds the
    /// day's measured skill signals into Core's trait-pressure store WITHOUT the LLM likelihood gate (so a
    /// shift is reachable from behaviour alone — the fix for #60), then runs the escalation ladder:
    ///   • below threshold → a growing "unease" mood modifier (common, reversible, what the night report warns about);
    ///   • at/over threshold → a small daily RNG roll (settings-controlled, default 0.5%, 0 disables) fires a
    ///     permanent trait shift toward the highest-pressure candidate, applying its real gameplay effect.
    /// Phase 2 layers the LLM judge/flavour on top; this loop stands alone and is model-independent.
    /// </summary>
    public static class SynapseTraitEngine
    {
        private const float UneaseFloor = 0.30f;         // fraction of threshold below which no unease shows
        private const int ShiftCooldownTicks = 180000;   // 3 days between permanent shifts on one pawn

        private static RimSynapsePsychologySettings Settings => RimSynapsePsychologyMod.Settings;

        /// <summary>Fold today's signals into pressure and resolve the escalation ladder for one pawn.
        /// <paramref name="todayMood"/> is the day's average mood [0,1], the basis for the reinforcement and
        /// stress dimensions that make every trait signal multidimensional.</summary>
        public static void ProcessRestEdge(Pawn pawn, SynapseCorePawnComp core, float todayMood)
        {
            if (pawn == null || core == null) return;

            // Lift any active break/strike whose timer expired or whose demand was met (the passion got
            // exercised today) BEFORE sampling, so a served demand ends the strike.
            LiftCoping(pawn, pawn.TryGetComp<SynapsePawnComp>());

            long nowAbs = Find.TickManager != null ? Find.TickManager.TicksAbs : 0L;
            float decay = SynapseCorePawnComp.TraitPressureDecayPerDay;

            float reinforcement = core.UpdateMoodBaselineAndGetReinforcement(todayMood);
            float stress = ComputeStress(pawn, todayMood);
            var signals = SynapseSkillAxisMap.SampleSignals(pawn, core, reinforcement, stress);
            float mult = Settings?.traitDriftMultiplier ?? 1f; // master "how fast personalities drift" knob
            foreach (var sig in signals)
            {
                string axisId = TraitAxis.AxisIdOf(sig.candidateId);
                float resistance = SynapseTraitPolicy.ResistanceFactor(axisId);
                // Ungated on purpose: measured evidence drives accumulation, not the model's confidence.
                core.AccumulateTraitPressure(sig.candidateId, sig.dailyPressure * mult, sig.direction, resistance, nowAbs, decay);
            }

            ResolveEscalation(pawn, core);
        }

        private static int HardenAfterBreaks => Settings?.hardenAfterBreaks ?? 3; // repeated breaks before permanent

        private static void ResolveEscalation(Pawn pawn, SynapseCorePawnComp core)
        {
            float threshold = Settings?.shiftThreshold ?? 1.0f;
            if (threshold <= 0f) threshold = 1.0f;

            var over = core.traitPressures.Where(kv => kv.Value.pressure >= threshold).ToList();
            if (over.Count == 0)
            {
                float topFrac = 0f;
                foreach (var kv in core.traitPressures) topFrac = System.Math.Max(topFrac, kv.Value.pressure / threshold);
                if (topFrac >= UneaseFloor) ApplyUnease(pawn, topFrac);
                return;
            }

            var aversions = over.Where(kv => IsAversionCandidate(kv.Key)).ToList();
            if (aversions.Count > 0)
            {
                // A work-stressor crash-out resolves as temporary COPING (strike/break) first, not a shift.
                float chance = Settings?.copingChancePerDay ?? 0.15f;
                if (chance > 0f && !RecentlyShifted(pawn) && Rand.Value < chance)
                    ApplyCoping(pawn, core, aversions);
                else
                    ApplyUnease(pawn, 1f);
                return;
            }

            // Non-aversion outcome (mood / bloodlust / wealth) — a rare PERMANENT shift.
            var top = over.OrderByDescending(kv => kv.Value.pressure).First();
            if (IsVetoed(pawn, top.Key)) { DampenAndUnease(pawn, core, top.Key); return; }
            float pchance = Settings?.traitShiftChancePerDay ?? 0.005f;
            if (pchance > 0f && !RecentlyShifted(pawn) && Rand.Value < pchance)
                FireShift(pawn, core, top.Key, top.Value);
            else
                ApplyUnease(pawn, 1f);
        }

        private static bool IsAversionCandidate(string candidateId)
        {
            string axisId = TraitAxis.AxisIdOf(candidateId);
            return axisId != null && (axisId.StartsWith("Synapse_Aversion_") || axisId.StartsWith("Synapse_Incapable_"));
        }

        /// <summary>
        /// Coping style from personality: CONFRONTERS (nerves of steel/steadfast, iron-willed, psychopath,
        /// tough, optimist) face the problem; AVOIDERS (nervous/volatile, depressive/pessimist) run from it.
        /// Ties resolve to facing. Drives strike-vs-withdraw.
        /// </summary>
        public static bool Confronts(Pawn pawn)
        {
            var traits = pawn?.story?.traits;
            if (traits == null) return true;
            float score = 0f;
            // Nerves is the spine of it: "iron-willed"/"nerves of steel" are Nerves degree +1/+2 (confront),
            // "nervous"/"volatile" are -1/-2 (run). NaturalMood adds outlook (optimist +, depressive -).
            var nerves = DefDatabase<TraitDef>.GetNamedSilentFail("Nerves");
            if (nerves != null && traits.HasTrait(nerves)) score += traits.DegreeOfTrait(nerves);
            var mood = DefDatabase<TraitDef>.GetNamedSilentFail("NaturalMood");
            if (mood != null && traits.HasTrait(mood)) score += traits.DegreeOfTrait(mood) * 0.5f;
            if (HasNamedTrait(traits, "Psychopath")) score += 1f;   // detached — confronts without emotional cost
            if (HasNamedTrait(traits, "Tough")) score += 0.5f;
            if (HasNamedTrait(traits, "Bloodlust")) score += 0.5f;
            return score >= 0f;
        }

        private static bool HasNamedTrait(TraitSet traits, string defName)
        {
            var def = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            return def != null && traits.HasTrait(def);
        }

        /// <summary>
        /// Resolve a work-stressor crash-out into a temporary coping response by personality:
        /// a CONFRONTER strikes against the drudge work to protect time for the starved passion (sheds the
        /// LOWER stressor); an AVOIDER withdraws from the highest-pressure work (sheds the HIGHER stressor).
        /// </summary>
        private static void ApplyCoping(Pawn pawn, SynapseCorePawnComp core, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, TraitPressure>> aversions)
        {
            var comp = pawn.TryGetComp<SynapsePawnComp>();
            if (comp == null) return;

            if (Confronts(pawn))
            {
                var starved = SynapseSkillAxisMap.MostStarvedStrongPassion(pawn);
                if (starved != null)
                {
                    Strike(pawn, comp, starved);
                    foreach (var kv in aversions) core.ResetTraitPressure(kv.Key); // striking relieves the pressure
                    return;
                }
                // No clear passion to strike for — fall through and withdraw instead.
            }

            var top = aversions.OrderByDescending(kv => kv.Value.pressure).First();
            if (IsVetoed(pawn, top.Key)) { DampenAndUnease(pawn, core, top.Key); return; }
            Withdraw(pawn, core, comp, top.Key, top.Value);
        }

        private static void Strike(Pawn pawn, SynapsePawnComp comp, RimWorld.SkillRecord passion)
        {
            int now = Find.TickManager.TicksGame;
            int expiry = now + ((Settings?.aversionBreakDays ?? 2) + 1) * 60000; // strikes outlast a plain break a touch
            string cond = passion.def.defName;
            string reason = $"{pawn.LabelShort} refuses to haul or clean until they get to work on {passion.def.label} again.";
            bool a = RegisterTempTrait(pawn, comp, "Synapse_Strike_Hauling", expiry, cond, "strike", reason);
            bool b = RegisterTempTrait(pawn, comp, "Synapse_Strike_Cleaning", expiry, cond, "strike", reason);
            if (a || b)
                RimSynapse.SynapseLogger.Message($"[RimSynapse] {pawn.LabelShort} is STRIKING (refusing drudge work) until they get to do {passion.def.label}.", "performance");
        }

        private static void Withdraw(Pawn pawn, SynapseCorePawnComp core, SynapsePawnComp comp, string candidateId, TraitPressure tp)
        {
            if (!TraitAxis.TryParse(candidateId, out string axisId, out _, out bool? singleAdd)) return;
            string domainKey = SynapseSkillAxisMap.DomainKeyForAxis(axisId);
            comp.aversionRecurrence.TryGetValue(domainKey, out int count);
            count++;
            comp.aversionRecurrence[domainKey] = count;
            bool permanent = count >= HardenAfterBreaks || (axisId != null && axisId.StartsWith("Synapse_Incapable_"));

            string templated = permanent
                ? $"After retreating from it again and again, {pawn.LabelShort}'s reluctance has set in for good."
                : $"{pawn.LabelShort} is taking a couple of days away from this work.";
            string reason = LlmReason(pawn, candidateId, templated); // LLM narration if the review pre-wrote it

            string applied = ApplyAxis(pawn, core, candidateId, reason);
            if (applied == null) return;

            comp.copingStates.RemoveAll(c => c.traitDefName == applied); // never stack duplicates for one trait
            if (!permanent)
            {
                int now = Find.TickManager.TicksGame;
                comp.copingStates.Add(new RimSynapse.Psychology.Models.CopingState
                {
                    traitDefName = applied,
                    expiryTick = now + (Settings?.aversionBreakDays ?? 2) * 60000,
                    conditionSkillDefName = null,
                    label = "break"
                });
            }
            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse] {pawn.LabelShort} WITHDREW from {applied} ({(permanent ? "PERMANENT — hardened" : "temporary break")}, recurrence {count}) :: {reason}", "performance");
        }

        private static bool RegisterTempTrait(Pawn pawn, SynapsePawnComp comp, string traitDefName, int expiryTick,
            string conditionSkill, string kind, string reason)
        {
            if (comp.copingStates.Any(c => c.traitDefName == traitDefName)) return false; // already active
            SynapsePsychology.ApplyTraitDirective(pawn, traitDefName, true, reason);
            comp.copingStates.Add(new RimSynapse.Psychology.Models.CopingState
            {
                traitDefName = traitDefName,
                expiryTick = expiryTick,
                conditionSkillDefName = conditionSkill,
                label = kind
            });
            return true;
        }

        /// <summary>Lift any coping whose timer expired or whose demanded passion was exercised today.</summary>
        public static void LiftCoping(Pawn pawn, SynapsePawnComp comp)
        {
            if (comp?.copingStates == null || comp.copingStates.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            var lifted = new System.Collections.Generic.List<RimSynapse.Psychology.Models.CopingState>();
            foreach (var cs in comp.copingStates)
            {
                bool lift = now >= cs.expiryTick;
                if (!lift && !string.IsNullOrEmpty(cs.conditionSkillDefName))
                {
                    var sd = DefDatabase<SkillDef>.GetNamedSilentFail(cs.conditionSkillDefName);
                    var rec = sd != null ? pawn.skills?.GetSkill(sd) : null;
                    if (rec != null && rec.xpSinceMidnight >= SynapseSkillAxisMap.PracticeXp) lift = true; // demand met
                }
                if (lift) lifted.Add(cs);
            }
            foreach (var cs in lifted)
            {
                SynapsePsychology.ApplyTraitDirective(pawn, cs.traitDefName, false, "coping lifted");
                comp.copingStates.Remove(cs);
            }
        }

        /// <summary>
        /// Debug/test hook: resolve the top over-threshold candidate NOW (ignoring the daily roll) but
        /// RESPECTING the LLM veto and narration — so the Phase 2 judge/narrate path can be exercised.
        /// </summary>
        public static string ForceResolve(Pawn pawn, SynapseCorePawnComp core)
        {
            if (pawn == null || core == null) return null;
            float threshold = Settings?.shiftThreshold ?? 1.0f;
            if (threshold <= 0f) threshold = 1f;
            var over = core.traitPressures.Where(kv => kv.Value.pressure >= threshold).ToList();
            if (over.Count == 0) return "nothing over threshold";

            var aversions = over.Where(kv => IsAversionCandidate(kv.Key)).ToList();
            if (aversions.Count > 0)
            {
                var t = aversions.OrderByDescending(kv => kv.Value.pressure).First();
                if (IsVetoed(pawn, t.Key)) { DampenAndUnease(pawn, core, t.Key); return "VETOED " + t.Key; }
                ApplyCoping(pawn, core, aversions);
                return "coping";
            }
            var top = over.OrderByDescending(kv => kv.Value.pressure).First();
            if (IsVetoed(pawn, top.Key)) { DampenAndUnease(pawn, core, top.Key); return "VETOED " + top.Key; }
            FireShift(pawn, core, top.Key, top.Value);
            return "shift " + top.Key;
        }

        /// <summary>Debug/test hook: force the crash-out coping now (ignoring the daily roll). Returns the style used.</summary>
        public static string ForceCoping(Pawn pawn, SynapseCorePawnComp core)
        {
            if (pawn == null || core == null) return null;
            float threshold = Settings?.shiftThreshold ?? 1.0f;
            var aversions = core.traitPressures.Where(kv => kv.Value.pressure >= threshold && IsAversionCandidate(kv.Key)).ToList();
            if (aversions.Count == 0) return null;
            ApplyCoping(pawn, core, aversions);
            return Confronts(pawn) && SynapseSkillAxisMap.MostStarvedStrongPassion(pawn) != null ? "strike" : "withdraw";
        }

        /// <summary>
        /// Debug/test hook: fire a shift toward the current highest-pressure candidate that has crossed
        /// threshold, ignoring the daily RNG roll and cooldown. Returns the fired candidate id, or null if
        /// nothing is over threshold. Used by the debug actions and deterministic TestRunner cases.
        /// </summary>
        public static string ForceTopShift(Pawn pawn, SynapseCorePawnComp core)
        {
            if (pawn == null || core == null) return null;
            float threshold = Settings?.shiftThreshold ?? 1.0f;
            string topId = null; TraitPressure topTp = null;
            foreach (var kvp in core.traitPressures)
                if (topTp == null || kvp.Value.pressure > topTp.pressure) { topId = kvp.Key; topTp = kvp.Value; }
            if (topTp == null || topTp.pressure < threshold) return null;
            FireShift(pawn, core, topId, topTp);
            return topId;
        }

        /// <summary>How close the pawn is to a mental break, in [0,1] — the strain dimension for the
        /// crash-out signals. Rises as mood falls toward (and below) the minor-break threshold.</summary>
        public static float ComputeStress(Pawn pawn, float todayMood)
        {
            // Pure function of todayMood vs the pawn's (stable) minor-break threshold, so it is deterministic
            // and controllable in simulation. Deliberately does NOT read instantaneous state like
            // BreakMinorIsImminent — that would couple stress to the pawn's real-time mood instead of the
            // day's average being evaluated (and made the fixation/bloodlust sims read the live mood).
            var mb = pawn?.mindState?.mentalBreaker;
            float thr = mb != null ? mb.BreakThresholdMinor : 0.35f;
            float span = thr + 0.15f;
            float s = span > 0f ? (span - todayMood) / span : 0f;
            if (s < 0f) s = 0f; else if (s > 1f) s = 1f;
            return s;
        }

        /// <summary>Non-mutating read of today's reinforcement vs the stored baseline (for debug/inspection).</summary>
        public static float PeekReinforcement(SynapseCorePawnComp core, float todayMood, float scale = 0.15f)
        {
            if (core == null || core.moodBaseline < 0f) return 0f;
            float d = (todayMood - core.moodBaseline) / (scale <= 0f ? 0.15f : scale);
            return d > 1f ? 1f : (d < -1f ? -1f : d);
        }

        /// <summary>
        /// The LLM's narration for a fired change: the per-candidate flavour if it wrote one, else the
        /// review's Headline (weak models reliably fill the narrative sections but often leave the
        /// TraitJudgment.flavor field empty — LCD fallback), else the templated line.
        /// </summary>
        private static string LlmReason(Pawn pawn, string candidateId, string fallback)
        {
            var comp = pawn.TryGetComp<SynapsePawnComp>();
            if (comp?.traitJudgments != null && comp.traitJudgments.TryGetValue(candidateId, out var j)
                && !string.IsNullOrWhiteSpace(j.flavor))
                return j.flavor;
            if (comp?.medicalProfile != null && comp.medicalProfile.TryGetValue("Headline", out var headline)
                && !string.IsNullOrWhiteSpace(headline))
                return headline;
            return fallback;
        }

        /// <summary>True if the LLM judged this candidate out-of-character and the feedback loop is on.</summary>
        private static bool IsVetoed(Pawn pawn, string candidateId)
        {
            if (Settings != null && !Settings.traitFeedbackEnabled) return false;
            var comp = pawn.TryGetComp<SynapsePawnComp>();
            return comp?.traitJudgments != null && comp.traitJudgments.TryGetValue(candidateId, out var j)
                && j.verdict == "out_of_character";
        }

        /// <summary>The feedback loop: the LLM found a building change out of character — ease its pressure back.</summary>
        private static void DampenAndUnease(Pawn pawn, SynapseCorePawnComp core, string candidateId)
        {
            if (core.traitPressures.TryGetValue(candidateId, out var tp)) tp.pressure *= 0.5f;
            ApplyUnease(pawn, 1f);
            RimSynapse.SynapseLogger.Message(
                $"[RimSynapse] {pawn.LabelShort}: '{candidateId}' judged out-of-character by review — pressure eased back.", "performance");
        }

        private static bool RecentlyShifted(Pawn pawn)
        {
            var comp = pawn.TryGetComp<SynapsePawnComp>();
            if (comp?.dynamicTraits == null || comp.dynamicTraits.Count == 0) return false;
            int now = Find.TickManager.TicksGame;
            return comp.dynamicTraits.Any(d => now - d.tickAdded < ShiftCooldownTicks);
        }

        /// <summary>Apply/refresh the growing-unease mood modifier at a stage scaled to how close to a shift the pawn is.</summary>
        private static void ApplyUnease(Pawn pawn, float fraction)
        {
            var thoughts = pawn?.needs?.mood?.thoughts?.memories;
            if (thoughts == null) return;
            var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("Synapse_Unease");
            if (def == null) return;
            int lastStage = (def.stages?.Count ?? 1) - 1;
            int stage = fraction >= 0.90f ? 2 : fraction >= 0.66f ? 1 : 0;
            if (stage > lastStage) stage = lastStage;
            var mem = (Thought_Memory)ThoughtMaker.MakeThought(def, stage);
            thoughts.TryGainMemory(mem);
        }

        /// <summary>
        /// Fire a permanent trait shift toward a candidate: apply the real trait (whose real gameplay effect
        /// follows), clear its pressure, and — for a spectrum — clear the opposing pole so it does not
        /// immediately re-trip. Phase 1 uses a templated reason; Phase 2 supplies LLM flavour.
        /// </summary>
        /// <summary>
        /// Apply a candidate's real trait effect and clear its pressure. Handles a spectrum step, a single
        /// add/remove, and the incapable-replaces-distaste swap. Returns the applied axis defName, or null.
        /// Shared by the permanent shift path (<see cref="FireShift"/>) and the temporary withdrawal path.
        /// </summary>
        private static string ApplyAxis(Pawn pawn, SynapseCorePawnComp core, string candidateId, string reason)
        {
            if (!TraitAxis.TryParse(candidateId, out string axisId, out int targetDegree, out bool? singleAdd)) return null;
            var def = DefDatabase<TraitDef>.GetNamedSilentFail(axisId);
            if (def == null) { core.ResetTraitPressure(candidateId); return null; }

            if (singleAdd.HasValue)
            {
                SynapsePsychology.ApplyTraitDirective(pawn, axisId, singleAdd.Value, reason);
                // A terminal incapable trait supersedes the graduated distaste it grew out of.
                if (singleAdd.Value)
                {
                    var dom = SynapseSkillAxisMap.DomainByIncapable(axisId);
                    if (dom?.aversionTraitDef != null)
                        SynapsePsychology.ApplyTraitDirective(pawn, dom.aversionTraitDef, false, reason);
                }
            }
            else
                SynapsePsychology.ApplyTraitStep(pawn, def, targetDegree, reason);

            core.ResetTraitPressure(candidateId);
            if (!singleAdd.HasValue)
                foreach (var key in core.traitPressures.Keys.Where(k => TraitAxis.AxisIdOf(k) == axisId).ToList())
                    core.ResetTraitPressure(key);
            return axisId;
        }

        private static void FireShift(Pawn pawn, SynapseCorePawnComp core, string candidateId, TraitPressure tp)
        {
            TraitAxis.TryParse(candidateId, out string axisId, out int targetDegree, out bool? singleAdd);
            var def = DefDatabase<TraitDef>.GetNamedSilentFail(axisId);
            string templated = def != null ? BuildTemplatedReason(pawn, def, targetDegree, singleAdd) : "";
            string reason = LlmReason(pawn, candidateId, templated); // LLM narration if the review pre-wrote it
            string applied = ApplyAxis(pawn, core, candidateId, reason);
            if (applied != null)
                RimSynapse.SynapseLogger.Message(
                    $"[RimSynapse] Trait shift fired for {pawn.LabelShort}: {candidateId} (pressure {tp.pressure:0.00}) :: {reason}", "performance");
        }

        private static string BuildTemplatedReason(Pawn pawn, TraitDef def, int targetDegree, bool? singleAdd)
        {
            string label = singleAdd.HasValue
                ? (def.degreeDatas != null && def.degreeDatas.Count > 0 ? def.degreeDatas[0].GetLabelFor(pawn) : def.label)
                : TraitAxis.LabelForDegree(def, targetDegree, pawn);
            return $"Weeks of how {pawn.LabelShort} has spent their days has slowly reshaped who they are, settling into '{label}'.";
        }
    }
}
