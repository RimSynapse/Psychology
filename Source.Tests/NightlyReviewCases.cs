using System.Collections.Generic;
using RimSynapse;
using RimSynapse.Psychology;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// The nightly clinical review batch (Psychology #33): the coalescing lever on top of Core's
    /// SynapseDeadlineBatch / SynapseBatchPlanner. Deterministic — pure planning over synthetic timings,
    /// no game and no LLM. Proves the degradation order (full -> shrink -> coalesce -> cut) and the
    /// drop-on-expiry policy.
    /// </summary>
    [SynapseTestSet]
    public static class NightlyReviewCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // Worsening conditions walk the levers in order: on track, then shrink, then coalesce, then cut.
            yield return new SynapseTestCase("Psychology_NightlyDeadline", () =>
            {
                var batch = new SynapseDeadlineBatch("Nightly Reviews", 0, 12000, 5, dropOnExpiry: true);

                // Plenty of time, cheap items -> on track at full context.
                string full = NightlyReviewPlanner.NextAction(
                    batch.PlanNow(0, 180f, 200, 100), batch.ItemsRemaining).Lever;

                // Item cost above budget but a shrink still fits (budget/est = 0.75, exact in float) -> shrink first.
                string shrink = NightlyReviewPlanner.NextAction(
                    batch.PlanNow(6600, 180f, 8000, 500), batch.ItemsRemaining).Lever;

                // Shrinking to the floor no longer fits (Core would cut) -> coalesce instead.
                var coalesce = NightlyReviewPlanner.NextAction(
                    batch.PlanNow(11500, 60f, 5000, 3000), batch.ItemsRemaining);

                // One colonist left and still impossible -> cut (drop) as the last resort.
                while (batch.ItemsRemaining > 1) batch.MarkItemDone();
                var cut = NightlyReviewPlanner.NextAction(
                    batch.PlanNow(11900, 60f, 999999, 500000), batch.ItemsRemaining);

                Assert.Equal("full", full, "starts on track at full context");
                Assert.Equal("shrink", shrink, "shrinks per-item context first");
                Assert.Equal("coalesce", coalesce.Lever, "coalesces before cutting");
                Assert.True(coalesce.Subjects > 1, $"a coalesced request packs multiple colonists ({coalesce.Subjects})");
                Assert.True(cut.Cut, "cuts a lone unfittable colonist as the last resort");

                return $"full -> shrink -> coalesce({coalesce.Subjects}) -> cut";
            });

            // At morning, unfinished reviews are dropped (a stale nightly review is worthless by noon).
            yield return new SynapseTestCase("Psychology_NightlyDeadline_DropsAtMorning", () =>
            {
                var batch = new SynapseDeadlineBatch("Nightly Reviews", 0, 100, 3, dropOnExpiry: true);
                batch.MarkItemDone(); // 1 of 3 done before the window closes

                Assert.True(batch.Expired(100), "the window expires at morning");
                int carried = batch.ExpireNow();
                Assert.Equal(0, carried, "drop-on-expiry carries nothing to the next window");
                Assert.Equal(0, batch.ItemsRemaining, "the batch is closed out");
                return "2 unfinished dropped at morning";
            });
        }
    }
}
