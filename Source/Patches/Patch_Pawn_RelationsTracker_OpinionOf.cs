using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.Patches
{
    [HarmonyPatch(typeof(Pawn_RelationsTracker), "OpinionOf")]
    public static class Patch_Pawn_RelationsTracker_OpinionOf
    {
        public static void Postfix(Pawn_RelationsTracker __instance, Pawn other, ref int __result, Pawn ___pawn)
        {
            if (___pawn == null || other == null) return;
            
            var comp = ___pawn.GetComp<SynapsePawnComp>();
            if (comp != null && comp.socialNetwork != null)
            {
                string otherId = other.GetUniqueLoadID();
                if (comp.socialNetwork.TryGetValue(otherId, out var record) && record != null)
                {
                    // Blend vanilla opinion with Synapse trust (tunable, Stage 3):
                    // 0 = pure vanilla, 1 = pure trust; default 0.5 keeps the original 50/50.
                    float blend = RimSynapse.Psychology.RimSynapsePsychologyMod.Settings?.opinionTrustBlend ?? 0.5f;
                    float vanilla = __result * (1f - blend);
                    float trustFactor = record.trust * blend;

                    __result = UnityEngine.Mathf.RoundToInt(vanilla + trustFactor);
                    __result = UnityEngine.Mathf.Clamp(__result, -100, 100);
                }
            }
        }
    }
}
