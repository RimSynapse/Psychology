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
