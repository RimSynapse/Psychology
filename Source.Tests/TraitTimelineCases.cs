using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Comps;
using RimSynapse.Models;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.Models;
using RimSynapse.Utils;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// The Dynamic Trait History Timeline (#25). The engine used to erase a dynamic trait's history the
    /// moment it was removed; these cases pin the fix: removal now CLOSES the record (keeping the gain and
    /// its reasoning, adding the loss and ITS reasoning), the merged timeline orders and de-dupes both
    /// sources, an unchanged pawn yields the empty-history path, and a closed record survives save/load.
    /// </summary>
    [SynapseTestSet]
    public static class TraitTimelineCases
    {
        // A trait the fixture colony is unlikely to already carry, used only to exercise add-then-remove.
        // Whichever candidate the focus pawn lacks (in traits AND in dynamic-trait records) is chosen; if
        // none is free the live case skips rather than corrupt real gameplay state.
        private static readonly string[] ProbeTraitDefNames = { "Nimble", "Tough", "QuickSleeper", "Undergrounder" };

        public static IEnumerable<SynapseTestCase> All()
        {
            // Removing a dynamic trait must NOT erase its history: after add→remove the record is still
            // present, closed out (tickRemoved > 0) with BOTH reasons intact and in order. This is the
            // exact regression #25 was filed against (the old code called dynamicTraits.RemoveAll).
            yield return new SynapseTestCase("Psychology_TraitTimeline_RemovalPreservesHistory",
                RemovalPreservesHistory,
                skipReason: RemovalPreservesHistorySkip,
                tier: "Execution", polarity: "positive",
                scenario: "The engine adds a dynamic trait, then later removes it",
                expectation: "Both the gain and the loss survive with their reasoning; the record is closed, not deleted");

            // The merged view orders gains/losses/therapy memories oldest-first and drops exact duplicates.
            yield return new SynapseTestCase("Psychology_TraitTimeline_MergesAndOrders", () =>
            {
                var td = DefDatabase<TraitDef>.GetNamedSilentFail("Nimble") ?? DefDatabase<TraitDef>.AllDefsListForReading.First();

                var pawnComp = new SynapsePawnComp
                {
                    dynamicTraits = new List<DynamicTraitRecord>
                    {
                        // Gained at game-tick 100, lost at game-tick 500 — two rows, each with its reason.
                        new DynamicTraitRecord(td, 100, "the fields hardened them") { tickRemoved = 500, removalReason = "therapy softened them again" }
                    }
                };
                var coreComp = new SynapseCorePawnComp
                {
                    memories = new List<WeightedMemory>
                    {
                        // A desensitization "TraitShift" memory already stamped in ABSOLUTE ticks.
                        new WeightedMemory { summary = "Lost 'Wimp' to repeated combat.", absTick = SynapseDateHelper.GameTickToAbsTick(300), tags = new List<string> { "TraitShift" } },
                        // A non-TraitShift memory must be ignored by the timeline entirely.
                        new WeightedMemory { summary = "Ate a fine meal.", absTick = SynapseDateHelper.GameTickToAbsTick(200), tags = new List<string> { "Meal" } }
                    }
                };

                var timeline = TraitTimeline.Build(coreComp, pawnComp);

                Assert.Equal(3, timeline.Count, "gain + therapy-shift + loss = 3 rows; the non-TraitShift memory is excluded");
                Assert.Contains(timeline[0].text, "Gained", "oldest row (tick 100) is the gain");
                Assert.Contains(timeline[1].text, "Lost 'Wimp'", "middle row (tick 300) is the therapy shift");
                Assert.Contains(timeline[2].text, "therapy softened them again", "newest row (tick 500) is the loss WITH its removal reason");
                Assert.Contains(timeline[0].text, "the fields hardened them", "the gain keeps its original reasoning");
                bool ordered = timeline[0].absTick <= timeline[1].absTick && timeline[1].absTick <= timeline[2].absTick;
                Assert.True(ordered, "rows are ordered oldest-first");
                return $"3 rows ordered: {string.Join(" | ", timeline.Select(r => r.text.Substring(0, System.Math.Min(18, r.text.Length))))}";
            },
            tier: "Execution", polarity: "positive",
            scenario: "Engine gain/loss records and a therapy TraitShift memory coexist",
            expectation: "One chronological list, oldest-first, non-TraitShift memories excluded");

            // Exact duplicates across the two sources collapse to a single row.
            yield return new SynapseTestCase("Psychology_TraitTimeline_DedupesExactDuplicates", () =>
            {
                var td = DefDatabase<TraitDef>.GetNamedSilentFail("Nimble") ?? DefDatabase<TraitDef>.AllDefsListForReading.First();
                long absAdd = SynapseDateHelper.GameTickToAbsTick(100);
                string label = td.LabelCap.ToString();
                if (string.IsNullOrEmpty(label)) label = td.label;

                var pawnComp = new SynapsePawnComp
                {
                    dynamicTraits = new List<DynamicTraitRecord> { new DynamicTraitRecord(td, 100, null) }
                };
                var coreComp = new SynapseCorePawnComp
                {
                    // Same tick and identical text as the gain row -> must be de-duplicated.
                    memories = new List<WeightedMemory> { new WeightedMemory { summary = $"Gained '{label}'", absTick = absAdd, tags = new List<string> { "TraitShift" } } }
                };

                var timeline = TraitTimeline.Build(coreComp, pawnComp);
                Assert.Equal(1, timeline.Count, "identical (tick,text) rows from both sources collapse to one");
                return "duplicate collapsed to a single row";
            },
            tier: "Execution", polarity: "negative",
            scenario: "The same trait event is present in both the record list and a TraitShift memory",
            expectation: "The duplicate is dropped; the row appears once");

            // A pawn with no dynamic-trait changes and no TraitShift memories yields an empty timeline —
            // the data condition behind the "No major psychological changes recorded." UI message.
            yield return new SynapseTestCase("Psychology_TraitTimeline_EmptyWhenUnchanged", () =>
            {
                var pawnComp = new SynapsePawnComp { dynamicTraits = new List<DynamicTraitRecord>() };
                var coreComp = new SynapseCorePawnComp
                {
                    memories = new List<WeightedMemory> { new WeightedMemory { summary = "Just chatted.", absTick = 123L, tags = new List<string> { "Social" } } }
                };

                var timeline = TraitTimeline.Build(coreComp, pawnComp);
                Assert.Equal(0, timeline.Count, "no dynamic traits and no TraitShift memories -> empty timeline (shows the empty-history message)");
                return "empty timeline for an unchanged pawn";
            },
            tier: "Execution", polarity: "negative",
            scenario: "A pawn has had no trait gains or losses",
            expectation: "The timeline is empty (the 'No major psychological changes recorded.' path)");

            // A closed record (gain + loss, both reasons) survives a real Scribe save/load round-trip, so the
            // timeline persists across save/reload.
            yield return new SynapseTestCase("Psychology_TraitTimeline_RecordSurvivesSaveLoad", () =>
            {
                var td = DefDatabase<TraitDef>.GetNamedSilentFail("Nimble") ?? DefDatabase<TraitDef>.AllDefsListForReading.First();
                var original = new DynamicTraitRecord(td, 111, "gained for a reason") { tickRemoved = 222, removalReason = "lost for a reason" };

                var reloaded = ScribeRoundTrip(original);
                Assert.NotNull(reloaded, "record must survive a scribe round-trip");
                Assert.Equal(td, reloaded.traitDef, "traitDef must survive save/load");
                Assert.Equal(111, reloaded.tickAdded, "tickAdded must survive save/load");
                Assert.Equal(222, reloaded.tickRemoved, "tickRemoved must survive save/load (record stays closed)");
                Assert.Equal("gained for a reason", reloaded.reason, "gain reasoning must survive save/load");
                Assert.Equal("lost for a reason", reloaded.removalReason, "loss reasoning must survive save/load");
                Assert.False(reloaded.IsActive, "a closed record round-trips as closed");
                return "closed gain+loss record round-trips with both reasons intact";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A save is written and reloaded while a closed trait record is present",
            expectation: "The record round-trips with both ticks and both reasons preserved");
        }

        private static string RemovalPreservesHistorySkip()
        {
            var pawn = FocusColonist();
            if (pawn == null) return "no colonist available on the map";
            if (pawn.TryGetComp<SynapsePawnComp>() == null) return "focus colonist has no SynapsePawnComp";
            if (PickProbeTrait(pawn) == null) return "no free probe trait (all candidates already held or recorded)";
            return null;
        }

        private static string RemovalPreservesHistory()
        {
            var pawn = FocusColonist();
            var comp = pawn.TryGetComp<SynapsePawnComp>();
            var traitDef = PickProbeTrait(pawn);
            int before = comp.dynamicTraits.Count;

            try
            {
                SynapsePsychology.ApplyTraitDirective(pawn, traitDef.defName, true, "weeks in the fields soured them on rest");
                SynapsePsychology.ApplyTraitDirective(pawn, traitDef.defName, false, "a good night's sleep set them right");

                var rec = comp.dynamicTraits.LastOrDefault(r => r.traitDef == traitDef);
                Assert.NotNull(rec, "a record for the probe trait must exist after add");
                Assert.True(comp.dynamicTraits.Count(r => r.traitDef == traitDef) >= 1,
                    "removal must NOT delete the record (the #25 regression)");
                Assert.True(rec.tickAdded > 0, "the gain tick is stamped");
                Assert.True(rec.tickRemoved > 0, "the record is CLOSED on removal, not deleted");
                Assert.True(rec.tickRemoved >= rec.tickAdded, "loss cannot precede gain");
                Assert.False(rec.IsActive, "a removed trait's record is inactive");
                Assert.Equal("weeks in the fields soured them on rest", rec.reason, "the gain reasoning is preserved");
                Assert.Equal("a good night's sleep set them right", rec.removalReason, "the loss reasoning is preserved");
                Assert.False(pawn.story.traits.HasTrait(traitDef), "the trait itself is off the pawn after removal");
                return $"gain@{rec.tickAdded} closed@{rec.tickRemoved}, both reasons intact, trait removed from pawn";
            }
            finally
            {
                // Restore the live colony: drop any record we injected and strip the probe trait if it lingers.
                comp.dynamicTraits.RemoveAll(r => r.traitDef == traitDef && comp.dynamicTraits.IndexOf(r) >= before);
                var lingering = pawn.story?.traits?.GetTrait(traitDef);
                if (lingering != null) pawn.story.traits.RemoveTrait(lingering, true);
            }
        }

        private static Pawn FocusColonist()
        {
            var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
            return map?.mapPawns?.FreeColonists?.FirstOrDefault();
        }

        /// <summary>First probe trait the pawn neither carries nor has a dynamic record for, or null.</summary>
        private static TraitDef PickProbeTrait(Pawn pawn)
        {
            var comp = pawn.TryGetComp<SynapsePawnComp>();
            foreach (var name in ProbeTraitDefNames)
            {
                var td = DefDatabase<TraitDef>.GetNamedSilentFail(name);
                if (td == null) continue;
                if (pawn.story?.traits?.HasTrait(td) == true) continue;
                if (comp?.dynamicTraits?.Any(r => r.traitDef == td) == true) continue;
                return td;
            }
            return null;
        }

        /// <summary>Save one record to a scratch file and load it back, exercising ExposeData both ways.</summary>
        private static DynamicTraitRecord ScribeRoundTrip(DynamicTraitRecord record)
        {
            string path = System.IO.Path.Combine(GenFilePaths.ConfigFolderPath, "synapse_traittimeline_roundtrip.xml");
            try
            {
                var toSave = record;
                Scribe.saver.InitSaving(path, "test");
                try { Scribe_Deep.Look(ref toSave, "record"); }
                finally { Scribe.saver.FinalizeSaving(); }

                DynamicTraitRecord loaded = null;
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
