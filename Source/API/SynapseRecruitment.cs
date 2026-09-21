using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// A prisoner comes to want to stay for the people (#72) — the recruitment end of the loop. Their
    /// resistance to joining is shaved each day by how fond they've grown of the colonists (the compass, built
    /// through warden interactions), and MUCH faster once they share the colony's faith — so the arc completes:
    /// befriend → warm → (their faith wavers →) convert → resistance crashes → they join willingly. No forced
    /// recruit; it's earned. Prisoner-only, and it never drops resistance below zero (vanilla then recruits).
    /// </summary>
    public static class SynapseRecruitment
    {
        /// <summary>Resistance shaved per day at full warmth, before the faith multiplier (settings-tunable later).</summary>
        public static float BaseResistancePerDay = 0.35f;
        /// <summary>A prisoner who shares the colony's faith loses resistance this many times faster.</summary>
        public static float ConvertRecruitMultiplier = 3f;

        /// <summary>Pure: resistance shaved this day = base × warmth × (convert ? multiplier : 1). Zero warmth →
        /// nothing, so a prisoner nobody has bonded with doesn't soften on their own.</summary>
        public static float ResistanceReduction(float warmthToColonists01, bool sharesColonyFaith)
        {
            float w = Clamp01(warmthToColonists01);
            return BaseResistancePerDay * w * (sharesColonyFaith ? ConvertRecruitMultiplier : 1f);
        }

        /// <summary>Strongest positive warmth this prisoner holds toward any free colonist, as 0..1.</summary>
        public static float WarmthTowardColonists(Pawn prisoner, SynapsePawnComp comp)
        {
            if (prisoner?.Map == null || comp?.socialNetwork == null) return 0f;
            float best = 0f;
            foreach (var kv in comp.socialNetwork)
            {
                if (kv.Value == null || kv.Value.warmth <= best) continue;
                var other = prisoner.Map.mapPawns.FreeColonists.FirstOrDefault(c => c.GetUniqueLoadID() == kv.Key);
                if (other != null) best = kv.Value.warmth;
            }
            return Clamp01(best / 100f);
        }

        /// <summary>Shave a recruitable prisoner's resistance one day's worth, per their warmth toward the
        /// colonists (× the faith bonus). Returns the resistance removed (0 if nothing happened), for the debug dump.</summary>
        public static float DriveDaily(Pawn prisoner, SynapsePawnComp comp)
        {
            if (prisoner == null || comp == null || !prisoner.IsPrisonerOfColony) return 0f;
            var guest = prisoner.guest;
            if (guest == null || !guest.Recruitable || guest.resistance <= 0f) return 0f;

            float warmth01 = WarmthTowardColonists(prisoner, comp);
            if (warmth01 <= 0f) return 0f;

            bool sharesFaith = false;
            if (ModsConfig.IdeologyActive)
            {
                var colonyIdeo = Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo;
                sharesFaith = colonyIdeo != null && prisoner.Ideo == colonyIdeo;
            }
            float reduction = ResistanceReduction(warmth01, sharesFaith);
            if (reduction <= 0f) return 0f;
            guest.resistance = System.Math.Max(0f, guest.resistance - reduction);
            return reduction;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
