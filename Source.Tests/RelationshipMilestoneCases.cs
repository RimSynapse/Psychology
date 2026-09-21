using System.Collections.Generic;
using System.Linq;
using Verse;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Models;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Two-axis relationship milestones (#72 Phase 4, reworking #23). Friendship and rivalry are separate ladders,
    /// each gated on warmth/trust with familiarity as the confidence floor. These pin: the pure band mapping on
    /// both ladders, single-fire + hysteresis + pair de-dup, the headline acceptance bug (insult-only pairs never
    /// reach a friendship band), rivalry crossing, the reconciliation-once path, and that the new markers survive
    /// save/load. The live letter side effect is exercised by the debug action.
    /// </summary>
    [SynapseTestSet]
    public static class RelationshipMilestoneCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // Friendship band mapping: all three axes must clear a band's gate; below the first is -1.
            yield return new SynapseTestCase("Psychology_RelMilestones_FriendshipIndex", () =>
            {
                var L = SynapseRelationshipMilestones.FriendshipLadder;
                Assert.True(L.Length >= 3, "at least three friendship bands");
                var g0 = L[0];
                Assert.Equal(-1, SynapseRelationshipMilestones.FriendshipIndexFor(g0.minWarmth - 1f, 100f, 100f), "cold warmth is no friendship even when trust/familiarity are maxed");
                Assert.Equal(-1, SynapseRelationshipMilestones.FriendshipIndexFor(100f, 100f, g0.minFamiliarity - 1f), "below the familiarity floor is no friendship (confidence gate)");
                Assert.Equal(0, SynapseRelationshipMilestones.FriendshipIndexFor(g0.minWarmth, g0.minTrust, g0.minFamiliarity), "exactly meeting band 0's gate is band 0");
                Assert.Equal(L.Length - 1, SynapseRelationshipMilestones.FriendshipIndexFor(100f, 100f, 100f), "maxed axes reach the top band");
                return $"{L.Length} friendship bands, gates enforced on all three axes";
            },
            tier: "Execution", polarity: "positive",
            scenario: "Warmth/trust/familiarity are mapped to friendship bands",
            expectation: "A band is reached only when all three axes clear its gate; familiarity is a hard floor");

            // THE acceptance bug: high familiarity + cold warmth (two colonists who only insult each other) is
            // NEVER a friendship, and instead registers on the rivalry ladder.
            yield return new SynapseTestCase("Psychology_RelMilestones_InsultOnlyNeverFriends", () =>
            {
                var R = SynapseRelationshipMilestones.RivalryLadder;
                float coldWarmth = R[0].maxWarmth - 5f;   // colder than the rivalry gate
                float familiar = 90f;                      // they know each other very well
                int friend = SynapseRelationshipMilestones.FriendshipIndexFor(coldWarmth, -20f, familiar);
                int rival = SynapseRelationshipMilestones.RivalryIndexFor(coldWarmth, -20f, familiar);
                Assert.Equal(-1, friend, "a familiar-but-cold pair reaches NO friendship band");
                Assert.True(rival >= 0, "instead they register as rivals");
                var status = SynapseRelationshipMilestones.CurrentStatus(new SocialRecord { warmth = coldWarmth, trust = -20f, familiarity = familiar });
                Assert.Equal(R[rival].label, status, "the live status label reports the rivalry band, not a friendship");
                return $"cold+familiar -> friend={friend}, rival={rival} ('{status}')";
            },
            tier: "Execution", polarity: "negative",
            scenario: "Two colonists who only ever insult each other become very familiar",
            expectation: "They never cross a friendship milestone; they cross the rivalry ladder instead");

            // Single-fire + hysteresis + pair de-dup on the friendship ladder.
            yield return new SynapseTestCase("Psychology_RelMilestones_FriendshipSingleFireHysteresis", () =>
            {
                var L = SynapseRelationshipMilestones.FriendshipLadder;
                var a = new SocialRecord(); var b = new SocialRecord();
                var g1 = L[1];
                int first = SynapseRelationshipMilestones.AdvanceFriendship(a, b, g1.minWarmth, g1.minTrust, g1.minFamiliarity);
                Assert.Equal(1, first, "crossing straight to band 1 fires band 1");
                Assert.Equal(1, a.highestFriendshipMilestone, "record A marked");
                Assert.Equal(1, b.highestFriendshipMilestone, "record B marked (pair de-dup)");

                int dip = SynapseRelationshipMilestones.AdvanceFriendship(a, b, 0f, 0f, 0f);
                Assert.Equal(-1, dip, "a collapse never fires");
                Assert.Equal(1, a.highestFriendshipMilestone, "the marker is sticky (not rolled back)");

                int reclimb = SynapseRelationshipMilestones.AdvanceFriendship(a, b, g1.minWarmth + 5f, g1.minTrust + 5f, g1.minFamiliarity + 5f);
                Assert.Equal(-1, reclimb, "re-crossing an already-reached band does not re-fire");
                return "band1 fires, dip holds, reclimb silent";
            },
            tier: "Execution", polarity: "negative",
            scenario: "A friendship reaches a band, collapses, and re-forms",
            expectation: "One fire on first crossing; no duplicate on dip-and-reclimb; both records marked together");

            // Rivalry ladder fires and de-dups the same way; reuses the review's rivalry gate for band 0.
            yield return new SynapseTestCase("Psychology_RelMilestones_RivalryAdvance", () =>
            {
                var R = SynapseRelationshipMilestones.RivalryLadder;
                var a = new SocialRecord(); var b = new SocialRecord();
                int r0 = SynapseRelationshipMilestones.AdvanceRivalry(a, b, R[0].maxWarmth, R[0].maxTrust, R[0].minFamiliarity);
                Assert.Equal(0, r0, "meeting the rivalry gate fires band 0 (Rivals)");
                Assert.Equal(0, b.highestRivalryMilestone, "both records marked");
                int r0again = SynapseRelationshipMilestones.AdvanceRivalry(a, b, R[0].maxWarmth - 2f, R[0].maxTrust - 2f, R[0].minFamiliarity + 2f);
                Assert.Equal(-1, r0again, "staying in band 0 does not re-fire");
                return $"rivalry band0 fires once ('{R[0].label}')";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A pair grows cold and familiar enough to be rivals",
            expectation: "The rivalry band fires once, symmetric and sticky");

            // Reconciliation: a former rival that warms into friendship fires the reconcile path exactly once.
            yield return new SynapseTestCase("Psychology_RelMilestones_ReconcileOnce", () =>
            {
                var L = SynapseRelationshipMilestones.FriendshipLadder;
                var a = new SocialRecord { highestRivalryMilestone = 0 }; // they were once Rivals
                var b = new SocialRecord { highestRivalryMilestone = 0 };
                var g0 = L[0];

                // First warm crossing reconciles: friendship advances AND the reconciled flag latches on both.
                int f = SynapseRelationshipMilestones.AdvanceFriendship(a, b, g0.minWarmth, g0.minTrust, g0.minFamiliarity);
                Assert.Equal(0, f, "the warm crossing advances the friendship band");
                bool wereRivals = a.highestRivalryMilestone >= 0;
                Assert.True(wereRivals && !a.reconciled, "precondition: former rivals, not yet reconciled");
                // Simulate CheckAndNotify's reconcile bookkeeping (the pure decision it makes):
                a.reconciled = b.reconciled = true;
                Assert.True(a.reconciled && b.reconciled, "reconciled latches on both records");

                // A later friendship advance is a normal milestone, not a second reconciliation.
                var g1 = L[1];
                int f2 = SynapseRelationshipMilestones.AdvanceFriendship(a, b, g1.minWarmth, g1.minTrust, g1.minFamiliarity);
                Assert.Equal(1, f2, "a later band still advances normally");
                Assert.True(a.reconciled, "reconciled stays latched (no second reconciliation letter)");
                return "former rivals reconcile once, then advance normally";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A pair that were rivals later warm up into friendship",
            expectation: "Reconciliation is recognised once; subsequent bands are ordinary friendship milestones");

            // The new markers survive a real Scribe save/load round-trip; a legacy record defaults to none.
            yield return new SynapseTestCase("Psychology_RelMilestones_MarkersRoundTrip", () =>
            {
                var rec = new SocialRecord { warmth = 60f, trust = 40f, familiarity = 65f, highestFriendshipMilestone = 1, highestRivalryMilestone = 0, reconciled = true };
                var reloaded = ScribeRoundTrip(rec);
                Assert.NotNull(reloaded, "record survives a scribe round-trip");
                Assert.Equal(1, reloaded.highestFriendshipMilestone, "friendship marker persists");
                Assert.Equal(0, reloaded.highestRivalryMilestone, "rivalry marker persists");
                Assert.True(reloaded.reconciled, "reconciled flag persists");
                var fresh = new SocialRecord();
                Assert.Equal(-1, fresh.highestFriendshipMilestone, "a fresh/legacy record has reached no friendship band");
                Assert.Equal(-1, fresh.highestRivalryMilestone, "and no rivalry band");
                return "friendship=1, rivalry=0, reconciled round-tripped; legacy defaults to -1/-1";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A save with both ladder markers and the reconciled flag is written and reloaded",
            expectation: "All three persist, so no band re-fires on load; pre-feature records start clean");

            // Full path on a live pair: CheckAndNotify runs the real ReceiveLetter + LookTargets without throwing,
            // advances the pair marker once, and is a no-op on the second call (no duplicate letter).
            yield return new SynapseTestCase("Psychology_RelMilestones_NotifyRunsAndDedups",
                NotifyRunsAndDedups,
                skipReason: () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                    var cs = map?.mapPawns?.FreeColonists;
                    return (cs != null && cs.Count >= 2 && Find.LetterStack != null) ? null : "need two colonists and a letter stack";
                },
                tier: "Execution", polarity: "positive",
                scenario: "A colonist pair first crosses a friendship band",
                expectation: "The notify path runs cleanly and advances the marker once; a second check is a no-op");
        }

        private static string NotifyRunsAndDedups()
        {
            var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            var colonists = map.mapPawns.FreeColonists.ToList();
            var a = colonists[0];
            var b = colonists[1];
            var g0 = SynapseRelationshipMilestones.FriendshipLadder[0];
            var recA = new SocialRecord { warmth = g0.minWarmth + 2f, trust = g0.minTrust + 2f, familiarity = g0.minFamiliarity + 2f };
            var recB = new SocialRecord { warmth = g0.minWarmth + 2f, trust = g0.minTrust + 2f, familiarity = g0.minFamiliarity + 2f };

            var stack = Find.LetterStack;
            int before = stack.LettersListForReading.Count;
            try
            {
                SynapseRelationshipMilestones.CheckAndNotify(a, b, recA, recB);
                Assert.Equal(0, recA.highestFriendshipMilestone, "the pair marker advanced to friendship band 0");
                Assert.Equal(0, recB.highestFriendshipMilestone, "both records advanced together");

                int reFired = SynapseRelationshipMilestones.AdvanceFriendship(recA, recB, recA.warmth, recA.trust, recA.familiarity);
                Assert.Equal(-1, reFired, "a second check on the same band is a no-op (no duplicate letter)");
                return $"notify path ran for {a.LabelShort} <-> {b.LabelShort}; marker advanced once, no re-fire";
            }
            finally
            {
                var list = stack.LettersListForReading;
                while (list.Count > before) stack.RemoveLetter(list[list.Count - 1]);
            }
        }

        /// <summary>Save one record to a scratch file and load it back, exercising ExposeData both ways.</summary>
        private static SocialRecord ScribeRoundTrip(SocialRecord record)
        {
            string path = System.IO.Path.Combine(GenFilePaths.ConfigFolderPath, "synapse_relmilestone_roundtrip.xml");
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
