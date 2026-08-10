using HarmonyLib;
using Verse;
using Verse.AI;
using RimWorld;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.MentalStates;

namespace RimSynapse.Psychology.Patches
{
    [HarmonyPatch(typeof(MentalState), "MentalStateTick")]
    public static class Patch_MentalState_MentalStateTick
    {
        public static void Postfix(MentalState __instance)
        {
            if (__instance.pawn == null) return;
            
            // Check dynamically every 150 ticks to avoid performance hits
            if (!__instance.pawn.IsHashIntervalTick(150)) return;

            var comp = __instance.pawn.GetComp<SynapsePawnComp>();
            if (comp == null || comp.breakIntensity != BreakIntensity.Light) return;

            bool isDepressive = __instance.pawn.story?.traits?.HasTrait(PsychologyDefCache.Depressive) == true ||
                                __instance.pawn.story?.traits?.HasTrait(PsychologyDefCache.Pessimist) == true;
            bool isBloodlust = __instance.pawn.story?.traits?.HasTrait(PsychologyDefCache.Bloodlust) == true ||
                               __instance.pawn.story?.traits?.HasTrait(PsychologyDefCache.Psychopath) == true;

            // Handle Light intensity abort conditions
            if (__instance.def.defName == "Synapse_SuicidalBurn")
            {
                if (__instance.pawn.HasAttachment(ThingDefOf.Fire) || __instance.pawn.AmbientTemperature > 50f)
                {
                    __instance.RecoverFromState();
                }
                
                float painThreshold = isDepressive ? 0.85f : 0.60f;
                float snapChance = isDepressive ? 0.10f : 0.50f;

                // NEW: Severe pain snaps them out of it
                if (__instance.pawn.health.hediffSet.PainTotal > painThreshold)
                {
                    if (Rand.Chance(snapChance))
                    {
                        Verse.Messages.Message($"{__instance.pawn.Name} has regained the will to live from the excruciating pain!", __instance.pawn, RimWorld.MessageTypeDefOf.PositiveEvent);
                        __instance.RecoverFromState();
                    }
                }
            }
            else if (__instance.def.defName == "Synapse_SuicidalAntagonize")
            {
                float painThreshold = (isDepressive || isBloodlust) ? 0.85f : 0.60f;
                float snapChance = (isDepressive || isBloodlust) ? 0.10f : 0.50f;

                // NEW: Severe pain snaps them out of it
                if (__instance.pawn.health.summaryHealth.SummaryHealthPercent < (1f - painThreshold) || __instance.pawn.health.hediffSet.PainTotal > painThreshold)
                {
                    if (Rand.Chance(snapChance))
                    {
                        Verse.Messages.Message($"{__instance.pawn.Name} has regained the will to live from the excruciating pain!", __instance.pawn, RimWorld.MessageTypeDefOf.PositiveEvent);
                        __instance.RecoverFromState();
                    }
                }
            }
            else if (__instance.def.defName == "Synapse_SuicidalStarve")
            {
                float malThreshold = isDepressive ? 0.95f : 0.85f;
                float snapChance = isDepressive ? 0.05f : 0.40f;

                // NEW: Extreme malnutrition snaps them out of it
                var malnutrition = __instance.pawn.health.hediffSet.GetFirstHediffOfDef(RimWorld.HediffDefOf.Malnutrition);
                if (malnutrition != null && malnutrition.Severity > malThreshold)
                {
                    if (Rand.Chance(snapChance))
                    {
                        Verse.Messages.Message($"{__instance.pawn.Name}'s extreme hunger has finally overridden their depression. They are desperately seeking food!", __instance.pawn, RimWorld.MessageTypeDefOf.PositiveEvent);
                        __instance.RecoverFromState();
                    }
                }
            }
        }
    }
}
