using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Psychology.API;

namespace RimSynapse.Psychology.Patches
{
    /// <summary>
    /// Fighting together builds TRUST (#72). When a colonist fells a threat to the colony, the colleagues who
    /// were drafted and fighting near the kill share the victory — each gains mutual trust with the killer.
    /// Only genuine threats count (hostile to the player faction), so hunting a deer or putting down a colony
    /// animal doesn't bond anyone.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_CombatTogether
    {
        private const float TrustPerSharedKill = 2f;
        private const float BondRadius = 18f;

        public static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            var victim = __instance;
            if (victim?.Map == null) return;

            var player = Faction.OfPlayerSilentFail;
            if (player == null || victim.Faction == player) return;   // don't bond over a colonist's death
            if (!victim.HostileTo(player)) return;                    // only a real threat counts
            if (!(dinfo?.Instigator is Pawn killer) || !killer.IsColonist) return;

            SynapseRelationships.AwardSharedVictory(killer, victim.Map, victim.Position, BondRadius, TrustPerSharedKill);
        }
    }
}
