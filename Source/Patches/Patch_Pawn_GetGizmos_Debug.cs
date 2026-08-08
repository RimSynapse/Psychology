using System.Collections.Generic;
using HarmonyLib;
using Verse;
using RimWorld;
using RimSynapse.Comps;
using RimSynapse.Psychology.API;

namespace RimSynapse.Psychology.Patches
{
    /// <summary>
    /// DevMode-only debug gizmos on colonists (Core#81 / Psychology#52). They call the same shared
    /// statics as the registered game tools, so both surfaces stay in lockstep. Hidden unless
    /// Prefs.DevMode is on, so they never clutter normal play.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_Debug
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;

            if (!Prefs.DevMode) yield break;
            Pawn pawn = __instance;
            if (pawn == null || !pawn.IsColonist) yield break;
            if (pawn.TryGetComp<SynapseCorePawnComp>() == null) yield break;

            yield return new Command_Action
            {
                defaultLabel = "Synapse: Dump Memories",
                defaultDesc = "DevMode: show this colonist's memories with weight, salience and long-term tier.",
                icon = TexCommand.Attack,
                action = () => Find.WindowStack.Add(new Dialog_MessageBox(
                    SynapseCoreDebug.DumpMemories(pawn), title: $"Memories — {pawn.LabelShort}"))
            };

            yield return new Command_Action
            {
                defaultLabel = "Synapse: Run Maintenance",
                defaultDesc = "DevMode: force the daily memory decay + salience/consolidation pass now.",
                icon = TexCommand.Attack,
                action = () => Messages.Message(SynapseCoreDebug.RunMaintenance(pawn), MessageTypeDefOf.TaskCompletion, false)
            };

            yield return new Command_Action
            {
                defaultLabel = "Synapse: Gen Memory (LLM)",
                defaultDesc = "DevMode: generate a memory via the live LLM and show the response.",
                icon = TexCommand.Attack,
                action = () => SynapsePsychologyDebug.GenerateMemory(pawn, null)
            };

            yield return new Command_Action
            {
                defaultLabel = "Synapse: Run Eval (LLM)",
                defaultDesc = "DevMode: run the daily psychology evaluation via the live LLM and show the result.",
                icon = TexCommand.Attack,
                action = () => SynapsePsychologyDebug.RunEvaluation(pawn)
            };
        }
    }
}
