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

            // OpinionOf is one of the hottest methods in the game (social thoughts, target
            // selection, float menus, lord/caravan AI). The overwhelming majority of calls are
            // for pawns with no Synapse social record, so bail before allocating the id string
            // and doing the dictionary lookup. (Re-keying socialNetwork from string to int id to
            // drop the GetUniqueLoadID allocation entirely is tracked in Psychology #57 — it
            // touches serialization and every access site.)
            var comp = ___pawn.GetComp<SynapsePawnComp>();
            if (comp?.socialNetwork == null || comp.socialNetwork.Count == 0) return;

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
