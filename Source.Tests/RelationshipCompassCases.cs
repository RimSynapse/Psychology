using System.Collections.Generic;
using System.Linq;
using Verse;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.Models;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// The relationship compass foundation (#72): the two independent axes (trust/respect, warmth) plus the
    /// symmetric award path every trust/warmth source funnels through. These pin the new warmth field's
    /// persistence and that a single award moves BOTH sides of a relationship by the same amount.
    /// </summary>
    [SynapseTestSet]
    public static class RelationshipCompassCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // The new warmth axis persists alongside trust/familiarity/marker.
            yield return new SynapseTestCase("Psychology_Compass_WarmthRoundTrips", () =>
            {
                var rec = new SocialRecord { trust = 30f, warmth = -45f, familiarity = 60f, highestFamiliarityMilestone = 1 };
                var reloaded = ScribeRoundTrip(rec);
                Assert.NotNull(reloaded, "record survives a scribe round-trip");
                Assert.True(System.Math.Abs(reloaded.warmth + 45f) < 0.001f, "warmth survives save/load");
                Assert.True(System.Math.Abs(reloaded.trust - 30f) < 0.001f, "trust survives too");
                Assert.True(System.Math.Abs(reloaded.familiarity - 60f) < 0.001f, "familiarity survives too");
                return $"warmth {reloaded.warmth:F0}, trust {reloaded.trust:F0} round-tripped";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A save is written with a relationship that has both compass axes set",
            expectation: "Both warmth and trust persist independently");

            // AddWarmth / AddTrust clamp to their ranges (warmth is signed like trust, not 0-floored like familiarity).
            yield return new SynapseTestCase("Psychology_Compass_AxesClampToRange", () =>
            {
                var r = new SocialRecord();
                r.AddWarmth(-500f);
                Assert.True(r.warmth >= -100f, "warmth clamps at -100 (can go negative — dislike)");
                r.AddWarmth(1000f);
                Assert.True(r.warmth <= 100f, "warmth clamps at +100");
                return $"warmth clamped to [{-100},{100}]";
            },
            tier: "Execution", polarity: "positive",
            scenario: "Warmth is pushed past its bounds",
            expectation: "It clamps to [-100, 100], and unlike familiarity it may be negative");

            // A single award moves BOTH directed records equally, creating them on demand; self/null are no-ops.
            yield return new SynapseTestCase("Psychology_Compass_AwardIsSymmetric",
                AwardIsSymmetric,
                skipReason: () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                    var cs = map?.mapPawns?.FreeColonists;
                    return (cs != null && cs.Count >= 2) ? null : "need two colonists";
                },
                tier: "Execution", polarity: "positive",
                scenario: "A trust/warmth source awards a pair",
                expectation: "Both sides of the relationship move by the same amount; self/null award nothing");

            // Directed award moves ONLY one side — the asymmetry jealousy/grudges need (A resents B; B oblivious).
            yield return new SynapseTestCase("Psychology_Compass_DirectedAwardIsOneSided",
                DirectedAwardIsOneSided,
                skipReason: () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                    var cs = map?.mapPawns?.FreeColonists;
                    return (cs != null && cs.Count >= 2) ? null : "need two colonists";
                },
                tier: "Execution", polarity: "positive",
                scenario: "A resents B (jealousy) while B is oblivious",
                expectation: "Only A→B trust/warmth moves; B→A is untouched");

            // The compulsion gate: a feeling only erupts into action when it's strong AND the pawn is volatile.
            yield return new SynapseTestCase("Psychology_Compulsion_GateNeedsStrongFeelingAndLowControl", () =>
            {
                // Max feeling, fully volatile -> acts. Max feeling, controlled -> suppressed. Weak feeling -> never.
                Assert.True(SynapseCompulsion.WouldActOn(1.0f, 0.0f), "a strong feeling in a volatile pawn erupts");
                Assert.False(SynapseCompulsion.WouldActOn(1.0f, 0.9f), "the same strong feeling in a controlled pawn is suppressed");
                Assert.False(SynapseCompulsion.WouldActOn(0.3f, 0.0f), "a weak feeling never erupts, even in a volatile pawn");
                Assert.True(System.Math.Abs(SynapseCompulsion.Drive(1f, 0f) - 1f) < 0.001f, "drive is max when volatile + strong");
                Assert.True(System.Math.Abs(SynapseCompulsion.Drive(1f, 1f)) < 0.001f, "drive is zero when fully controlled");
                return "gate: strong+volatile acts; controlled or weak does not";
            },
            tier: "Execution", polarity: "positive",
            scenario: "Two pawns feel the same resentment; one is volatile, one is controlled",
            expectation: "Only the volatile pawn acts on it; a weak feeling never acts");

            // Effective control uses the LLM-stored override when set, else the deterministic baseline; clamped.
            yield return new SynapseTestCase("Psychology_Compulsion_EffectiveUsesOverrideElseBaseline", () =>
            {
                Assert.True(System.Math.Abs(SynapseCompulsion.Effective(null, new SynapsePawnComp { compulsionControl = 0.8f }) - 0.8f) < 0.001f,
                    "a stored (LLM-refined) value is used verbatim");
                Assert.True(System.Math.Abs(SynapseCompulsion.Effective(null, new SynapsePawnComp { compulsionControl = 5f }) - 1f) < 0.001f,
                    "a stored value is clamped to [0,1]");
                Assert.True(System.Math.Abs(SynapseCompulsion.Effective(null, new SynapsePawnComp()) - 0.5f) < 0.001f,
                    "the sentinel -1 falls back to the baseline (0.5 with no mental breaker)");
                return "override honoured; -1 sentinel falls back to baseline";
            },
            tier: "Execution", polarity: "positive",
            scenario: "The LLM eval has (or hasn't) refined a pawn's temperament",
            expectation: "Triggers read the stored value when present, else the trait baseline");

            // Baseline from real pawns stays in range and reflects volatility (steadier pawns score higher).
            yield return new SynapseTestCase("Psychology_Compulsion_BaselineInRange",
                () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
                    var colonists = map.mapPawns.FreeColonists.ToList();
                    foreach (var c in colonists)
                    {
                        float ctrl = SynapseCompulsion.Baseline(c);
                        Assert.True(ctrl >= 0f && ctrl <= 1f, $"{c.LabelShort} control baseline is in [0,1] (got {ctrl:F2})");
                    }
                    return $"{colonists.Count} colonist control baseline(s) in range";
                },
                skipReason: () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                    var cs = map?.mapPawns?.FreeColonists;
                    return (cs != null && cs.Count >= 1) ? null : "need a colonist";
                },
                tier: "Execution", polarity: "positive",
                scenario: "Real colonists' control is derived from their stability traits",
                expectation: "Every pawn's baseline is a valid [0,1] control value");

            // Shared victory: a drafted colleague near the kill bonds (trust) with the killer; null/empty is safe.
            yield return new SynapseTestCase("Psychology_Compass_SharedVictoryBondsNearbyFighters",
                SharedVictoryBondsNearbyFighters,
                skipReason: () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                    var cs = map?.mapPawns?.FreeColonists?.Where(c => c.drafter != null).ToList();
                    return (cs != null && cs.Count >= 2) ? null : "need two draftable colonists";
                },
                tier: "Execution", polarity: "positive",
                scenario: "A colonist fells a threat while a colleague fights nearby",
                expectation: "The nearby drafted colleague gains trust with the killer; null/empty inputs are safe");

            // The heart of #72's continuous compatibility: familiarity desensitises a clash you like, hypersensitises one you don't.
            yield return new SynapseTestCase("Psychology_Compat_FamiliarityDesensitisesOrHypersensitises", () =>
            {
                float rate = SynapseCompatibility.DriftRate;
                // Positive compatibility is steady — liking someone who fits isn't modulated by familiarity/valence.
                Assert.True(System.Math.Abs(SynapseCompatibility.EffectivePull(0.5f, 100f, 50f) - 0.5f * rate) < 0.001f, "kinship pull is steady");
                Assert.True(System.Math.Abs(SynapseCompatibility.EffectivePull(0.5f, 0f, -50f) - 0.5f * rate) < 0.001f, "kinship pull ignores familiarity/valence");
                // A clash, unfamiliar: full bite, no modulation.
                Assert.True(System.Math.Abs(SynapseCompatibility.EffectivePull(-0.5f, 0f, 0f) - (-0.5f * rate)) < 0.001f, "an unfamiliar clash bites fully");
                // A clash you currently LIKE + very familiar → desensitised (dampened).
                float desens = SynapseCompatibility.EffectivePull(-0.5f, 100f, 20f);
                Assert.True(desens > -0.5f * rate && desens < 0f, $"a valued, familiar clash is dampened (got {desens:F2} vs full {-0.5f*rate:F2})");
                // A clash you currently DISLIKE + very familiar → hypersensitised (amplified beyond full).
                float hyper = SynapseCompatibility.EffectivePull(-0.5f, 100f, -20f);
                Assert.True(hyper < -0.5f * rate, $"a resented, familiar clash grates harder than full (got {hyper:F2})");
                return $"kinship steady; clash bite: unfamiliar {-0.5f*rate:F2}, liked/familiar {desens:F2}, disliked/familiar {hyper:F2}";
            },
            tier: "Execution", polarity: "positive",
            scenario: "The same personality clash, between friends vs between enemies, as familiarity grows",
            expectation: "Friends grow to overlook it; enemies grow to resent it more");

            // Compatibility is symmetric and bounded on real pawns, and recomputed live (not frozen).
            yield return new SynapseTestCase("Psychology_Compat_ScoreSymmetricAndInRange",
                () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
                    var cs = map.mapPawns.FreeColonists.ToList();
                    for (int i = 0; i < cs.Count; i++)
                        for (int j = i + 1; j < cs.Count; j++)
                        {
                            float ab = SynapseCompatibility.Score(cs[i], cs[j]);
                            float ba = SynapseCompatibility.Score(cs[j], cs[i]);
                            Assert.True(ab >= -1f && ab <= 1f, $"compat in [-1,1] (got {ab:F2})");
                            Assert.True(System.Math.Abs(ab - ba) < 0.001f, "compat is symmetric");
                        }
                    return $"{cs.Count} colonist(s): all pair compat values symmetric and in range";
                },
                skipReason: () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                    var cs = map?.mapPawns?.FreeColonists;
                    return (cs != null && cs.Count >= 2) ? null : "need two colonists";
                },
                tier: "Execution", polarity: "positive",
                scenario: "Real colonists' personalities are compared",
                expectation: "Compatibility is symmetric and within [-1, 1]");

            // The tough gate: rivalry/betrayal only qualify when the relationship's state earns them.
            yield return new SynapseTestCase("Psychology_RelReview_RivalryAndBetrayalGates", () =>
            {
                // Betrayal needs prior trust AND a real breach this night.
                Assert.True(SynapseRelationshipReview.QualifiesAsBetrayal(30f, -20f), "a real breach of a trusted bond is betrayal");
                Assert.False(SynapseRelationshipReview.QualifiesAsBetrayal(10f, -20f), "you can't betray someone who didn't trust you");
                Assert.False(SynapseRelationshipReview.QualifiesAsBetrayal(30f, -5f), "a minor slight isn't betrayal");
                // Rivalry needs sustained, deep, familiar antagonism.
                Assert.True(SynapseRelationshipReview.QualifiesAsRivalry(-60f, 70f, -10f), "deep cold + well-known + no trust = rivalry");
                Assert.False(SynapseRelationshipReview.QualifiesAsRivalry(-60f, 20f, 0f), "you can't have a rivalry with someone you barely know");
                Assert.False(SynapseRelationshipReview.QualifiesAsRivalry(-10f, 70f, 0f), "mild dislike isn't rivalry");
                return "betrayal/rivalry gates hold";
            },
            tier: "Execution", polarity: "negative",
            scenario: "The eval proposes rivalry or betrayal",
            expectation: "It only qualifies with a genuinely earned relationship state, never over something small");

            // Parsing tolerates garbage and reads a well-formed result.
            yield return new SynapseTestCase("Psychology_RelReview_ParseTolerant", () =>
            {
                Assert.True(SynapseRelationshipReview.Parse(null) == null, "null content parses to null, no throw");
                Assert.True(SynapseRelationshipReview.Parse("not json at all") == null, "garbage parses to null");
                var r = SynapseRelationshipReview.Parse("{\"compulsionControl\":0.2,\"relationships\":[{\"who\":\"Bob\",\"warmthDelta\":-6,\"trustDelta\":-3,\"reason\":\"x\",\"kind\":\"jealousy\"}]}");
                Assert.NotNull(r, "well-formed JSON parses");
                Assert.True(r.compulsionControl.HasValue && System.Math.Abs(r.compulsionControl.Value - 0.2f) < 0.001f, "compulsionControl read");
                Assert.Equal(1, r.relationships.Count, "one relationship read");
                Assert.Equal("Bob", r.relationships[0].who, "target read");
                return "parse: null/garbage safe; valid read";
            },
            tier: "Execution", polarity: "positive",
            scenario: "The eval returns JSON (or garbage)",
            expectation: "Well-formed results parse; malformed ones return null without throwing");

            // Apply writes control, applies bounded DIRECTED deltas, and downgrades an unearned rivalry to friction.
            yield return new SynapseTestCase("Psychology_RelReview_ApplyBoundedDirectedAndGated",
                ApplyBoundedDirectedAndGated,
                skipReason: () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                    var cs = map?.mapPawns?.FreeColonists;
                    return (cs != null && cs.Count >= 2) ? null : "need two colonists";
                },
                tier: "Execution", polarity: "positive",
                scenario: "A parsed review is applied to a colonist",
                expectation: "Control is written; deltas apply directed and bounded; an unearned rivalry becomes friction");

            // The nightly review gathers + builds its prompt without throwing, and fires at most once a day.
            yield return new SynapseTestCase("Psychology_RelReview_QueuesOncePerDay",
                QueuesOncePerDay,
                skipReason: () =>
                {
                    var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
                    var cs = map?.mapPawns?.FreeColonists;
                    return (cs != null && cs.Count >= 2) ? null : "need two colonists";
                },
                tier: "Execution", polarity: "positive",
                scenario: "A colonist with a significant relationship has their nightly review fired",
                expectation: "It builds and queues once; a second call the same day is a no-op");

            // Conversion driver (the C# hour-to-hour half): faith only erodes with BOTH the LLM gate open AND fond
            // ties to the colony's faith. Either at zero → no drift, so nobody converts by accident.
            yield return new SynapseTestCase("Psychology_Conversion_CertaintyErosionNeedsGateAndBond", () =>
            {
                float b = SynapseConversion.BaseErosionPerDay;
                Assert.True(System.Math.Abs(SynapseConversion.CertaintyErosion(1f, 1f) - b) < 0.0001f, "full warmth + full openness = a full day's erosion");
                Assert.Equal(0f, SynapseConversion.CertaintyErosion(1f, 0f), "an unshakeable pawn (susceptibility 0) never drifts, however many friends they make");
                Assert.Equal(0f, SynapseConversion.CertaintyErosion(0f, 1f), "an open pawn with no fond ties to the faith doesn't drift");
                Assert.True(System.Math.Abs(SynapseConversion.CertaintyErosion(0.5f, 0.5f) - b * 0.25f) < 0.0001f, "erosion is warmth × susceptibility");
                return $"erosion = base({b:F2}) × warmth × susceptibility; both gates required";
            },
            tier: "Execution", polarity: "positive",
            scenario: "The LLM sets a susceptibility gate; C# drives certainty each day",
            expectation: "Faith drifts only when the pawn is both open AND fond of the colony's faithful");
        }

        private static string QueuesOncePerDay()
        {
            var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            var colonists = map.mapPawns.FreeColonists.ToList();
            var a = colonists[0];
            var b = colonists[1];
            var compA = a.GetComp<SynapsePawnComp>();
            string idB = b.GetUniqueLoadID();
            compA.socialNetwork.TryGetValue(idB, out var prior);
            int day0 = compA.lastRelationshipReviewDay;
            try
            {
                // Ensure a significant relationship exists so the review has something to reconsider.
                var rec = prior ?? new SocialRecord();
                if (prior == null) compA.socialNetwork[idB] = rec;
                rec.familiarity = System.Math.Max(rec.familiarity, 40f);
                compA.lastRelationshipReviewDay = -1;

                bool first = RimSynapse.Psychology.API.SynapsePsychology.QueueRelationshipReview(a);
                Assert.True(first, "the review builds and queues for a pawn with a significant relationship");
                Assert.Equal(RimWorld.GenDate.DaysPassed, compA.lastRelationshipReviewDay, "the review is marked done for today");
                bool second = RimSynapse.Psychology.API.SynapsePsychology.QueueRelationshipReview(a);
                Assert.False(second, "a second call the same day is a no-op (once/day)");
                return "queued once; second same-day call skipped";
            }
            finally
            {
                compA.lastRelationshipReviewDay = day0;
                if (prior == null) compA.socialNetwork.Remove(idB);
            }
        }

        private static string ApplyBoundedDirectedAndGated()
        {
            var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            var colonists = map.mapPawns.FreeColonists.ToList();
            var a = colonists[0];
            var b = colonists[1];
            var compA = a.GetComp<SynapsePawnComp>();
            var compB = b.GetComp<SynapsePawnComp>();
            string idB = b.GetUniqueLoadID(), idA = a.GetUniqueLoadID();
            compA.socialNetwork.TryGetValue(idB, out var priorAB);
            compB.socialNetwork.TryGetValue(idA, out var priorBA);
            float ctrl0 = compA.compulsionControl;
            float w0AB = priorAB?.warmth ?? 0f, t0AB = priorAB?.trust ?? 0f;
            try
            {
                // Fresh pair (low familiarity), so a proposed "rivalry" must NOT qualify -> downgraded to friction.
                var result = new RelationshipReviewResult
                {
                    compulsionControl = 0.25f,
                    relationships = new List<RelationshipDelta>
                    {
                        new RelationshipDelta { who = "TARGET", warmthDelta = -100f, trustDelta = -4f, reason = "cold read", kind = "rivalry" }
                    }
                };
                var applied = SynapseRelationshipReview.Apply(a, result, _ => b);
                Assert.Equal(1, applied.Count, "one relationship applied");
                Assert.Equal("friction", applied[0], "an unearned rivalry (fresh pair) is downgraded to friction");
                Assert.True(System.Math.Abs(compA.compulsionControl - 0.25f) < 0.001f, "the LLM-refined compulsion control is stored");

                var recAB = compA.socialNetwork[idB];
                Assert.True(System.Math.Abs(recAB.warmth - ((priorAB?.warmth ?? 0f) - SynapseRelationshipReview.MaxNightlyDelta)) < 0.001f,
                    "warmth delta is clamped to the nightly bound (-12), not -100");
                // Directed: B's record toward A is untouched by A's review.
                float bToAWarmth = compB.socialNetwork.TryGetValue(idA, out var recBA) ? recBA.warmth : (priorBA?.warmth ?? 0f);
                Assert.True(System.Math.Abs(bToAWarmth - (priorBA?.warmth ?? 0f)) < 0.001f, "B→A is untouched — the review is one-sided");
                return "control stored; warmth clamped to -12; rivalry gated to friction; directed";
            }
            finally
            {
                compA.compulsionControl = ctrl0;
                if (priorAB != null)
                {
                    priorAB.warmth = w0AB; priorAB.trust = t0AB;
                    priorAB.relationshipMemories.Remove("cold read");
                }
                else compA.socialNetwork.Remove(idB);
                if (priorBA == null) compB.socialNetwork.Remove(idA);
            }
        }

        private static string SharedVictoryBondsNearbyFighters()
        {
            Assert.Equal(0, SynapseRelationships.AwardSharedVictory(null, null, default, 18f, 2f), "null inputs bond nobody, no throw");

            var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            var colonists = map.mapPawns.FreeColonists.Where(c => c.drafter != null).ToList();
            var killer = colonists[0];
            var ally = colonists[1];
            var compK = killer.GetComp<SynapsePawnComp>();
            string idAlly = ally.GetUniqueLoadID();
            compK.socialNetwork.TryGetValue(idAlly, out var prior);
            float t0 = prior?.trust ?? 0f;
            bool wasDrafted = ally.drafter.Drafted;
            try
            {
                ally.drafter.Drafted = true; // the colleague is in the fight
                // Evaluate the shared victory at the ally's own cell, so distance is 0 and only the "drafted +
                // near" filter is under test (not pawn placement).
                int bonded = SynapseRelationships.AwardSharedVictory(killer, map, ally.Position, 18f, 5f);
                Assert.True(bonded >= 1, $"at least the nearby drafted ally bonds (got {bonded})");
                float after = compK.socialNetwork[idAlly].trust;
                Assert.True(System.Math.Abs((after - t0) - 5f) < 0.001f, "the killer's trust with the drafted ally rose by 5");
                return $"{bonded} fighter(s) bonded; killer↔ally trust +5";
            }
            finally
            {
                ally.drafter.Drafted = wasDrafted;
                if (prior != null) prior.trust = t0; else compK.socialNetwork.Remove(idAlly);
                var allyComp = ally.GetComp<SynapsePawnComp>();
                string idKiller = killer.GetUniqueLoadID();
                if (allyComp != null) { allyComp.socialNetwork.TryGetValue(idKiller, out var back); if (back != null && back.trust == 5f && t0 == 0f) allyComp.socialNetwork.Remove(idKiller); }
            }
        }

        private static string DirectedAwardIsOneSided()
        {
            var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            var colonists = map.mapPawns.FreeColonists.ToList();
            var a = colonists[0];
            var b = colonists[1];
            var compA = a.GetComp<SynapsePawnComp>();
            var compB = b.GetComp<SynapsePawnComp>();
            string idA = a.GetUniqueLoadID(), idB = b.GetUniqueLoadID();

            compA.socialNetwork.TryGetValue(idB, out var priorA);
            compB.socialNetwork.TryGetValue(idA, out var priorB);
            float t0a = priorA?.trust ?? 0f, t0b = priorB?.trust ?? 0f;
            try
            {
                SynapseRelationships.AwardTrustDirected(a, b, -8f); // A's jealous resentment of B
                var recA = compA.socialNetwork[idB];
                Assert.True(System.Math.Abs((recA.trust - t0a) + 8f) < 0.001f, "A→B trust soured by 8");
                float bToA = compB.socialNetwork.TryGetValue(idA, out var recB) ? recB.trust : t0b;
                Assert.True(System.Math.Abs(bToA - t0b) < 0.001f, "B→A trust is untouched — B doesn't know");
                return "A's resentment moved only A→B; B unaffected";
            }
            finally
            {
                if (priorA != null) priorA.trust = t0a; else compA.socialNetwork.Remove(idB);
                if (priorB == null) compB.socialNetwork.Remove(idA);
            }
        }

        private static string AwardIsSymmetric()
        {
            var map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
            var colonists = map.mapPawns.FreeColonists.ToList();
            var a = colonists[0];
            var b = colonists[1];
            var compA = a.GetComp<SynapsePawnComp>();
            var compB = b.GetComp<SynapsePawnComp>();
            string idA = a.GetUniqueLoadID(), idB = b.GetUniqueLoadID();

            // Snapshot so we can restore the live colony's records afterward.
            compA.socialNetwork.TryGetValue(idB, out var priorA);
            compB.socialNetwork.TryGetValue(idA, out var priorB);
            float t0a = priorA?.trust ?? 0f, t0b = priorB?.trust ?? 0f;
            float w0a = priorA?.warmth ?? 0f, w0b = priorB?.warmth ?? 0f;
            try
            {
                SynapseRelationships.AwardTrust(a, b, 5f);
                SynapseRelationships.AwardWarmth(a, b, -4f);
                var recA = compA.socialNetwork[idB];
                var recB = compB.socialNetwork[idA];
                Assert.True(System.Math.Abs((recA.trust - t0a) - 5f) < 0.001f, "A→B trust rose by 5");
                Assert.True(System.Math.Abs((recB.trust - t0b) - 5f) < 0.001f, "B→A trust rose by 5 (symmetric)");
                Assert.True(System.Math.Abs((recA.warmth - w0a) + 4f) < 0.001f, "A→B warmth fell by 4");
                Assert.True(System.Math.Abs((recB.warmth - w0b) + 4f) < 0.001f, "B→A warmth fell by 4 (symmetric)");

                // Self and null award nothing (no throw).
                SynapseRelationships.AwardTrust(a, a, 100f);
                SynapseRelationships.AwardTrust(a, null, 100f);
                Assert.True(System.Math.Abs((compA.socialNetwork[idB].trust - t0a) - 5f) < 0.001f, "self/null awards changed nothing");
                return "trust +5 and warmth -4 applied symmetrically to both records";
            }
            finally
            {
                if (priorA != null) { priorA.trust = t0a; priorA.warmth = w0a; } else compA.socialNetwork.Remove(idB);
                if (priorB != null) { priorB.trust = t0b; priorB.warmth = w0b; } else compB.socialNetwork.Remove(idA);
            }
        }

        private static SocialRecord ScribeRoundTrip(SocialRecord record)
        {
            string path = System.IO.Path.Combine(GenFilePaths.ConfigFolderPath, "synapse_compass_roundtrip.xml");
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
