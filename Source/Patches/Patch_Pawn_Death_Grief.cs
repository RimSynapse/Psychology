using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Psychology.API;

namespace RimSynapse.Psychology.Patches
{
    /// <summary>
    /// When a humanlike dies, seed grief (#17) on the colonists who were close to them. SeedGrief decides
    /// closeness (blood/love relations, then #72 compass warmth) and only afflicts those who truly grieve, so
    /// iterating every colonist is safe — a stranger's death seeds nothing.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Death_Grief
    {
        public static void Postfix(Pawn __instance)
        {
            Pawn deceased = __instance;
            if (deceased == null || !deceased.RaceProps.Humanlike) return;

            Map map = deceased.MapHeld ?? Find.CurrentMap;
            if (map?.mapPawns == null) return;

            foreach (Pawn mourner in map.mapPawns.FreeColonists.ToList())
            {
                if (mourner == deceased || mourner.Dead) continue;
                SynapseTherapyConditions.SeedGrief(mourner, deceased);
            }
        }
    }
}
