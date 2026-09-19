using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Psychology.API;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Therapy as a treatable illness (#17 redesign). Psychological conditions are per-pawn hediffs that never
    /// heal on their own; therapy applies a WEIGHTED quality (not a coin flip) that lowers severity, cures at
    /// zero, and a poor session sets back. These pin the treatment math + seed mapping. Severity/cure cases run
    /// live (a hediff needs a real pawn) and clean up; the seed-severity mapping is pure.
    /// </summary>
    [SynapseTestSet]
    public static class TherapyConditionCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // Seed severity scales with how deep the seeding trait runs (pure — just the def table).
            yield return new SynapseTestCase("Psychology_TherapyConditions_SeedSeverityByDegree", () =>
            {
                var natural = DefDatabase<TraitDef>.GetNamedSilentFail("NaturalMood");
                Assert.NotNull(natural, "NaturalMood trait def is loaded");
                float deep = SynapseTherapyConditions.SeedSeverity(new Trait(natural, -2));
                float mild = SynapseTherapyConditions.SeedSeverity(new Trait(natural, -1));
                Assert.True(deep > mild, $"a deeper trait seeds a worse condition ({mild:0.00} vs {deep:0.00})");
                Assert.True(deep <= 1f && mild > 0f, "seed severities stay in range");
                return $"degree -1 -> {mild:0.00}, degree -2 -> {deep:0.00}";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A condition is seeded from a trait of a given degree",
            expectation: "A deeper trait produces a more severe starting condition");

            // A run of good sessions (max quality) lowers severity monotonically and cures at zero.
            yield return new SynapseTestCase("Psychology_TherapyConditions_GoodSessionsCure",
                () => TreatTrajectory(quality: 1.0f, expectCure: true),
                skipReason: NeedColonistAndTraumaDef,
                tier: "Execution", polarity: "positive",
                scenario: "A colonist with a trauma condition receives several high-quality sessions",
                expectation: "Severity falls each session and the condition is cured (hediff removed) within a handful");

            // A poor session (low quality) is a setback — severity rises, not a coin flip that might help.
            yield return new SynapseTestCase("Psychology_TherapyConditions_PoorSessionSetsBack",
                () => PoorSession(),
                skipReason: NeedColonistAndTraumaDef,
                tier: "Execution", polarity: "negative",
                scenario: "A badly-judged session (very low quality) is applied to a condition",
                expectation: "The condition's severity rises — a poor session hurts rather than randomly helping");

            // Null-safe: no participant / no target never throws.
            yield return new SynapseTestCase("Psychology_TherapyConditions_NullSafe", () =>
            {
                Assert.True(!SynapseTherapyConditions.Treat(null, null, null, 1f), "null treat is a no-op returning false");
                Assert.Equal(0f, SynapseTherapyConditions.Quality(null, null, null), "null quality is 0");
                return "null participants handled";
            },
            tier: "Execution", polarity: "negative",
            scenario: "Treatment helpers are called with missing pawns",
            expectation: "No throw; treat returns false and quality returns 0");
        }

        private static string NeedColonistAndTraumaDef()
        {
            var cs = (Find.CurrentMap ?? Find.Maps?.FirstOrDefault())?.mapPawns?.FreeColonists;
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("Synapse_Hediff_Trauma");
            return (cs != null && cs.Count >= 1 && def != null) ? null : "need a colonist and the trauma hediff def";
        }

        private static string TreatTrajectory(float quality, bool expectCure)
        {
            var cs = (Find.CurrentMap ?? Find.Maps.FirstOrDefault()).mapPawns.FreeColonists.ToList();
            Pawn patient = cs[0];
            Pawn therapist = cs.Count > 1 ? cs[1] : cs[0];
            var def = DefDatabase<HediffDef>.GetNamed("Synapse_Hediff_Trauma");
            var h = HediffMaker.MakeHediff(def, patient); h.Severity = 0.8f; patient.health.AddHediff(h);
            try
            {
                float prev = h.Severity;
                int sessions = 0;
                while (patient.health.hediffSet.HasHediff(def) && sessions < 8)
                {
                    SynapseTherapyConditions.Treat(therapist, patient, h, quality);
                    sessions++;
                    float sev = patient.health.hediffSet.HasHediff(def) ? h.Severity : 0f;
                    Assert.True(sev <= prev + 0.0001f, $"severity did not increase on a good session ({prev:0.00}->{sev:0.00})");
                    prev = sev;
                }
                Assert.True(!patient.health.hediffSet.HasHediff(def), "the condition was cured (hediff removed)");
                Assert.True(sessions <= 6, $"cured within a handful of sessions (took {sessions})");
                return $"cured in {sessions} sessions";
            }
            finally
            {
                var left = patient.health.hediffSet.GetFirstHediffOfDef(def);
                while (left != null) { patient.health.RemoveHediff(left); left = patient.health.hediffSet.GetFirstHediffOfDef(def); }
            }
        }

        private static string PoorSession()
        {
            var cs = (Find.CurrentMap ?? Find.Maps.FirstOrDefault()).mapPawns.FreeColonists.ToList();
            Pawn patient = cs[0];
            Pawn therapist = cs.Count > 1 ? cs[1] : cs[0];
            var def = DefDatabase<HediffDef>.GetNamed("Synapse_Hediff_Trauma");
            var h = HediffMaker.MakeHediff(def, patient); h.Severity = 0.4f; patient.health.AddHediff(h);
            try
            {
                float before = h.Severity;
                SynapseTherapyConditions.Treat(therapist, patient, h, 0.1f);
                Assert.True(h.Severity > before, $"a poor session worsened the condition ({before:0.00}->{h.Severity:0.00})");
                return $"setback {before:0.00} -> {h.Severity:0.00}";
            }
            finally
            {
                var left = patient.health.hediffSet.GetFirstHediffOfDef(def);
                while (left != null) { patient.health.RemoveHediff(left); left = patient.health.hediffSet.GetFirstHediffOfDef(def); }
            }
        }
    }
}
