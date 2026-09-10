using System;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Models;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Two-axis relationship milestones (#72 Phase 4, reworking #23). A relationship is a point on the warmth
    /// (liking) / trust (reliance) compass, with familiarity as the CONFIDENCE gate — you can be neither sworn
    /// friends nor sworn enemies with someone you barely know. Milestones therefore live on TWO ladders:
    ///   • Friendship — rising warmth + trust (Close Friends → Best Friends → Confidants).
    ///   • Rivalry    — cold warmth + low trust (Rivals → Sworn Enemies).
    /// Both are gated on familiarity, which kills the old familiarity-only bug where two colonists who only ever
    /// insulted each other still crossed a "Best Friends" threshold. A former rival who later warms into a
    /// friendship band fires a distinct RECONCILIATION letter ("old enemies, now friends"), once.
    ///
    /// Each ladder keeps the #23 guarantees: one letter per newly-reached band (sticky marker), never re-firing
    /// on load or on a dip-and-reclimb (hysteresis), and de-duped at the pair level so the reciprocal record
    /// can't send a second letter. The pure advance/index decisions are split out so they are unit-testable
    /// without a live letter stack.
    /// </summary>
    public static class SynapseRelationshipMilestones
    {
        /// <summary>Friendship bands, ascending. A band is reached only when warmth, trust AND familiarity all
        /// meet its gate; thresholds increase monotonically across bands so "highest passing band" is well-defined.</summary>
        public static readonly (string label, float minWarmth, float minTrust, float minFamiliarity)[] FriendshipLadder =
        {
            ("Close Friends", 30f, 10f, 40f),
            ("Best Friends",  55f, 35f, 60f),
            ("Confidants",    75f, 60f, 80f),
        };

        /// <summary>Rivalry bands, descending in warmth. The first band reuses the relationship-review rivalry
        /// gate (<see cref="SynapseRelationshipReview.RivalryWarmth"/> / <see cref="SynapseRelationshipReview.RivalryFamiliarity"/>)
        /// so "what counts as a rivalry" has one source of truth across the mod.</summary>
        public static readonly (string label, float maxWarmth, float maxTrust, float minFamiliarity)[] RivalryLadder =
        {
            ("Rivals",        SynapseRelationshipReview.RivalryWarmth, 0f,   SynapseRelationshipReview.RivalryFamiliarity),
            ("Sworn Enemies", -75f,                                    -30f, 80f),
        };

        /// <summary>Highest friendship band index whose warmth/trust/familiarity gate all pass, or -1 if none.</summary>
        public static int FriendshipIndexFor(float warmth, float trust, float familiarity)
        {
            int idx = -1;
            for (int i = 0; i < FriendshipLadder.Length; i++)
            {
                var g = FriendshipLadder[i];
                if (warmth >= g.minWarmth && trust >= g.minTrust && familiarity >= g.minFamiliarity) idx = i; else break;
            }
            return idx;
        }

        /// <summary>Highest rivalry band index whose (cold) warmth/trust and familiarity gate all pass, or -1.</summary>
        public static int RivalryIndexFor(float warmth, float trust, float familiarity)
        {
            int idx = -1;
            for (int i = 0; i < RivalryLadder.Length; i++)
            {
                var g = RivalryLadder[i];
                if (warmth <= g.maxWarmth && trust <= g.maxTrust && familiarity >= g.minFamiliarity) idx = i; else break;
            }
            return idx;
        }

        /// <summary>Advance the pair's shared friendship marker to whatever the axes now warrant; return the newly
        /// reached band (or -1). Both records are marked together (pair de-dup); a lower/equal band never re-advances.</summary>
        public static int AdvanceFriendship(SocialRecord recA, SocialRecord recB, float warmth, float trust, float familiarity)
        {
            if (recA == null || recB == null) return -1;
            int already = Math.Max(recA.highestFriendshipMilestone, recB.highestFriendshipMilestone);
            int reached = FriendshipIndexFor(warmth, trust, familiarity);
            if (reached <= already) return -1;
            recA.highestFriendshipMilestone = recB.highestFriendshipMilestone = reached;
            return reached;
        }

        /// <summary>Advance the pair's shared rivalry marker; return the newly reached band (or -1). Symmetric,
        /// sticky and pair-de-duped, exactly like the friendship ladder.</summary>
        public static int AdvanceRivalry(SocialRecord recA, SocialRecord recB, float warmth, float trust, float familiarity)
        {
            if (recA == null || recB == null) return -1;
            int already = Math.Max(recA.highestRivalryMilestone, recB.highestRivalryMilestone);
            int reached = RivalryIndexFor(warmth, trust, familiarity);
            if (reached <= already) return -1;
            recA.highestRivalryMilestone = recB.highestRivalryMilestone = reached;
            return reached;
        }

        /// <summary>
        /// Grow-side hook: after an interaction moved the compass, fire at most ONE player letter for a pair that
        /// just crossed a new band. Rivalry is checked first (a pair going cold shouldn't also read as "friends" in
        /// the same tick). A former rival crossing into friendship reconciles — one distinct letter, once. Both
        /// axes use the MIN across the two records so a milestone reflects a MUTUAL state, never a one-sided feeling.
        /// </summary>
        public static void CheckAndNotify(Pawn a, Pawn b, SocialRecord recA, SocialRecord recB)
        {
            if (a == null || b == null || recA == null || recB == null) return;

            float warmth = Math.Min(recA.warmth, recB.warmth);
            float trust = Math.Min(recA.trust, recB.trust);
            float familiarity = Math.Min(recA.familiarity, recB.familiarity);

            int rivalry = AdvanceRivalry(recA, recB, warmth, trust, familiarity);
            if (rivalry >= 0)
            {
                string label = RivalryLadder[rivalry].label;
                FireLetter(a, b, $"Rivalry: {label}",
                    $"{a.Name.ToStringShort} and {b.Name.ToStringShort} have become {label}.",
                    LetterDefOf.NegativeEvent);
                return;
            }

            int friendship = AdvanceFriendship(recA, recB, warmth, trust, familiarity);
            if (friendship < 0) return;

            // Reconciliation: a pair that once reached a rivalry band and has now warmed into friendship. Fire the
            // stronger, once-only letter instead of the plain friendship one; later friendship bands notify normally.
            bool wereRivals = recA.highestRivalryMilestone >= 0 || recB.highestRivalryMilestone >= 0;
            bool alreadyReconciled = recA.reconciled || recB.reconciled;
            if (wereRivals && !alreadyReconciled)
            {
                recA.reconciled = recB.reconciled = true;
                int wasBand = Math.Max(recA.highestRivalryMilestone, recB.highestRivalryMilestone);
                FireLetter(a, b, "Reconciliation: Old Enemies, Now Friends",
                    $"{a.Name.ToStringShort} and {b.Name.ToStringShort}, once {RivalryLadder[wasBand].label}, have set their enmity aside and become {FriendshipLadder[friendship].label}.",
                    LetterDefOf.PositiveEvent);
                return;
            }

            string flabel = FriendshipLadder[friendship].label;
            FireLetter(a, b, $"Friendship: {flabel}",
                $"{a.Name.ToStringShort} and {b.Name.ToStringShort} have become {flabel}.",
                LetterDefOf.PositiveEvent);
        }

        /// <summary>The relationship's current status label for the Social tab (rivalry takes precedence over
        /// friendship), derived live from this record's axes, or null if it sits in no named band.</summary>
        public static string CurrentStatus(SocialRecord rec)
        {
            if (rec == null) return null;
            int r = RivalryIndexFor(rec.warmth, rec.trust, rec.familiarity);
            if (r >= 0) return RivalryLadder[r].label;
            int f = FriendshipIndexFor(rec.warmth, rec.trust, rec.familiarity);
            return f >= 0 ? FriendshipLadder[f].label : null;
        }

        private static void FireLetter(Pawn a, Pawn b, string title, string text, LetterDef def)
        {
            if (Prefs.DevMode) title = "[RimSynapse Psychology] " + title;
            Find.LetterStack?.ReceiveLetter(title, text, def, new LookTargets(a, b));
        }
    }
}
