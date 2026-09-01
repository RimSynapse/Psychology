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
        /// <summary>Get-or-create just the <paramref name="from"/> → <paramref name="to"/> directed record, or
        /// null if it can't hold one. For ASYMMETRIC feelings — jealousy, a one-sided grudge — where one pawn
        /// resents another who may be oblivious.</summary>
        public static SocialRecord TryDirected(Pawn from, Pawn to)
        {
            if (from == null || to == null || from == to) return null;
            var comp = from.GetComp<SynapsePawnComp>();
            if (comp == null || to.GetComp<SynapsePawnComp>() == null) return null;
            string idTo = to.GetUniqueLoadID();
            if (!comp.socialNetwork.TryGetValue(idTo, out var rec)) { rec = new SocialRecord(); comp.socialNetwork[idTo] = rec; }
            return rec;
        }

        /// <summary>Move ONLY <paramref name="from"/>'s trust toward <paramref name="to"/> (one-sided). Negative
        /// amounts sour it — e.g. jealousy or a betrayal felt by one side alone.</summary>
        public static void AwardTrustDirected(Pawn from, Pawn to, float amount)
        {
            if (amount == 0f) return;
            TryDirected(from, to)?.AddTrust(amount);
        }

        /// <summary>Move ONLY <paramref name="from"/>'s warmth toward <paramref name="to"/> (one-sided).</summary>
        public static void AwardWarmthDirected(Pawn from, Pawn to, float amount)
        {
            if (amount == 0f) return;
            TryDirected(from, to)?.AddWarmth(amount);
        }

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
