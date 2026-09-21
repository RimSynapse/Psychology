using System.Collections.Generic;
using System.Linq;
using Verse;
using RimSynapse;
using RimSynapse.Comps;
using RimSynapse.Psychology.API;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Faction-leader context injection (#21). The visitor/leader backstory prompts now weave in whatever a
    /// mod (Factions) injects via SynapseCoreContext.OnInjectGenericContext — the same hook the colonist path
    /// uses — plus a one-line note from the open-ended leadership hierarchy hook. These pin: an injected
    /// sentinel appears verbatim, nothing is added when nobody subscribes, and the hierarchy line appears only
    /// when a provider is registered. Live (the builders read a real pawn's story/name); each restores the
    /// global hook/provider it touched.
    /// </summary>
    [SynapseTestSet]
    public static class LeaderContextCases
    {
        private const string Sentinel = "SENTINEL_FactionHistory_7f3";

        private sealed class RoleProvider : SynapseCoreHierarchy.IHierarchyProvider
        {
            public Pawn who; public string role;
            public object ReportsTo(object node) => null;
            public IEnumerable<object> DirectReports(object node) => Enumerable.Empty<object>();
            public string RoleTitle(object node) => ReferenceEquals(node, who) ? role : null;
        }

        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Psychology_LeaderContextInjection_Appears",
                () =>
                {
                    var pawn = FirstColonist();
                    SynapseCoreContext.ContextInjectionHandler handler = (p, type, list) =>
                    {
                        if (type == SynapseContextTypes.BackstoryAdulthood) list.Add(Sentinel);
                    };
                    SynapseCoreContext.OnInjectGenericContext += handler;
                    try
                    {
                        string msg = SynapsePsychology.BuildVisitorAdulthoodUserMessage(
                            pawn, pawn.TryGetComp<SynapseCorePawnComp>(), "TestFaction", "Outlander");
                        Assert.True(msg.Contains(Sentinel), "the injected faction context appears verbatim in the visitor prompt");
                        Assert.True(msg.Contains("Visitor:"), "the base prompt is still intact");
                        return "injected context wove into the adulthood prompt";
                    }
                    finally { SynapseCoreContext.OnInjectGenericContext -= handler; }
                },
                skipReason: NeedColonist,
                tier: "Execution", polarity: "positive",
                scenario: "Factions subscribes to the context hook and a visitor/leader backstory is built",
                expectation: "The injected text appears verbatim in the outgoing prompt");

            yield return new SynapseTestCase("Psychology_LeaderContextInjection_CleanWithoutSubscriber",
                () =>
                {
                    var pawn = FirstColonist();
                    string msg = SynapsePsychology.BuildVisitorChildhoodUserMessage(pawn, "TestFaction", "Outlander");
                    Assert.True(!msg.Contains(Sentinel), "no injected block when nobody subscribes");
                    Assert.True(msg.Contains("Visitor:") && msg.Contains("Skills:"), "the prompt generates normally");
                    return "no subscriber -> prompt unchanged";
                },
                skipReason: NeedColonist,
                tier: "Execution", polarity: "negative",
                scenario: "No mod subscribes to the context hook",
                expectation: "The visitor prompt is unchanged and generation is unaffected");

            yield return new SynapseTestCase("Psychology_LeaderContext_HierarchyLine",
                () =>
                {
                    var pawn = FirstColonist();
                    var saved = SynapseCoreHierarchy.Provider;
                    SynapseCoreHierarchy.Provider = new RoleProvider { who = pawn, role = "Faction Leader" };
                    try
                    {
                        string msg = SynapsePsychology.BuildVisitorChildhoodUserMessage(pawn, "TestFaction", "Outlander");
                        Assert.True(msg.Contains("Leadership:") && msg.Contains("Faction Leader"),
                            "the hierarchy role is woven in when a provider is registered");
                        return "hierarchy role injected";
                    }
                    finally { SynapseCoreHierarchy.Provider = saved; }
                },
                skipReason: NeedColonist,
                tier: "Execution", polarity: "positive",
                scenario: "A leadership provider reports a role for the pawn",
                expectation: "A 'Leadership: Role: ...' line is added to the prompt");
        }

        private static string NeedColonist()
        {
            var cs = (Find.CurrentMap ?? Find.Maps?.FirstOrDefault())?.mapPawns?.FreeColonists;
            return (cs != null && cs.Count >= 1) ? null : "need a colonist with a backstory";
        }

        private static Pawn FirstColonist() => (Find.CurrentMap ?? Find.Maps.FirstOrDefault()).mapPawns.FreeColonists.First();
    }
}
