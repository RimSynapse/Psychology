using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Faith drifts toward the people you love (#72). The LLM eval DETERMINES a pawn's
    /// <see cref="SynapsePawnComp.conversionSusceptibility"/> (0..1 — is their certainty wavering, given their
    /// bonds + character); this C# driver then erodes their ideoligion Certainty a little each day, scaled by that
    /// gate × their warmth toward colonists who share the COLONY's faith. When certainty bottoms out, vanilla
    /// handles the actual conversion. No forced conversion — it's earned through the relationships already tracked.
    /// Strictly Ideology-DLC-gated and a no-op for a pawn already of the colony faith.
    /// </summary>
    public static class SynapseConversion
    {
        /// <summary>Certainty eroded per day at full warmth × full susceptibility (settings-tunable later).</summary>
        public static float BaseErosionPerDay = 0.05f;

        /// <summary>
        /// Pure: certainty eroded this day = <see cref="BaseErosionPerDay"/> × warmth × susceptibility. Both inputs
        /// are 0..1; the product means a pawn only drifts when they are BOTH open (LLM gate) AND fond of someone of
        /// the colony's faith (the compass). Either at zero → no drift.
        /// </summary>
        public static float CertaintyErosion(float warmthToColonyFaith01, float susceptibility)
        {
            float w = Clamp01(warmthToColonyFaith01);
            float s = Clamp01(susceptibility);
            return BaseErosionPerDay * w * s;
        }

        /// <summary>Strongest positive warmth this pawn holds toward a colonist of <paramref name="colonyIdeo"/>,
        /// as 0..1 — "the friend you're closest to who prays to their gods".</summary>
        public static float WarmthTowardColonyFaith(Pawn pawn, SynapsePawnComp comp, Ideo colonyIdeo)
        {
            if (pawn?.Map == null || comp?.socialNetwork == null || colonyIdeo == null) return 0f;
            float best = 0f;
            foreach (var kv in comp.socialNetwork)
            {
                if (kv.Value == null || kv.Value.warmth <= 0f) continue;
                var other = pawn.Map.mapPawns.FreeColonists.FirstOrDefault(c => c.GetUniqueLoadID() == kv.Key);
                if (other != null && other.Ideo == colonyIdeo && kv.Value.warmth > best) best = kv.Value.warmth;
            }
            return Clamp01(best / 100f);
        }

        /// <summary>Erode a pawn's faith one day's worth, per the LLM gate × the compass. Ideology-only; no-op for
        /// a pawn already of the colony faith, unshakeable pawns (susceptibility 0), or one with no fond ties to
        /// the faith. Returns the certainty eroded (0 if nothing happened), for the debug dump.</summary>
        public static float DriveDaily(Pawn pawn, SynapsePawnComp comp)
        {
            if (!ModsConfig.IdeologyActive || comp == null || pawn?.ideo == null) return 0f;
            var colonyIdeo = Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo;
            if (colonyIdeo == null || pawn.Ideo == null || pawn.Ideo == colonyIdeo) return 0f;

            float susc = Clamp01(comp.conversionSusceptibility);
            if (susc <= 0f) return 0f;
            float warmth01 = WarmthTowardColonyFaith(pawn, comp, colonyIdeo);
            float erosion = CertaintyErosion(warmth01, susc);
            if (erosion > 0f) pawn.ideo.OffsetCertainty(-erosion);
            return erosion;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
