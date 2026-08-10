using System;
using System.Linq;
using Verse;
using LudeonTK;
using RimWorld;
using RimSynapse.Comps;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Managers;
using RimSynapse.Models;

namespace RimSynapse.Psychology.Utils
{
    public static class SynapseDebugActions
    {
        [DebugAction("RimSynapse", "Psychology: Dump State (Log)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpPsychologyState(Pawn p)
        {
            if (p == null) return;
            var comp = p.TryGetComp<SynapseCorePawnComp>();
            if (comp == null)
            {
                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] {p.Name} has no SynapseCorePawnComp.");
                return;
            }
            
            RimSynapse.SynapseLogger.Info("psychology", $"--- Psychology State for {p.Name} ---");
            RimSynapse.SynapseLogger.Info("psychology", $"Memories: {comp.memories.Count}");
            foreach (var m in comp.memories)
            {
                RimSynapse.SynapseLogger.Info("psychology", $"  - [{m.memoryType}] {m.summary} (w:{m.weight}) refs:{m.timesReferenced}");
            }
            
            var psychComp = p.TryGetComp<SynapsePawnComp>();
            if (psychComp != null)
            {
                RimSynapse.SynapseLogger.Info("psychology", $"Break Category: {psychComp.breakCategory}");
                RimSynapse.SynapseLogger.Info("psychology", $"Zealot: {psychComp.ideologyZealot}");
                RimSynapse.SynapseLogger.Info("psychology", $"Has Backstory Memory: {psychComp.hasBackstoryMemory}");
                if (psychComp.medicalProfile != null && psychComp.medicalProfile.Count > 0)
                {
                    RimSynapse.SynapseLogger.Info("psychology", "--- Clinical evaluation (medicalProfile) ---");
                    foreach (var kv in psychComp.medicalProfile)
                        RimSynapse.SynapseLogger.Info("psychology", $"  [{kv.Key}] {kv.Value}");
                }
            }
        }

        [DebugAction("RimSynapse", "Skill Engine: Dump signals (Log)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpSkillSignals(Pawn p)
        {
            if (p == null) return;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            if (core == null) { RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] {p.Name} has no SynapseCorePawnComp."); return; }

            core.GetActivityMetrics(out float idle, out float violence);
            float todayMood = p.needs?.mood?.CurLevelPercentage ?? 0.5f;
            float reinf = RimSynapse.Psychology.API.SynapseTraitEngine.PeekReinforcement(core, todayMood);
            float stress = RimSynapse.Psychology.API.SynapseTraitEngine.ComputeStress(p, todayMood);
            RimSynapse.SynapseLogger.Info("psychology",
                $"--- Skill signals for {p.LabelShort} --- mood={todayMood:P0} baseline={core.moodBaseline:0.00} reinforcement={reinf:+0.00;-0.00} stress={stress:0.00} idle={idle:P0} livingViolence={violence:P0}");
            if (p.skills?.skills != null)
                foreach (var rec in p.skills.skills.Where(r => r.passion != RimWorld.Passion.None || r.xpSinceMidnight > 0f))
                    RimSynapse.SynapseLogger.Info("psychology", $"  skill {rec.def.defName} L{rec.Level} passion={rec.passion} xpToday={rec.xpSinceMidnight:0}");

            var signals = SynapseSkillAxisMap.SampleSignals(p, core, reinf, stress);
            RimSynapse.SynapseLogger.Info("psychology", $"Derived {signals.Count} signal(s):");
            foreach (var s in signals)
                RimSynapse.SynapseLogger.Info("psychology", $"  -> {s.candidateId}  +{s.dailyPressure:0.00}  [{s.reason}]");

            RimSynapse.SynapseLogger.Info("psychology", $"Current pressures ({core.traitPressures.Count}):");
            foreach (var kv in core.traitPressures)
                RimSynapse.SynapseLogger.Info("psychology", $"  {kv.Key} = {kv.Value.pressure:0.00} (peak {kv.Value.peak:0.00})");

            bool confronts = RimSynapse.Psychology.API.SynapseTraitEngine.Confronts(p);
            RimSynapse.SynapseLogger.Info("psychology",
                $"Coping: {(confronts ? "CONFRONTS" : "AVOIDS")} | breakThrMinor={p.mindState?.mentalBreaker?.BreakThresholdMinor:0.00}");
            if (p.story?.traits?.allTraits != null)
                foreach (var t in p.story.traits.allTraits)
                    RimSynapse.SynapseLogger.Info("psychology", $"  trait def={t.def.defName} degree={t.Degree} label={t.Label}");
        }

        [DebugAction("RimSynapse", "Skill Engine: Simulate fixation day (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SimulateFixationDay(Pawn p)
        {
            if (p == null || p.skills == null) return;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            if (core == null) return;

            // Dominant, fulfilled passion: Plants (worked hard today). Starved passion: Intellectual (untouched).
            var plants = p.skills.GetSkill(SkillDefOf.Plants);
            var intel = p.skills.GetSkill(SkillDefOf.Intellectual);
            if (plants != null) { plants.passion = RimWorld.Passion.Major; plants.Learn(1600f, direct: true); }
            if (intel != null) { intel.passion = RimWorld.Passion.Major; }

            // Prime a normally-content baseline, then simulate a stressed day (low mood) so the multidimensional
            // crash-out actually fires: fulfilled-but-mood-fell burnout + stressed rejection of the neglected skill.
            core.moodBaseline = 0.6f;
            RimSynapse.Psychology.API.SynapseTraitEngine.ProcessRestEdge(p, core, 0.25f);
            RimSynapse.SynapseLogger.Info("psychology",
                $"[RimSynapse] Simulated a stressed fixation day for {p.LabelShort} (Plants fulfilled, Intellectual starved, mood 0.25). Pressures now:");
            foreach (var kv in core.traitPressures)
                RimSynapse.SynapseLogger.Info("psychology", $"  {kv.Key} = {kv.Value.pressure:0.00}");
        }

        [DebugAction("RimSynapse", "Skill Engine: Force research aversion (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceResearchAversion(Pawn p)
        {
            if (p == null) return;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            if (core == null) return;

            long now = Find.TickManager != null ? Find.TickManager.TicksAbs : 0L;
            // Walk one step along the intellectual aversion each call: none -> reluctant -> research-averse
            // -> (finally) research-broken (incapable). Pick the right candidate from the pawn's current degree.
            var distaste = DefDatabase<TraitDef>.GetNamedSilentFail("Synapse_Aversion_Intellectual");
            int maxDeg = (distaste?.degreeDatas != null && distaste.degreeDatas.Count > 0) ? distaste.degreeDatas.Max(d => d.degree) : 0;
            int cur = p.story?.traits?.DegreeOfTrait(distaste) ?? 0;
            string cand = cur < maxDeg
                ? RimSynapse.Models.TraitAxis.SpectrumCandidate("Synapse_Aversion_Intellectual", cur + 1)
                : RimSynapse.Models.TraitAxis.SingleCandidate("Synapse_Incapable_Intellectual", true);
            core.AccumulateTraitPressure(cand, 2f, "add", 0f, now, SynapseCorePawnComp.TraitPressureDecayPerDay);
            string fired = RimSynapse.Psychology.API.SynapseTraitEngine.ForceTopShift(p, core);

            var research = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Research");
            bool disabled = research != null && p.WorkTypeIsDisabled(research);
            int newDeg = p.story?.traits?.DegreeOfTrait(distaste) ?? 0;
            RimSynapse.SynapseLogger.Info("psychology",
                $"[RimSynapse] {p.LabelShort}: fired '{fired ?? "(none)"}'. Distaste degree {cur}->{newDeg}. Research disabled: {disabled}.");
        }

        // ── Testing accelerators: inject history + simulate long-term pressure without waiting days ──

        private static void InjectMem(SynapseCorePawnComp core, string summary, string type, float weight,
            long absTick, bool longTerm, params string[] tags)
        {
            var mem = new WeightedMemory
            {
                summary = summary, memoryType = type, weight = weight, baseWeight = weight,
                isLongTerm = longTerm, absTick = absTick, tags = tags?.ToList() ?? new System.Collections.Generic.List<string>()
            };
            core.AddMemory(mem);
        }

        [DebugAction("RimSynapse", "Memory: Inject backdated history (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void InjectBackdatedHistory(Pawn p)
        {
            if (p == null) return;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            if (core == null) return;
            long now = Find.TickManager.TicksAbs;
            const long D = 60000L;
            InjectMem(core, "Watched a raider gun down a close friend during the winter siege.", "EventReflection", 0.85f, now - 14 * D, true, "grief", "combat", "death");
            InjectMem(core, "Spent weeks hunched over the research bench, and it finally paid off.", "EventReflection", 0.5f, now - 11 * D, false, "work", "intellectual");
            InjectMem(core, "Butchered a manhunter pack and felt a grim, unsettling satisfaction.", "EventReflection", 0.6f, now - 8 * D, false, "combat", "violence");
            InjectMem(core, "Argued bitterly with a bunkmate over dwindling food rations.", "EventReflection", 0.45f, now - 5 * D, false, "social", "conflict");
            InjectMem(core, "Missed the harvest again; the fields have gone to weeds.", "EventReflection", 0.5f, now - 3 * D, false, "work", "plants", "neglect");
            InjectMem(core, "Sat alone through another sleepless, anxious night.", "EventReflection", 0.55f, now - 1 * D, false, "mood", "stress");
            RimSynapse.SynapseLogger.Info("psychology",
                $"[RimSynapse] Injected 6 backdated memories into {p.LabelShort}.\n{SynapseCoreDebug.DumpMemories(core)}");
        }

        [DebugAction("RimSynapse", "Skill Engine: Simulate 12 fixation days (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SimulateFixationArc(Pawn p)
        {
            if (p == null || p.skills == null) return;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            if (core == null) return;
            var plants = p.skills.GetSkill(SkillDefOf.Plants);
            var intel = p.skills.GetSkill(SkillDefOf.Intellectual);
            if (plants != null) plants.passion = RimWorld.Passion.Major;
            if (intel != null) intel.passion = RimWorld.Passion.Major;
            core.moodBaseline = 0.6f; // normally content, so a stressed day reads as a real drop

            for (int day = 1; day <= 12; day++)
            {
                if (plants != null) plants.xpSinceMidnight = 2000f; // fulfilled: worked the fields hard
                if (intel != null) intel.xpSinceMidnight = 0f;      // starved: never touched research
                RimSynapse.Psychology.API.SynapseTraitEngine.ProcessRestEdge(p, core, 0.28f); // stressed day
            }
            bool confronts = RimSynapse.Psychology.API.SynapseTraitEngine.Confronts(p);
            RimSynapse.SynapseLogger.Info("psychology",
                $"--- Fixation arc for {p.LabelShort} (12 stressed days) --- coping style: {(confronts ? "CONFRONTS (strike)" : "AVOIDS (withdraw)")} ---");
            foreach (var kv in core.traitPressures.OrderByDescending(k => k.Value.pressure))
                RimSynapse.SynapseLogger.Info("psychology", $"  {kv.Key} = {kv.Value.pressure:0.00} (peak {kv.Value.peak:0.00})");
            string style = RimSynapse.Psychology.API.SynapseTraitEngine.ForceCoping(p, core);
            RimSynapse.SynapseLogger.Info("psychology", $"  => coping fired: {style ?? "(nothing over threshold)"}");
            var psych = p.TryGetComp<SynapsePawnComp>();
            if (psych?.copingStates != null)
                foreach (var cs in psych.copingStates)
                    RimSynapse.SynapseLogger.Info("psychology", $"     active {cs.label}: {cs.traitDefName}{(cs.conditionSkillDefName != null ? " until " + cs.conditionSkillDefName : "")}");
        }

        [DebugAction("RimSynapse", "Skill Engine: Simulate 12 bloodlust days (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SimulateBloodlustArc(Pawn p)
        {
            if (p == null) return;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            if (core == null) return;
            core.moodBaseline = 0.5f;

            for (int day = 1; day <= 12; day++)
            {
                int now = Find.TickManager.TicksGame;
                core.recentJobs.Clear();
                core.recentJobs.Add(new JobInterval("AttackMelee", now - 50000, 50000, "humanlike"));
                core.lastJobStartedTick = -1; // no ongoing job to dilute the fraction
                // Mood ABOVE baseline: they feel good after the killing -> positive reinforcement -> Bloodlust.
                RimSynapse.Psychology.API.SynapseTraitEngine.ProcessRestEdge(p, core, 0.72f);
            }
            RimSynapse.SynapseLogger.Info("psychology", $"--- Bloodlust arc for {p.LabelShort} (12 violent, high-mood days) ---");
            foreach (var kv in core.traitPressures.OrderByDescending(k => k.Value.pressure))
                RimSynapse.SynapseLogger.Info("psychology", $"  {kv.Key} = {kv.Value.pressure:0.00} (peak {kv.Value.peak:0.00})");
            string fired = RimSynapse.Psychology.API.SynapseTraitEngine.ForceTopShift(p, core);
            RimSynapse.SynapseLogger.Info("psychology", $"  => forced shift: {fired ?? "(nothing over threshold)"}");
        }

        [DebugAction("RimSynapse", "Skill Engine: Force resolve now (respect veto/flavor)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceResolveNow(Pawn p)
        {
            var core = p?.TryGetComp<SynapseCorePawnComp>();
            if (core == null) return;
            string r = RimSynapse.Psychology.API.SynapseTraitEngine.ForceResolve(p, core);
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Force resolve for {p.LabelShort}: {r}");
        }

        [DebugAction("RimSynapse", "Skill Engine: Dump candidate prompt (Log)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpCandidatePrompt(Pawn p)
        {
            var core = p?.TryGetComp<SynapseCorePawnComp>();
            if (core == null) return;
            string block = RimSynapse.Psychology.API.SynapsePsychology.DescribeTraitCandidatesForPrompt(p, core);
            RimSynapse.SynapseLogger.Info("psychology", $"--- {{TRAIT_CANDIDATES}} the model receives for {p.LabelShort} ---\n{block}");
        }

        [DebugAction("RimSynapse", "Skill Engine: Dump judgments (Log)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpJudgments(Pawn p)
        {
            var psych = p?.TryGetComp<SynapsePawnComp>();
            if (psych?.traitJudgments == null || psych.traitJudgments.Count == 0)
            {
                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] {p?.LabelShort} has no stored LLM trait judgments.");
                return;
            }
            RimSynapse.SynapseLogger.Info("psychology", $"--- LLM judgments for {p.LabelShort} ---");
            foreach (var kv in psych.traitJudgments)
                RimSynapse.SynapseLogger.Info("psychology", $"  {kv.Key}: [{kv.Value.verdict}] {kv.Value.flavor}");
        }

        private static string TopCandidate(SynapseCorePawnComp core)
        {
            string top = null; float best = -1f;
            if (core?.traitPressures != null)
                foreach (var kv in core.traitPressures)
                    if (kv.Value.pressure > best) { best = kv.Value.pressure; top = kv.Key; }
            return top;
        }

        [DebugAction("RimSynapse", "Skill Engine: Inject LLM flavor on top candidate (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void InjectJudgmentFlavor(Pawn p)
        {
            var core = p?.TryGetComp<SynapseCorePawnComp>();
            var psych = p?.TryGetComp<SynapsePawnComp>();
            string top = TopCandidate(core);
            if (top == null || psych == null) { RimSynapse.SynapseLogger.Info("psychology", "[RimSynapse] No candidate to judge."); return; }
            psych.traitJudgments[top] = new RimSynapse.Psychology.Models.TraitJudgmentRecord
            {
                verdict = "in_character",
                flavor = $"Something in {p.LabelShort} has quietly given way; the overseer would do well to notice.",
                tick = Find.TickManager.TicksGame
            };
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Injected in-character flavor on '{top}' for {p.LabelShort}. Fire a shift to see it in the letter.");
        }

        [DebugAction("RimSynapse", "Skill Engine: Inject VETO on top candidate (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void InjectJudgmentVeto(Pawn p)
        {
            var core = p?.TryGetComp<SynapseCorePawnComp>();
            var psych = p?.TryGetComp<SynapsePawnComp>();
            string top = TopCandidate(core);
            if (top == null || psych == null) { RimSynapse.SynapseLogger.Info("psychology", "[RimSynapse] No candidate to judge."); return; }
            psych.traitJudgments[top] = new RimSynapse.Psychology.Models.TraitJudgmentRecord
            {
                verdict = "out_of_character", flavor = "", tick = Find.TickManager.TicksGame
            };
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Injected OUT-OF-CHARACTER veto on '{top}' for {p.LabelShort}. It should be pushed back at the rest edge.");
        }

        [DebugAction("RimSynapse", "Skill Engine: Lift coping now (serve/expire)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LiftCopingNow(Pawn p)
        {
            if (p == null) return;
            var psych = p.TryGetComp<SynapsePawnComp>();
            if (psych?.copingStates == null || psych.copingStates.Count == 0)
            {
                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] {p.LabelShort} has no active coping to lift.");
                return;
            }
            int before = psych.copingStates.Count;
            // Serve every demanded passion (mark it exercised) so condition-gated strikes lift.
            foreach (var cs in psych.copingStates)
            {
                if (string.IsNullOrEmpty(cs.conditionSkillDefName)) continue;
                var sd = DefDatabase<SkillDef>.GetNamedSilentFail(cs.conditionSkillDefName);
                var rec = sd != null ? p.skills?.GetSkill(sd) : null;
                if (rec != null) rec.xpSinceMidnight = SynapseSkillAxisMap.PracticeXp;
            }
            RimSynapse.Psychology.API.SynapseTraitEngine.LiftCoping(p, psych);
            RimSynapse.SynapseLogger.Info("psychology",
                $"[RimSynapse] {p.LabelShort}: served/expired coping. {before - psych.copingStates.Count} lifted, {psych.copingStates.Count} remain. Traits now: {string.Join(", ", p.story.traits.allTraits.Select(t => t.Label))}");
        }

        [DebugAction("RimSynapse", "Skill Engine: Reset trait state (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ResetTraitState(Pawn p)
        {
            if (p == null) return;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            if (core == null) return;
            core.traitPressures?.Clear();
            core.moodBaseline = -1f;
            core.skillLevelSnapshot?.Clear();
            core.skillXpSinceLevelSnapshot?.Clear();
            var psych = p.TryGetComp<SynapsePawnComp>();
            psych?.copingStates?.Clear();
            psych?.aversionRecurrence?.Clear();
            // Strip any engine-applied aversion / incapable / strike traits so a scenario can be re-run cleanly.
            var toClear = new System.Collections.Generic.List<string> { "Synapse_Strike_Hauling", "Synapse_Strike_Cleaning" };
            foreach (var d in SynapseSkillAxisMap.WorkDomains) { toClear.Add(d.aversionTraitDef); toClear.Add(d.incapableTraitDef); }
            foreach (var defName in toClear)
            {
                var def = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
                if (def != null && p.story?.traits != null && p.story.traits.HasTrait(def))
                    SynapsePsychology.ApplyTraitDirective(p, defName, false, "debug reset");
            }
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Reset trait-engine state for {p.LabelShort}.");
        }

        [DebugAction("RimSynapse", "Psychology: Dump voice (Log)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpVoice(Pawn p)
        {
            if (p == null) return;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            if (core == null) { RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] {p.Name} has no SynapseCorePawnComp."); return; }
            RimSynapse.SynapseLogger.Info("psychology",
                $"--- Voice for {p.LabelShort} ({p.gender}) --- kokoroVoice={core.kokoroVoice ?? "(none)"} speed={core.kokoroSpeed:F2} " +
                $"blend={(string.IsNullOrEmpty(core.kokoroBlendVoice) ? "none" : core.kokoroBlendVoice + "@" + core.kokoroBlendWeight.ToString("F2"))} " +
                $"generated={core.voiceGenerated}");
            RimSynapse.SynapseLogger.Info("psychology", $"  style: {core.voiceProfile ?? "(none)"}");
        }

        [DebugAction("RimSynapse", "Psychology: (Re)generate voice (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RegenerateVoice(Pawn p)
        {
            if (p == null) return;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            var psych = p.TryGetComp<SynapsePawnComp>();
            if (core == null || psych == null) { RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] {p.Name} missing comps for voice."); return; }
            core.voiceGenerated = false;      // allow re-derive
            core.kokoroVoice = null;          // reroll base voice too
            psych.DeriveVoiceProfile(p, core);
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Requested voice (re)generation for {p.LabelShort}.");
        }

        [DebugAction("RimSynapse", "Core: Dump event episodes (Log)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpEventEpisodes()
        {
            var wc = Find.World?.GetComponent<SynapseCoreWorldComponent>();
            if (wc == null) { RimSynapse.SynapseLogger.Info("core", "[RimSynapse] No core world component."); return; }
            RimSynapse.SynapseLogger.Info("core", $"--- Event backlog: {wc.BacklogCount} episode(s) ---");
            foreach (var e in wc.AllEvents)
            {
                RimSynapse.SynapseLogger.Info("core",
                    $"  [{e.severity}] {e.CoalesceKey} x{e.occurrenceCount} " +
                    $"(involved {e.involvedPawnIds.Count}, witness {e.witnessPawnIds.Count}, aftermath {e.afterEffectPawnIds.Count}) : \"{e.eventDescription}\"");
            }
        }

        /// <summary>Playtest sim (Core#88): flood the clicked pawn with 27 trivial claw wounds + one
        /// serious one from a crow, then dump — expect one coalesced episode (x27) + one distinct serious.</summary>
        [DebugAction("RimSynapse", "Core: Simulate injury burst (Tool)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SimulateInjuryBurst(Pawn p)
        {
            if (p == null) return;
            var wc = Find.World?.GetComponent<SynapseCoreWorldComponent>();
            if (wc == null) return;
            string tag = $"Injury({p.LabelShort} vs a crow)";
            for (int i = 0; i < 27; i++)
            {
                wc.EnqueuePastEvent(new RimSynapse.Models.PastEvent
                {
                    mcpTag = tag,
                    category = "ColonistInjured",
                    severity = RimSynapse.Models.EventSeverity.Trivial,
                    eventDescription = $"{p.LabelShort} suffered a scratch from a crow.",
                    involvedPawnIds = new System.Collections.Generic.List<string> { p.ThingID }
                });
            }
            wc.EnqueuePastEvent(new RimSynapse.Models.PastEvent
            {
                mcpTag = tag,
                category = "ColonistInjured",
                severity = RimSynapse.Models.EventSeverity.Serious,
                eventDescription = $"{p.LabelShort} took a deep gash to the leg from the crow.",
                involvedPawnIds = new System.Collections.Generic.List<string> { p.ThingID }
            });
            RimSynapse.SynapseLogger.Info("core", $"[RimSynapse] Simulated 27 trivial + 1 serious crow wound on {p.LabelShort}. Backlog now {wc.BacklogCount}.");
            DumpEventEpisodes();
        }

        [DebugAction("RimSynapse", "Psychology: Add Random Memory", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AddRandomMemory(Pawn p)
        {
            if (p == null) return;
            var memory = new WeightedMemory
            {
                memoryType = "Social",
                summary = $"Debug memory generated at tick {Find.TickManager.TicksGame}",
                weight = Rand.Range(0.1f, 0.9f),
                timesReferenced = 0
            };
            SynapsePsychology.AddMemory(p, memory);
        }
        
        [DebugAction("RimSynapse", "Psychology: Force Daily Review", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceDailyReview(Pawn p)
        {
            if (p == null) return;
            // Use the pawn's REAL current mood + recent memories (not a hardcoded 0.5 / empty list) so the
            // review actually reflects their situation — essential for testing trouble → eval → badges.
            float mood = p.needs?.mood?.CurLevelPercentage ?? 0.5f;
            long nowAbs = Find.TickManager.TicksAbs;
            var core = p.TryGetComp<SynapseCorePawnComp>();
            var recent = core?.memories?.Where(m => nowAbs - m.absTick < 60000).ToList()
                         ?? new System.Collections.Generic.List<WeightedMemory>();
            SynapsePsychology.QueueDailyPsychologyReview(p, mood, recent);
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Forced daily review for {p.Name} (mood {mood:P0}, {recent.Count} recent memories)");
        }

        /// <summary>Testing aid: put a colonist in genuine distress — real injuries (pain → mood drop, which
        /// drives the live Mood badge) plus a grief/trauma memory for the evaluation to reason about.</summary>
        [DebugAction("RimSynapse", "Psychology: Trouble pawn (injuries + grief)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TroublePawn(Pawn p)
        {
            if (p == null) return;
            for (int i = 0; i < 6; i++)
                p.TakeDamage(new DamageInfo(DamageDefOf.Cut, 10f, 0f, -1f, null, null, null));

            SynapsePsychology.AddMemory(p, new WeightedMemory
            {
                summary = $"{p.LabelShort} barely survived a savage mauling and watched a friend bleed out in the mud.",
                weight = 0.9f, baseWeight = 0.9f, memoryType = "EventReflection",
                tags = new System.Collections.Generic.List<string> { "Death", "Grief", "Trauma" },
                absTick = Find.TickManager.TicksAbs, isLongTerm = false
            });
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Troubled {p.LabelShort}: 6 cuts + grief memory. Mood now {(p.needs?.mood?.CurLevelPercentage ?? 0f):P0}.");
        }

        private static readonly string[] BleakDayLines =
        {
            "Another pointless day; nothing they build ever lasts, and they said so to anyone who'd listen.",
            "They watched supplies rot and muttered the colony is doomed no matter what anyone does.",
            "A fresh grave, worse weather, more loss — they've stopped expecting anything to ever go right.",
            "They snapped that hope is a lie people tell themselves; everything decays, everyone leaves.",
            "They stared at the wall for hours, certain the next raid will be the one that finishes them all.",
        };

        /// <summary>Testing aid: apply one day of bleak, hopeless experience (a despair-tagged memory, no
        /// injuries) — repeat across several forced reviews to build sustained pressure toward Pessimist.</summary>
        [DebugAction("RimSynapse", "Psychology: Apply bleak day (pessimism evidence)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ApplyBleakDay(Pawn p)
        {
            if (p == null) return;
            SynapsePsychology.AddMemory(p, new WeightedMemory
            {
                summary = BleakDayLines[Rand.Range(0, BleakDayLines.Length)],
                weight = 0.85f, baseWeight = 0.85f, memoryType = "EventReflection",
                tags = new System.Collections.Generic.List<string> { "Despair", "Hopelessness", "Grief", "Pessimism" },
                absTick = Find.TickManager.TicksAbs, isLongTerm = false
            });
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Applied a bleak day to {p.LabelShort} (despair memory).");
        }

        [DebugAction("RimSynapse", "Psychology: Force Break Sweep (all colonists)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceBreakSweep()
        {
            var mgr = Current.Game?.GetComponent<SynapseBreakManager>();
            if (mgr == null)
            {
                RimSynapse.SynapseLogger.Info("psychology", "[RimSynapse] SynapseBreakManager not found (no active game?).");
                return;
            }
            int n = mgr.ForceEvaluateAll();
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Forced break sweep evaluated {n} colonist(s).");
        }

        [DebugAction("RimSynapse", "Psychology: Trigger Euphoria", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TriggerEuphoria(Pawn p)
        {
            if (p == null || p.mindState == null) return;
            var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("Synapse_EuphoricReckless");
            if (stateDef != null)
            {
                p.mindState.mentalStateHandler.TryStartMentalState(stateDef, "Debug command", true);
            }
        }
        
        [DebugAction("RimSynapse", "Psychology: Trigger Suicidal (Starve)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TriggerSuicidalStarve(Pawn p)
        {
            if (p == null || p.mindState == null) return;
            var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("Synapse_SuicidalStarve");
            if (stateDef != null)
            {
                p.mindState.mentalStateHandler.TryStartMentalState(stateDef, "Debug command", true);
            }
        }
        
        [DebugAction("RimSynapse", "Psychology: Trigger Suicidal (Burn)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TriggerSuicidalBurn(Pawn p)
        {
            if (p == null || p.mindState == null) return;
            var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("Synapse_SuicidalBurn");
            if (stateDef != null)
            {
                p.mindState.mentalStateHandler.TryStartMentalState(stateDef, "Debug command", true);
            }
        }
        
        [DebugAction("RimSynapse", "Psychology: Trigger Suicidal (Antagonize)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TriggerSuicidalAntagonize(Pawn p)
        {
            if (p == null || p.mindState == null) return;
            var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("Synapse_SuicidalAntagonize");
            if (stateDef != null)
            {
                p.mindState.mentalStateHandler.TryStartMentalState(stateDef, "Debug command", true);
            }
        }
    }
}


