using HarmonyLib;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;
using System.Linq;
using System.Collections.Generic;

namespace RimSynapse.Psychology.Patches
{
    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_Verb_LaunchProjectile_TryCastShot
    {
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (!__result || __instance.CasterPawn == null || !__instance.CasterPawn.RaceProps.Humanlike) return;

            Pawn caster = __instance.CasterPawn;
            if (caster.story == null || caster.story.traits == null) return;

            var ptsdTrait = caster.story.traits.GetTrait(DefDatabase<TraitDef>.GetNamedSilentFail("Synapse_PTSD"));
            if (ptsdTrait == null) return;

            if (caster.Map == null) return;

            float searchRadius = 12f;
            Pawn supervisor = (Pawn)GenClosest.ClosestThingReachable(
                caster.Position, 
                caster.Map, 
                ThingRequest.ForGroup(ThingRequestGroup.Pawn), 
                Verse.AI.PathEndMode.OnCell, 
                TraverseParms.For(caster), 
                searchRadius, 
                t => {
                    if (t is Pawn other && other != caster && other.RaceProps.Humanlike && !other.Dead && !other.Downed && other.Awake())
                    {
                        if (other.Faction == caster.Faction && !other.InMentalState)
                        {
                            int socialLevel = other.skills != null ? other.skills.GetSkill(SkillDefOf.Social).Level : 0;
                            int shootingLevel = other.skills != null ? other.skills.GetSkill(SkillDefOf.Shooting).Level : 0;
                            return socialLevel >= 8 || shootingLevel >= 8;
                        }
                    }
                    return false;
                }
            );

            if (supervisor != null)
            {
                // 0.5% chance to desensitize PTSD on each shot under supervision
                if (Rand.Chance(0.005f))
                {
                    caster.story.traits.allTraits.Remove(ptsdTrait);
                    
                    Messages.Message($"{caster.NameShortColored} has desensitized their combat trauma (PTSD) through supervised shooting practice with {supervisor.NameShortColored}!", caster, MessageTypeDefOf.PositiveEvent);
                    
                    var coreComp = caster.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
                    if (coreComp != null)
                    {
                        long currentTick = Find.TickManager?.TicksAbs ?? 0;
                        coreComp.AddMemory(new RimSynapse.Models.WeightedMemory
                        {
                            summary = $"Cured of PTSD through supervised shooting practice with {supervisor.LabelShort}.",
                            weight = 1.0f,
                            baseWeight = 1.0f,
                            decayRate = 0f,
                            isLongTerm = true,
                            tags = new List<string> { "TraitShift", "Desensitization", "Recovery" },
                            memoryType = "TraitLost",
                            absTick = currentTick,
                            gameTick = Find.TickManager?.TicksGame ?? 0
                        });
                    }
                }
            }
        }
    }
}
