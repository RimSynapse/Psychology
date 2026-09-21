using System.Collections.Generic;
using System.Linq;
using Verse;
using RimSynapse;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.Models;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Colony cliques (#20, re-scoped to clique detection). Friend-groups are derived from mutual warmth +
    /// familiarity on the #72 compass, and a pawn's clique is injected into social-flavoured prompt context.
    /// These pin: a mutual pair groups (a one-sided bond does not), and the context injection fires for social
    /// stages only. Live (real pawns + comps); each restores the social records it touches. The rumor mill is
    /// intentionally absent (owned by Conversations #60 / Factions #17).
    /// </summary>
    [SynapseTestSet]
    public static class CliqueDetectionCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Psychology_CliqueDetection_GroupsMutualClose",
                GroupsMutualClose,
                skipReason: NeedThreeColonists,
                tier: "Execution", polarity: "positive",
                scenario: "Two colonists are mutually warm+familiar; a third is close to one only one-sidedly",
                expectation: "The mutual pair forms a clique; the one-sided colonist is not grouped");

            yield return new SynapseTestCase("Psychology_Clique_ContextInjectedForSocialStagesOnly",
                ContextInjection,
                skipReason: NeedThreeColonists,
                tier: "Execution", polarity: "positive",
                scenario: "A pawn in a clique has context gathered for different generation stages",
                expectation: "The clique line is added for RelationshipEvaluation but not for a backstory stage");

            yield return new SynapseTestCase("Psychology_Cliques_NullAndEmptySafe", () =>
            {
                Assert.Equal(0, SynapseCliques.Detect(null).Count, "null input -> no groups");
                Assert.Equal(0, SynapseCliques.Detect(new List<Pawn>()).Count, "empty input -> no groups");
                Assert.Equal("", SynapseCliques.CliqueSummaryFor(null), "null pawn -> empty summary");
                return "null/empty safe";
            },
            tier: "Execution", polarity: "negative",
            scenario: "Detection helpers are called with no/empty input",
            expectation: "No throw; empty results");
        }

        private static string NeedThreeColonists()
        {
            var cs = (Find.CurrentMap ?? Find.Maps?.FirstOrDefault())?.mapPawns?.FreeColonists;
            return (cs != null && cs.Count >= 3) ? null : "need three colonists";
        }

        private sealed class Snap { public SynapsePawnComp comp; public string id; public bool existed; public float w, f; }

        private static string GroupsMutualClose()
        {
            var cs = (Find.CurrentMap ?? Find.Maps.FirstOrDefault()).mapPawns.FreeColonists.ToList();
            Pawn a = cs[0], b = cs[1], c = cs[2];
            var snap = new List<Snap>();
            try
            {
                Set(snap, a, b, 60f, 70f); Set(snap, b, a, 55f, 65f);   // mutual
                Set(snap, a, c, 60f, 70f); Set(snap, c, a, -5f, 65f);   // one-sided

                var groups = SynapseCliques.Detect(new List<Pawn> { a, b, c });
                var withA = groups.FirstOrDefault(g => g.Contains(a));
                Assert.NotNull(withA, "a clique containing A exists");
                Assert.True(withA.Contains(b), "A and B are in the same clique (mutual)");
                Assert.True(!withA.Contains(c), "C is excluded — a one-sided bond is not a clique link");
                return $"clique of {withA.Count}: [{string.Join(",", withA.Select(p => p.LabelShort))}]";
            }
            finally { Restore(snap); }
        }

        private static string ContextInjection()
        {
            var cs = (Find.CurrentMap ?? Find.Maps.FirstOrDefault()).mapPawns.FreeColonists.ToList();
            Pawn a = cs[0], b = cs[1];
            var snap = new List<Snap>();
            try
            {
                Set(snap, a, b, 60f, 70f); Set(snap, b, a, 55f, 65f);
                SynapseCliques.ForMap(Find.CurrentMap, force: true);

                var social = new List<string>();
                SynapseCliques.InjectCliqueContext(a, SynapseContextTypes.RelationshipEvaluation, social);
                Assert.True(social.Count == 1 && social[0].Contains(b.LabelShort), "clique context injected for a social stage");

                var backstory = new List<string>();
                SynapseCliques.InjectCliqueContext(a, SynapseContextTypes.BackstoryChildhood, backstory);
                Assert.Equal(0, backstory.Count, "no clique context on a backstory stage");
                return $"injected: \"{social[0]}\"";
            }
            finally { Restore(snap); SynapseCliques.ForMap(Find.CurrentMap, force: true); }
        }

        private static void Set(List<Snap> snap, Pawn from, Pawn to, float w, float f)
        {
            var comp = from.GetComp<SynapsePawnComp>();
            string id = to.GetUniqueLoadID();
            bool existed = comp.socialNetwork.TryGetValue(id, out var rec);
            snap.Add(new Snap { comp = comp, id = id, existed = existed, w = existed ? rec.warmth : 0f, f = existed ? rec.familiarity : 0f });
            if (!existed) { rec = new SocialRecord(); comp.socialNetwork[id] = rec; }
            rec.warmth = w; rec.familiarity = f;
        }

        private static void Restore(List<Snap> snap)
        {
            foreach (var s in snap)
            {
                if (s.existed && s.comp.socialNetwork.TryGetValue(s.id, out var rec)) { rec.warmth = s.w; rec.familiarity = s.f; }
                else s.comp.socialNetwork.Remove(s.id);
            }
        }
    }
}
