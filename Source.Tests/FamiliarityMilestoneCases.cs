using System.Collections.Generic;
using System.Linq;
using Verse;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Models;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Familiarity milestone events (#23): as a pair's familiarity first crosses a named threshold the player
    /// gets ONE letter, and a per-relationship marker makes it fire exactly once — never on load, never on a
    /// dip-and-reclimb. These pin the pure decision (threshold mapping, single-fire, hysteresis, pair de-dup)
    /// and that the marker survives save/load. The letter side effect itself is exercised by the debug action.
    /// </summary>
    [SynapseTestSet]
    public static class FamiliarityMilestoneCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // Threshold mapping: below the first is -1; each band maps to its index; above the last stays last.
            yield return new SynapseTestCase("Psychology_FamiliarityMilestones_IndexForThresholds", () =>
            {
                var ms = SynapseFamiliarityMilestones.Milestones;
                Assert.True(ms.Length >= 3, "there are at least three named milestones");
                Assert.Equal(-1, SynapseFamiliarityMilestones.MilestoneIndexFor(ms[0].threshold - 0.1f), "just below the first is no milestone");
                Assert.Equal(0, SynapseFamiliarityMilestones.MilestoneIndexFor(ms[0].threshold), "at the first threshold is index 0");
                Assert.Equal(1, SynapseFamiliarityMilestones.MilestoneIndexFor(ms[1].threshold), "at the second threshold is index 1");
                Assert.Equal(ms.Length - 1, SynapseFamiliarityMilestones.MilestoneIndexFor(100f), "max familiarity is the last milestone");
                return $"{ms.Length} milestones: {string.Join(", ", System.Array.ConvertAll(ms, m => $"{m.label}@{m.threshold:F0}"))}";
            },
            tier: "Execution", polarity: "positive",
            scenario: "Familiarity values are mapped to named milestone bands",
            expectation: "Below the first is none; each threshold maps to its band; the top stays the top");

            // Advance fires once per newly-reached band; the same band never re-fires; a higher band does.
            yield return new SynapseTestCase("Psychology_FamiliarityMilestones_AdvanceFiresOncePerBand", () =>
            {
                var ms = SynapseFamiliarityMilestones.Milestones;
                var a = new SocialRecord(); var b = new SocialRecord();

                int first = SynapseFamiliarityMilestones.AdvanceMilestone(a, b, ms[0].threshold + 1f);
                Assert.Equal(0, first, "first crossing of band 0 reports index 0 (fire once)");
                Assert.Equal(0, a.highestFamiliarityMilestone, "record A marked to 0");
                Assert.Equal(0, b.highestFamiliarityMilestone, "record B marked to 0 (pair de-dup)");

                int again = SynapseFamiliarityMilestones.AdvanceMilestone(a, b, ms[0].threshold + 5f);
                Assert.Equal(-1, again, "staying within band 0 does not re-fire");

                int second = SynapseFamiliarityMilestones.AdvanceMilestone(a, b, ms[1].threshold + 1f);
                Assert.Equal(1, second, "crossing into band 1 fires once");
                Assert.Equal(1, b.highestFamiliarityMilestone, "both markers advance to 1");
                return "band0 fires, holds, band1 fires";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A pair's familiarity climbs through the bands",
            expectation: "Exactly one fire per newly-reached band, both records marked together");

            // Hysteresis: after reaching a band, dropping below and re-climbing does NOT re-fire.
            yield return new SynapseTestCase("Psychology_FamiliarityMilestones_ReCrossNoSpam", () =>
            {
                var ms = SynapseFamiliarityMilestones.Milestones;
                var a = new SocialRecord(); var b = new SocialRecord();
                SynapseFamiliarityMilestones.AdvanceMilestone(a, b, ms[1].threshold + 1f); // reach band 1

                int dip = SynapseFamiliarityMilestones.AdvanceMilestone(a, b, 0f); // familiarity collapses
                Assert.Equal(-1, dip, "a dip never fires (marker is sticky)");
                Assert.Equal(1, a.highestFamiliarityMilestone, "the marker is NOT rolled back by a dip");

                int reclimb = SynapseFamiliarityMilestones.AdvanceMilestone(a, b, ms[1].threshold + 2f); // climb back
                Assert.Equal(-1, reclimb, "re-crossing an already-reached band does not re-fire");
                return "dip and reclimb produce no new letters";
            },
            tier: "Execution", polarity: "negative",
            scenario: "Familiarity dips below a reached milestone and climbs back",
            expectation: "No duplicate letter — the already-reached marker holds");

            // Pair de-dup: the reciprocal record can't fire a second time for the same band.
            yield return new SynapseTestCase("Psychology_FamiliarityMilestones_ReciprocalNoDouble", () =>
            {
                var ms = SynapseFamiliarityMilestones.Milestones;
                // Simulate one side already marked (as if its notify ran first), the other still -1.
                var a = new SocialRecord { highestFamiliarityMilestone = 0 };
                var b = new SocialRecord { highestFamiliarityMilestone = -1 };
                int r = SynapseFamiliarityMilestones.AdvanceMilestone(a, b, ms[0].threshold + 1f);
                Assert.Equal(-1, r, "the reciprocal side does not re-fire band 0 (max-of-both already-reached)");
                return "reciprocal record de-duped";
            },
            tier: "Execution", polarity: "negative",
            scenario: "The reciprocal SocialRecord is checked after its partner already advanced",
            expectation: "No second letter for the same band");

            // The marker survives a real Scribe save/load round-trip.
            yield return new SynapseTestCase("Psychology_FamiliarityMilestones_MarkerRoundTrips", () =>
            {
                var rec = new SocialRecord { familiarity = 72f, trust = 10f, highestFamiliarityMilestone = 1 };
                var reloaded = ScribeRoundTrip(rec);
                Assert.NotNull(reloaded, "record survives a scribe round-trip");
                Assert.Equal(1, reloaded.highestFamiliarityMilestone, "the milestone marker survives save/load");
                Assert.True(System.Math.Abs(reloaded.familiarity - 72f) < 0.001f, "familiarity survives too");
                return "marker round-trips as 1";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A save is written and reloaded with a relationship that reached a milestone",
            expectation: "The milestone marker persists, so no threshold re-fires on load");

            // Full path on a live pair: CheckAndNotify runs the real ReceiveLetter call + LookTargets on two
            // pawns without throwing, advances the pair marker on the first crossing, and is a no-op on the
            // second. (It does NOT assert the letter-stack count — Core's deferred-news system holds letters to
            // batch them, so a milestone letter is deferred rather than added to the stack synchronously.)
            yield return new SynapseTestCase("Psychology_FamiliarityMilestones_NotifyRunsAndDedups",
                NotifyRunsAndDedups,
                skipReason: () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                    var cs = map?.mapPawns?.FreeColonists;
                    return (cs != null && cs.Count >= 2 && Find.LetterStack != null) ? null : "need two colonists and a letter stack";
                },
                tier: "Execution", polarity: "positive",
                scenario: "A colonist pair first crosses the Close Friends threshold",
                expectation: "The notify path runs cleanly and advances the marker once; a second check is a no-op");

            // A save predating milestones (no marker key) loads as -1, not 0 — nothing spuriously already-reached.
            yield return new SynapseTestCase("Psychology_FamiliarityMilestones_LegacyDefaultsToNone", () =>
            {
                Assert.Equal(-1, new SocialRecord().highestFamiliarityMilestone, "a fresh/legacy record has no milestone reached");
                return "legacy default -1";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A relationship record from before this feature loads",
            expectation: "It starts at 'no milestone', so its true first crossing still notifies");
        }

        private static string NotifyRunsAndDedups()
        {
            var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            var colonists = map.mapPawns.FreeColonists.ToList();
            var a = colonists[0];
            var b = colonists[1];
            float t0 = SynapseFamiliarityMilestones.Milestones[0].threshold;
            var recA = new SocialRecord { familiarity = t0 + 1f };
            var recB = new SocialRecord { familiarity = t0 + 1f };

            var stack = Find.LetterStack;
            int before = stack.LettersListForReading.Count;
            try
            {
                // First crossing: the full path runs (real ReceiveLetter + LookTargets on two pawns) and advances.
                SynapseFamiliarityMilestones.CheckAndNotify(a, b, recA, recB);
                Assert.Equal(0, recA.highestFamiliarityMilestone, "the pair marker advanced to band 0");
                Assert.Equal(0, recB.highestFamiliarityMilestone, "both records advanced together");

                // Second call: no new advance (the de-dup that prevents a duplicate letter).
                recA.highestFamiliarityMilestone = recB.highestFamiliarityMilestone = 0;
                int reFired = SynapseFamiliarityMilestones.AdvanceMilestone(recA, recB, recA.familiarity);
                Assert.Equal(-1, reFired, "a second check on the same band is a no-op (no duplicate letter)");
                return $"notify path ran for {a.LabelShort} ↔ {b.LabelShort}; marker advanced once, no re-fire";
            }
            finally
            {
                // Core's deferred-news may or may not have added a letter synchronously; clean up any we caused.
                var list = stack.LettersListForReading;
                while (list.Count > before) stack.RemoveLetter(list[list.Count - 1]);
            }
        }

        /// <summary>Save one record to a scratch file and load it back, exercising ExposeData both ways.</summary>
        private static SocialRecord ScribeRoundTrip(SocialRecord record)
        {
            string path = System.IO.Path.Combine(GenFilePaths.ConfigFolderPath, "synapse_familiarity_roundtrip.xml");
            try
            {
                var toSave = record;
                Scribe.saver.InitSaving(path, "test");
                try { Scribe_Deep.Look(ref toSave, "record"); }
                finally { Scribe.saver.FinalizeSaving(); }

                SocialRecord loaded = null;
                Scribe.loader.InitLoading(path);
                try { Scribe_Deep.Look(ref loaded, "record"); }
                finally { Scribe.loader.FinalizeLoading(); }
                return loaded;
            }
            finally
            {
                try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
            }
        }
    }
}
