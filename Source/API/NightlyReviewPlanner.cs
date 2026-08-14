using System;

namespace RimSynapse.Psychology
{
    /// <summary>
    /// Psychology's coalescing lever on top of Core's <see cref="RimSynapse.SynapseDeadlineBatch"/> /
    /// <see cref="RimSynapse.SynapseBatchPlanner"/> (Psychology #33). Given the batch's plan for the
    /// current dispatch, it picks the degradation lever in a fixed order:
    ///
    /// <list type="number">
    /// <item><b>full</b> — on track: one colonist at full context.</item>
    /// <item><b>shrink</b> (Core's first lever) — one colonist at reduced context, while the scale stays
    /// above the coalesce threshold and Core is not yet asking to cut.</item>
    /// <item><b>coalesce</b> (Psychology's lever) — where Core would start dropping colonists, evaluate
    /// several in a single request instead. One coalesced request covers what Core would have cut, so no
    /// colonist is lost. The batch planner cannot do this itself: it needs the task's cooperation.</item>
    /// <item><b>cut</b> (last resort) — a lone colonist that still will not fit is dropped.</item>
    /// </list>
    ///
    /// Pure and deterministic — the sizing brain the batch drives; the async dispatch honours the result.
    /// </summary>
    public static class NightlyReviewPlanner
    {
        /// <summary>At or below this per-item context scale, coalesce colonists per request rather than shrink further.</summary>
        public const float CoalesceThreshold = 0.5f;

        public struct ReviewAction
        {
            /// <summary>Colonists to evaluate in this dispatch (1 = normal, &gt;1 = coalesced into one request).</summary>
            public int Subjects;
            /// <summary>Per-subject context scale to apply when building the prompt.</summary>
            public float ContextScale;
            /// <summary>The last resort: drop this item rather than delay the whole batch past morning.</summary>
            public bool Cut;
            /// <summary>Which lever is in effect: "full" | "shrink" | "coalesce" | "cut".</summary>
            public string Lever;
        }

        /// <param name="plan">Core's batch plan for the current dispatch.</param>
        /// <param name="itemsRemaining">Colonists still awaiting review.</param>
        /// <param name="maxCoalesce">Cap on colonists coalesced into a single request.</param>
        public static ReviewAction NextAction(SynapseBatchPlan plan, int itemsRemaining, int maxCoalesce = 4)
        {
            if (itemsRemaining <= 0)
                return new ReviewAction { Subjects = 0, ContextScale = 1f, Lever = "full" };

            if (plan.OnTrack)
                return new ReviewAction { Subjects = 1, ContextScale = 1f, Lever = "full" };

            // Lever 1 (Core): shrink per-item context while it stays above the coalesce threshold and Core
            // is not yet asking to cut.
            if (plan.ItemsToCut <= 0 && plan.ContextScale >= CoalesceThreshold)
                return new ReviewAction { Subjects = 1, ContextScale = plan.ContextScale, Lever = "shrink" };

            // Lever 2 (Psychology): coalesce rather than cut. Pack enough colonists into one request to
            // cover what Core would have dropped (ItemsToCut + the current one), capped and bounded by what
            // is actually left.
            if (itemsRemaining > 1 && maxCoalesce > 1)
            {
                int want = Math.Max(2, plan.ItemsToCut + 1);
                int subjects = Math.Min(want, Math.Min(maxCoalesce, itemsRemaining));
                return new ReviewAction { Subjects = subjects, ContextScale = plan.ContextScale, Lever = "coalesce" };
            }

            // Lever 3 (last resort): a single colonist that still will not fit is cut.
            return new ReviewAction { Subjects = 1, ContextScale = plan.ContextScale, Cut = true, Lever = "cut" };
        }
    }
}
