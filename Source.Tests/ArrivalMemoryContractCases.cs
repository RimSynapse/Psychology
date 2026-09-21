using System.Collections.Generic;
using System.Linq;
using RimSynapse.Comps;
using RimSynapse.Psychology.API;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// The backstory-arrival / defining-memory contract (Psychology #22). Deterministic — operates on a
    /// bare comp through the comp-level cores (PlantDefiningMemoryOn / BumpMemoryOn). Relies on the
    /// shipped BackstoryArrival bornLongTerm class being loaded (it is, in-game).
    /// </summary>
    [SynapseTestSet]
    public static class ArrivalMemoryContractCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // A planted defining memory is a secured BackstoryArrival carrying the requested tags.
            yield return new SynapseTestCase("Psychology_PlantDefiningMemory_IsSecured", () =>
            {
                var comp = new SynapseCorePawnComp();
                SynapsePsychology.PlantDefiningMemoryOn(comp, "the day they arrived", new[] { "Arrival" });
                var m = comp.memories.FirstOrDefault(x => x.memoryType == "BackstoryArrival");
                Assert.NotNull(m, "a BackstoryArrival memory was planted");
                Assert.True(m.isLongTerm, "the defining memory is secured (long-term / pruning-exempt)");
                Assert.True(m.tags.Contains("Arrival") && m.tags.Contains("DefiningMemory"),
                    $"the planted tags + DefiningMemory marker are present (was [{string.Join(",", m.tags)}])");
                return $"isLongTerm={m.isLongTerm}, tags=[{string.Join(",", m.tags)}]";
            });

            // It survives repeated daily maintenance unchanged (pruning-exempt).
            yield return new SynapseTestCase("Psychology_PlantDefiningMemory_SurvivesMaintenance", () =>
            {
                var comp = new SynapseCorePawnComp();
                SynapsePsychology.PlantDefiningMemoryOn(comp, "arrival anchor", new[] { "Arrival" });
                float w0 = comp.memories.First(x => x.memoryType == "BackstoryArrival").weight;
                for (int i = 0; i < 5; i++) comp.RunMemoryMaintenance();
                var m = comp.memories.FirstOrDefault(x => x.memoryType == "BackstoryArrival");
                Assert.NotNull(m, "the defining memory survives 5 maintenance passes");
                Assert.Equal(w0, m.weight, "its weight is untouched by decay");
                return $"survived, weight {w0:F2}->{m.weight:F2}";
            });

            // Resonance: BumpMemory matches the planted tag, raising weight + timesReferenced.
            yield return new SynapseTestCase("Psychology_ArrivalMemory_TagResonance", () =>
            {
                var comp = new SynapseCorePawnComp();
                SynapsePsychology.PlantDefiningMemoryOn(comp, "hometown Kharstead", new[] { "Hometown" });
                var m = comp.memories.First(x => x.memoryType == "BackstoryArrival");
                float w0 = m.weight;
                int r0 = m.timesReferenced;
                SynapsePsychology.BumpMemoryOn(comp, "Hometown", 0.2f); // reference by the planted tag
                Assert.True(m.weight > w0, $"the tag reference reinforced the memory ({w0:F2}->{m.weight:F2})");
                Assert.Equal(r0 + 1, m.timesReferenced, "timesReferenced incremented");
                return $"weight {w0:F2}->{m.weight:F2}, refs {r0}->{m.timesReferenced}";
            });

            // Null-safe: no throw on a null pawn or null comp.
            yield return new SynapseTestCase("Psychology_PlantDefiningMemory_NullSafe", () =>
            {
                SynapsePsychology.PlantDefiningMemory(null, "ignored");   // null pawn -> warn, no throw
                SynapsePsychology.PlantDefiningMemoryOn(null, "ignored"); // null comp -> no throw
                SynapsePsychology.BumpMemoryOn(null, "x");                // null comp -> no throw
                return "null pawn/comp handled without throwing";
            });
        }
    }
}
