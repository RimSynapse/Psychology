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
            }
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
            SynapsePsychology.QueueDailyPsychologyReview(p, 0.5f, new System.Collections.Generic.List<WeightedMemory>());
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Forced daily psychology review for {p.Name}");
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


