using System;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Models;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Named familiarity milestones (#23): as two colonists' relationship familiarity (0–100, grown on every
    /// interaction) first crosses each named threshold, the player gets ONE letter naming both of them. A
    /// per-relationship marker (<see cref="SocialRecord.highestFamiliarityMilestone"/>) makes each threshold fire
    /// exactly once — never again on load, and never re-firing if familiarity dips and climbs back (hysteresis).
    /// The advance decision is pure and split out so it is unit-testable without a live letter stack.
    /// </summary>
    public static class SynapseFamiliarityMilestones
    {
        /// <summary>Ordered, ascending by threshold. Index is the value stored in the per-record marker.</summary>
        public static readonly (string label, float threshold)[] Milestones =
        {
            ("Close Friends", 40f),
            ("Best Friends",  70f),
            ("Confidants",    95f),
        };

        /// <summary>Highest milestone index whose threshold the familiarity meets, or -1 if below the first.</summary>
        public static int MilestoneIndexFor(float familiarity)
        {
            int idx = -1;
            for (int i = 0; i < Milestones.Length; i++)
                if (familiarity >= Milestones[i].threshold) idx = i; else break;
            return idx;
        }

        /// <summary>
        /// Pure core: advance the PAIR's shared marker to whatever the current familiarity now warrants, and
        /// return the newly-reached milestone index (or -1 if none is new). Both records are marked together so
        /// the reciprocal record can't fire a second letter, and a lower/equal current band never re-advances.
        /// </summary>
        public static int AdvanceMilestone(SocialRecord recA, SocialRecord recB, float familiarity)
        {
            if (recA == null || recB == null) return -1;
            int alreadyReached = Math.Max(recA.highestFamiliarityMilestone, recB.highestFamiliarityMilestone);
            int reached = MilestoneIndexFor(familiarity);
            if (reached <= alreadyReached) return -1;
            recA.highestFamiliarityMilestone = reached;
            recB.highestFamiliarityMilestone = reached;
            return reached;
        }

        /// <summary>
        /// Grow-side hook: after an interaction has raised both records' familiarity, fire a single player letter
        /// naming both colonists if the pair just crossed a new named threshold. De-duped at the pair level, so the
        /// reciprocal record does not send a duplicate.
        /// </summary>
        public static void CheckAndNotify(Pawn a, Pawn b, SocialRecord recA, SocialRecord recB)
        {
            if (a == null || b == null || recA == null || recB == null) return;
            // Both records grow symmetrically; the min is the safe pair familiarity if they ever drift.
            float familiarity = Math.Min(recA.familiarity, recB.familiarity);
            int reached = AdvanceMilestone(recA, recB, familiarity);
            if (reached < 0) return;

            string label = Milestones[reached].label;
            string title = $"Friendship: {label}";
            if (Prefs.DevMode) title = "[RimSynapse Psychology] " + title;
            string text = $"{a.Name.ToStringShort} and {b.Name.ToStringShort} have become {label}.";
            Find.LetterStack?.ReceiveLetter(title, text, LetterDefOf.PositiveEvent, new LookTargets(a, b));
        }

        /// <summary>The current milestone label for a relationship (for the Social tab), or null if none yet.</summary>
        public static string CurrentLabel(SocialRecord rec)
        {
            if (rec == null) return null;
            int idx = MilestoneIndexFor(rec.familiarity);
            return idx >= 0 ? Milestones[idx].label : null;
        }
    }
}
