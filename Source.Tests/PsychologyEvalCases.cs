using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimSynapse.Models;
using RimSynapse.Psychology.API;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Stage 0 of the 0.7.1 memory/eval redesign (Psychology #42, #43): the daily evaluation must be
    /// able to tell today from a lifetime, and must state lifetime violence against the living so
    /// object-bashing cannot masquerade as bloodlust.
    /// </summary>
    [SynapseTestSet]
    public static class PsychologyEvalCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // #42: only memories within the last day count as "today", even when handed the full bank.
            yield return new SynapseTestCase("Psychology_EvalSelectsOnlyTodaysEvents", () =>
            {
                long now = 1_000_000L;
                var events = new List<WeightedMemory>
                {
                    new WeightedMemory { summary = "today event",      absTick = now - 100 },
                    new WeightedMemory { summary = "yesterday-ish",     absTick = now - 59_999 },
                    new WeightedMemory { summary = "a year ago",        absTick = now - 500_000 },
                };

                var today = SynapsePsychology.SelectTodaysEvents(events, now);
                Assert.Equal(2, today.Count, "only sub-day memories are 'today'");
                Assert.DoesNotContain(string.Join("|", today.ConvertAll(m => m.summary)), "a year ago",
                    "lifetime-old memories must not be fed as today's events");
                return $"selected {today.Count} of {events.Count}";
            });

            // Null input must not throw — the debug action passes an empty/absent list.
            yield return new SynapseTestCase("Psychology_EvalTodaysEventsHandlesNull", () =>
            {
                var today = SynapsePsychology.SelectTodaysEvents(null, 1000L);
                Assert.Equal(0, today.Count, "null events yield an empty list, not a throw");
                return "null-safe";
            });

            // #43: lifetime violence names living kills distinctly (humanlike + animal).
            yield return new SynapseTestCase("Psychology_LifetimeViolenceNamesLivingKills", () =>
            {
                string desc = SynapsePsychology.DescribeLifetimeViolence(3, 5);
                Assert.Contains(desc, "3 humanlike", "humanlike kills must be stated");
                Assert.Contains(desc, "5 animal", "animal kills must be stated");
                Assert.Contains(desc, "8 living", "living total must be stated");
                return desc;
            });

            // Clinical-review eligibility (#39): colonists are eligible; null/ineligible are not.
            yield return new SynapseTestCase("Psychology_ReviewEligibility", () =>
            {
                Assert.False(SynapsePsychology.IsEligibleForReview(null), "null is never eligible");

                var map = Verse.Find.CurrentMap ?? Verse.Find.Maps.FirstOrDefault();
                Assert.True(map != null, "no map available");
                var colonists = map.mapPawns.FreeColonists.ToList();
                Assert.True(colonists.Count > 0, "expected at least one colonist");
                foreach (var c in colonists)
                    Assert.True(SynapsePsychology.IsEligibleForReview(c), $"{c.LabelShort} (colonist) should be eligible");

                // Any non-colony humanlike on the map (visitor/enemy) must NOT be eligible.
                int ineligible = 0;
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    if (!p.RaceProps.Humanlike || p.Dead) continue;
                    if (p.IsColonist || p.IsPrisonerOfColony || p.IsSlaveOfColony || p.IsQuestLodger()) continue;
                    Assert.False(SynapsePsychology.IsEligibleForReview(p), $"{p.LabelShort} (non-affiliate) must not be eligible");
                    ineligible++;
                }
                return $"{colonists.Count} colonist(s) eligible; {ineligible} non-affiliate(s) excluded";
            });
        }
    }
}
