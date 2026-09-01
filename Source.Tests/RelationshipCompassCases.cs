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
