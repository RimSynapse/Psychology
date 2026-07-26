using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Models;

namespace RimSynapse.Psychology.Patches
{
    [HarmonyPatch(typeof(LordJob_Ritual), "ApplyOutcome")]
    public static class Patch_LordJob_Ritual_ApplyOutcome
    {
        [HarmonyPostfix]
        public static void Postfix(LordJob_Ritual __instance, float progress, Precept_Ritual ___ritual)
        {
            if (!ModsConfig.IdeologyActive) return;
            if (__instance == null) return;

            string ritualLabel = __instance.RitualLabel;
            string outcomeLabel = "completed";

            try
            {
                if (___ritual != null && ___ritual.outcomeEffect != null && ___ritual.outcomeEffect.def != null)
                {
                    var outcome = ___ritual.outcomeEffect.def.outcomeChances
                        .OrderBy(o => Math.Abs(o.positivityIndex - progress))
                        .FirstOrDefault();
                    if (outcome != null)
                    {
                        outcomeLabel = outcome.label;
                    }
                }
            }
            catch {}

            var coreComp = Find.World?.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
            if (coreComp != null)
            {
                coreComp.EnqueuePastEvent(new PastEvent
                {
                    gameTick = Find.TickManager.TicksGame,
                    eventDescription = $"The ritual '{ritualLabel}' was concluded with the outcome: '{outcomeLabel}'.",
                    pawnSnapshots = new System.Collections.Generic.Dictionary<string, string>(),
                    category = "Ideology"
                });
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_IdeoTracker), "SetIdeo")]
    public static class Patch_Pawn_IdeoTracker_SetIdeo
    {
        public static void Postfix(Pawn_IdeoTracker __instance, Ideo ideo)
        {
            if (!ModsConfig.IdeologyActive) return;
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager.TicksGame <= 5) return;
            Pawn pawn = HarmonyLib.Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null || !pawn.IsColonist) return;

            var coreComp = Find.World?.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
            if (coreComp != null)
            {
                coreComp.EnqueuePastEvent(new PastEvent
                {
                    gameTick = Find.TickManager.TicksGame,
                    eventDescription = $"{pawn.Name.ToStringShort} experienced a crisis of faith and converted to the ideology '{ideo?.name ?? "Unknown"}'.",
                    pawnSnapshots = new System.Collections.Generic.Dictionary<string, string>(),
                    category = "Ideology"
                });
            }
        }
    }
}
