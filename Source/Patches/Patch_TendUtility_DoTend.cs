using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Psychology.API;

namespace RimSynapse.Psychology.Patches
{
    /// <summary>
    /// Tending builds TRUST (#72) — once per tend SESSION, not per wound. <see cref="TendUtility.DoTend"/>
    /// performs a single treatment covering every optimally-tended hediff in one pass, so a postfix here fires
    /// once per act of care no matter how many injuries were dressed. Self-tend and no-doctor tends award nothing.
    /// </summary>
    [HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
    public static class Patch_TendUtility_DoTend
    {
        private const float TrustPerTendSession = 3f;

        public static void Postfix(Pawn doctor, Pawn patient)
        {
            if (doctor == null || patient == null || doctor == patient) return;
            // One caring act → one mutual trust increment, regardless of how many wounds were tended.
            SynapseRelationships.AwardTrust(doctor, patient, TrustPerTendSession);
        }
    }
}
