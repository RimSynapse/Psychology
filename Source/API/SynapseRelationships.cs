using Verse;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.Models;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Central entry points for moving the relationship compass (#72). Trust and warmth are always applied
    /// SYMMETRICALLY to both pawns' records (a relationship is one thing seen from two sides), and records are
    /// created on demand. Trust comes from shared trials (tending, fighting together, rescue, deep talks);
    /// warmth from personality compatibility and affectionate/hostile interactions. Callers (Harmony patches,
    /// the interaction hook, the compatibility read) funnel through here so the growth rules live in one place.
    /// </summary>
    public static class SynapseRelationships
    {
        /// <summary>Get-or-create both directed records for a pair, or (null, null) if either side can't hold one.</summary>
        public static bool TryPair(Pawn a, Pawn b, out SocialRecord recA, out SocialRecord recB)
        {
            recA = recB = null;
            if (a == null || b == null || a == b) return false;
            var compA = a.GetComp<SynapsePawnComp>();
            var compB = b.GetComp<SynapsePawnComp>();
            if (compA == null || compB == null) return false;

            string idA = a.GetUniqueLoadID(), idB = b.GetUniqueLoadID();
            if (!compA.socialNetwork.TryGetValue(idB, out recA)) { recA = new SocialRecord(); compA.socialNetwork[idB] = recA; }
            if (!compB.socialNetwork.TryGetValue(idA, out recB)) { recB = new SocialRecord(); compB.socialNetwork[idA] = recB; }
            return true;
        }

        /// <summary>Move the TRUST/RESPECT axis for a pair by <paramref name="amount"/> (both sides).</summary>
        public static void AwardTrust(Pawn a, Pawn b, float amount)
        {
            if (amount == 0f || !TryPair(a, b, out var recA, out var recB)) return;
            recA.AddTrust(amount);
            recB.AddTrust(amount);
        }

        /// <summary>Move the WARMTH (liking) axis for a pair by <paramref name="amount"/> (both sides).</summary>
        public static void AwardWarmth(Pawn a, Pawn b, float amount)
        {
            if (amount == 0f || !TryPair(a, b, out var recA, out var recB)) return;
            recA.AddWarmth(amount);
            recB.AddWarmth(amount);
        }
    }
}
