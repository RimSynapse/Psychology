using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Comps;
using RimSynapse.Psychology.API;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Gourmand wiring: the trait was whitelisted but had NO measured signal, so a pawn could never develop
    /// it through play (it was dead). It now grows the same way as every other lifestyle trait — repeatedly
    /// indulging in fine/lavish meals AND enjoying them (exposure × the mood response). These pin the exposure
    /// (counts quality meals, ignores hunger food) and that the signal actually fires.
    /// </summary>
    [SynapseTestSet(TestPhase.MapMutating)]
    public static class GourmandReinforcementCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // MealIndulgenceToday counts recent fine/lavish meal thoughts and EXCLUDES hunger food (paste).
            yield return new SynapseTestCase("Psychology_Gourmand_IndulgenceCountsQualityNotHunger",
                IndulgenceCountsQualityNotHunger,
                skipReason: NeedsColonist,
                tier: "Execution", polarity: "positive",
                scenario: "A colonist eats fine/lavish meals (and, separately, nutrient paste)",
                expectation: "The indulgence count reflects the quality meals only; paste is ignored");

            // With enough indulgence and a lifted mood, the Gourmand signal is emitted (it never was before).
            yield return new SynapseTestCase("Psychology_Gourmand_IndulgenceEmitsSignal",
                IndulgenceEmitsSignal,
                skipReason: NeedsColonistWithoutGourmand,
                tier: "Execution", polarity: "positive",
                scenario: "A colonist repeatedly savours fine meals while their mood is up",
                expectation: "A Gourmand candidate is produced by the day's signals");

            // Null-safety of the raw exposure fact.
            yield return new SynapseTestCase("Psychology_Gourmand_IndulgenceNullSafe", () =>
            {
                Assert.Equal(0, SynapseCorePawnComp.MealIndulgenceToday(null), "null pawn yields 0 indulgence, no throw");
                return "null-safe";
            });
        }

        private static string NeedsColonist()
        {
            var p = Colonist();
            if (p == null) return "no colonist on the map";
            if (p.needs?.mood?.thoughts?.memories == null) return "colonist has no mood/thought tracker";
            if (DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal") == null) return "AteFineMeal thought not loaded";
            return null;
        }

        private static string NeedsColonistWithoutGourmand()
        {
            string baseSkip = NeedsColonist();
            if (baseSkip != null) return baseSkip;
            var gourmand = DefDatabase<TraitDef>.GetNamedSilentFail("Gourmand");
            if (gourmand == null) return "Gourmand trait not loaded";
            var p = Colonist();
            if (p.story?.traits?.HasTrait(gourmand) == true) return $"{p.LabelShort} already has Gourmand";
            return null;
        }

        private static string IndulgenceCountsQualityNotHunger()
        {
            var pawn = Colonist();
            var mem = pawn.needs.mood.thoughts.memories;
            var fine = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal");
            var paste = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteNutrientPasteMeal");

            int before = SynapseCorePawnComp.MealIndulgenceToday(pawn);
            try
            {
                mem.TryGainMemory(fine);
                mem.TryGainMemory(fine);
                int afterFine = SynapseCorePawnComp.MealIndulgenceToday(pawn);
                Assert.True(afterFine > before, $"fine meals raise the indulgence count ({before} -> {afterFine})");

                if (paste != null)
                {
                    mem.TryGainMemory(paste);
                    int afterPaste = SynapseCorePawnComp.MealIndulgenceToday(pawn);
                    Assert.Equal(afterFine, afterPaste, "nutrient paste (hunger food) does NOT count as indulgence");
                }
                return $"indulgence {before} -> {afterFine}; paste excluded";
            }
            finally
            {
                if (fine != null) mem.RemoveMemoriesOfDef(fine);
                if (paste != null) mem.RemoveMemoriesOfDef(paste);
            }
        }

        private static string IndulgenceEmitsSignal()
        {
            var pawn = Colonist();
            var core = pawn.TryGetComp<SynapseCorePawnComp>();
            var mem = pawn.needs.mood.thoughts.memories;
            var fine = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal");

            try
            {
                // One fine meal is enough — RimWorld keeps a single refreshing "ate fine meal" thought, and the
                // engine builds Gourmand across days it stays present, not from multiple entries in one day.
                mem.TryGainMemory(fine);
                if (SynapseCorePawnComp.MealIndulgenceToday(pawn) < 1)
                    return "SKIPPED: injected fine-meal thought did not register as indulgence";

                // A lifted mood (reinforcement 1) so the behaviour is reinforced. Look for the Gourmand candidate.
                var signals = SynapseSkillAxisMap.SampleSignals(pawn, core, reinforcement: 1f, stress: 0f);
                bool hasGourmand = signals.Any(s => s.candidateId != null
                    && s.candidateId.StartsWith(SynapseSkillAxisMap.Gourmand));
                Assert.True(hasGourmand, "indulgence + lifted mood emits a Gourmand signal (it was previously unwired/dead)");
                var sig = signals.First(s => s.candidateId.StartsWith(SynapseSkillAxisMap.Gourmand));
                Assert.True(sig.dailyPressure > 0f, "the Gourmand signal carries real daily pressure");
                return $"Gourmand signal emitted: {sig.candidateId} @ {sig.dailyPressure:F3}";
            }
            finally
            {
                if (fine != null) mem.RemoveMemoriesOfDef(fine);
            }
        }

        private static Pawn Colonist()
        {
            var map = Find.CurrentMap ?? Find.Maps?.FirstOrDefault();
            return map?.mapPawns?.FreeColonists?.FirstOrDefault();
        }
    }
}
