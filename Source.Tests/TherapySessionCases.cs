using System.Collections.Generic;
using System.Linq;
using Verse;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Comps;
using RimSynapse.Comps;
using RimSynapse.Models;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Therapy session auto-modes (#17). The mode selection and the LLM dialogue are UI/live-model concerns;
    /// the DETERMINISTIC backbone that every mode (Guiding Hand / Watch / Background) converges on is
    /// <see cref="SynapseTherapy.RecordSession"/> — on completion both participants gain a Therapy-tagged
    /// memory and a transcript. These cases pin that: both-pawns record on success and failure, the tags, and
    /// null-safety. They run live (AddMemory needs real comps) and clean up the state they add.
    /// </summary>
    [SynapseTestSet]
    public static class TherapySessionCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Psychology_TherapyAutoModes_RecordsBothPawns",
                () => RecordsBoth(success: true, expectRoleTag: "Helped"),
                skipReason: NeedTwoColonists,
                tier: "Execution", polarity: "positive",
                scenario: "A therapy session completes successfully (any mode)",
                expectation: "Both therapist and patient gain a Therapy memory + transcript; tags reflect the roles and a helpful outcome");

            yield return new SynapseTestCase("Psychology_TherapyAutoModes_AwkwardStillRecords",
                () => RecordsBoth(success: false, expectRoleTag: "Awkward"),
                skipReason: NeedTwoColonists,
                tier: "Execution", polarity: "negative",
                scenario: "A therapy session completes but goes awkwardly",
                expectation: "It is still recorded on both participants, tagged Awkward — a failed session is not a no-op");

            yield return new SynapseTestCase("Psychology_TherapyAutoModes_NullSafe", () =>
            {
                SynapseTherapy.RecordSession(null, null, true);   // both null -> no throw
                var one = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault();
                SynapseTherapy.RecordSession(one, null, true);    // one null -> no throw
                return "null participants handled without throwing";
            },
            tier: "Execution", polarity: "negative",
            scenario: "RecordSession is called with a missing participant",
            expectation: "No throw and no record written");
        }

        private static string NeedTwoColonists()
        {
            var cs = (Find.CurrentMap ?? Find.Maps?.FirstOrDefault())?.mapPawns?.FreeColonists;
            return (cs != null && cs.Count >= 2) ? null : "need two free colonists";
        }

        private static string RecordsBoth(bool success, string expectRoleTag)
        {
            var cs = (Find.CurrentMap ?? Find.Maps.FirstOrDefault()).mapPawns.FreeColonists.ToList();
            Pawn therapist = cs[0], patient = cs[1];
            var tCore = therapist.TryGetComp<SynapseCorePawnComp>();
            var pCore = patient.TryGetComp<SynapseCorePawnComp>();
            var tPsych = therapist.TryGetComp<SynapsePawnComp>();
            var pPsych = patient.TryGetComp<SynapsePawnComp>();
            Assert.NotNull(tCore, "therapist has a core comp");
            Assert.NotNull(pCore, "patient has a core comp");

            var tMemsBefore = new HashSet<WeightedMemory>(tCore.memories);
            var pMemsBefore = new HashSet<WeightedMemory>(pCore.memories);
            int tScriptBefore = tPsych?.therapyTranscripts.Count ?? 0;
            int pScriptBefore = pPsych?.therapyTranscripts.Count ?? 0;

            try
            {
                SynapseTherapy.RecordSession(therapist, patient, success);

                var tAdded = tCore.memories.Where(m => !tMemsBefore.Contains(m) && m.memoryType == "Therapy").ToList();
                var pAdded = pCore.memories.Where(m => !pMemsBefore.Contains(m) && m.memoryType == "Therapy").ToList();
                Assert.Equal(1, tAdded.Count, "therapist gained exactly one Therapy memory");
                Assert.Equal(1, pAdded.Count, "patient gained exactly one Therapy memory");
                Assert.True(tAdded[0].tags.Contains("Therapist") && tAdded[0].tags.Contains(expectRoleTag),
                    $"therapist memory tagged Therapist+{expectRoleTag} (was [{string.Join(",", tAdded[0].tags)}])");
                Assert.True(pAdded[0].tags.Contains("Patient") && pAdded[0].tags.Contains(expectRoleTag),
                    $"patient memory tagged Patient+{expectRoleTag} (was [{string.Join(",", pAdded[0].tags)}])");
                Assert.Equal(tScriptBefore + 1, tPsych?.therapyTranscripts.Count ?? 0, "therapist gained a transcript");
                Assert.Equal(pScriptBefore + 1, pPsych?.therapyTranscripts.Count ?? 0, "patient gained a transcript");
                return $"both recorded ({expectRoleTag}); therapist='{tAdded[0].summary}'";
            }
            finally
            {
                // Clean up everything we added so the live colony is left untouched.
                tCore.memories.RemoveAll(m => !tMemsBefore.Contains(m) && m.memoryType == "Therapy");
                pCore.memories.RemoveAll(m => !pMemsBefore.Contains(m) && m.memoryType == "Therapy");
                if (tPsych != null) while (tPsych.therapyTranscripts.Count > tScriptBefore) tPsych.therapyTranscripts.RemoveAt(tPsych.therapyTranscripts.Count - 1);
                if (pPsych != null) while (pPsych.therapyTranscripts.Count > pScriptBefore) pPsych.therapyTranscripts.RemoveAt(pPsych.therapyTranscripts.Count - 1);
            }
        }
    }
}
