using System.Collections.Generic;
using RimWorld;
using Verse;
using RimSynapse.Models;
using RimSynapse.Psychology.API;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// The two standalone 0.7.1-audit bugs: AI-driven mental breaks never fire (#50) and therapy
    /// summaries computed then discarded (#51).
    /// </summary>
    [SynapseTestSet]
    public static class StandaloneBugCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // #50: every break candidate the LLM is offered MUST resolve as a MentalStateDef, or
            // PredictMentalBreak's lookup returns null and the break silently never fires (the exact
            // trap that "Catatonic" — a MentalBreakDef, not a MentalStateDef — would have caused).
            yield return new SynapseTestCase("Psychology_BreakCandidatesAreValidMentalStateDefs", () =>
            {
                Assert.True(SynapsePsychology.BreakCandidates.Length > 0, "there must be candidate breaks to offer");
                foreach (var name in SynapsePsychology.BreakCandidates)
                {
                    var def = DefDatabase<MentalStateDef>.GetNamedSilentFail(name);
                    Assert.NotNull(def, $"break candidate '{name}' must be a real MentalStateDef");
                }
                return $"{SynapsePsychology.BreakCandidates.Length} candidates all valid MentalStateDefs";
            });

            // #51: the therapy summary is turned into a durable, long-term, tagged memory (it used to be
            // computed and thrown away).
            yield return new SynapseTestCase("Psychology_TherapySummaryBecomesDurableMemory", () =>
            {
                WeightedMemory mem = SynapsePsychology.BuildTherapyMemory(null, "They finally forgave themselves.", 4242L);
                Assert.NotNull(mem, "a therapy memory is produced");
                Assert.True(mem.isLongTerm, "therapy summaries are long-term (never decay)");
                Assert.Contains(mem.summary, "They finally forgave themselves.", "the summary text is preserved");
                Assert.True(mem.tags != null && mem.tags.Contains("Therapy"), "tagged Therapy so it is retrievable");
                Assert.Equal(4242L, mem.absTick, "stamped with the session tick");
                return $"durable memory: longTerm={mem.isLongTerm}, tags=[{string.Join(",", mem.tags)}]";
            });
        }
    }
}
