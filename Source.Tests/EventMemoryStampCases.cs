using System.Collections.Generic;
using System.Linq;
using RimSynapse.Models;
using RimSynapse.Psychology.API;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// #73: the opportunistic-memory event-provenance contract. Deterministic / headless — exercises the
    /// two roster resolvers the minting path uses (SynapsePsychology.ResolveSubjectLoadIds /
    /// ResolveWitnessLoadIds) directly on synthetic PastEvents, with no live map. The live-map tiering
    /// end-to-end (InvolvementOf over real pawns) is covered by the "Validate event memory stamping (#73)"
    /// debug action, which needs the game.
    /// </summary>
    [SynapseTestSet]
    public static class EventMemoryStampCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // Involved roster comes verbatim from the canonical involvedPawnLoadIds Core captured at event
            // time — protagonist first (index 0), order preserved — NOT re-derived from ThingIDs.
            yield return new SynapseTestCase("Psychology_EventStamp_InvolvedRosterFromLoadIds", () =>
            {
                var pe = new PastEvent
                {
                    involvedPawnLoadIds = new List<string> { "Human_Alice12345", "Human_Bob67890" },
                    involvedPawnIds = new List<string> { "Thing_100", "Thing_101" } // ThingIDs must NOT leak through
                };
                var ids = SynapsePsychology.ResolveSubjectLoadIds(pe, null);
                Assert.Equal(2, ids.Count, "both involved pawns resolved");
                Assert.Equal("Human_Alice12345", ids[0], "protagonist (index 0) preserved");
                Assert.Equal("Human_Bob67890", ids[1], "participant preserved in order");
                Assert.True(!ids.Contains("Thing_100") && !ids.Contains("Thing_101"),
                    "no raw ThingID leaked into the canonical roster");
                return $"involved=[{string.Join(",", ids)}]";
            });

            // A witness ThingID that resolves to no live pawn (no map in a headless run) is DROPPED — never
            // stored as a raw ThingID, which would fail to match Core's canonical involvement scheme.
            yield return new SynapseTestCase("Psychology_EventStamp_WitnessUnresolvableDropped", () =>
            {
                var pe = new PastEvent
                {
                    witnessPawnIds = new List<string> { "Thing_ghost_9999" }
                };
                var ids = SynapsePsychology.ResolveWitnessLoadIds(pe);
                Assert.Equal(0, ids.Count, "unresolvable witness dropped, not stored as a ThingID");
                Assert.True(!ids.Contains("Thing_ghost_9999"), "no raw ThingID retained");
                return $"witness=[{string.Join(",", ids)}] (dropped 1 unresolvable)";
            });

            // Null / empty rosters never throw and yield empty lists (safe stamp for event-less memories).
            yield return new SynapseTestCase("Psychology_EventStamp_NullSafe", () =>
            {
                var involved = SynapsePsychology.ResolveSubjectLoadIds(new PastEvent(), null);
                var witness = SynapsePsychology.ResolveWitnessLoadIds(new PastEvent());
                Assert.NotNull(involved, "involved resolver never returns null");
                Assert.NotNull(witness, "witness resolver never returns null");
                Assert.Equal(0, witness.Count, "empty witness roster -> empty result");
                return $"involved={involved.Count}, witness={witness.Count}";
            });
        }
    }
}
