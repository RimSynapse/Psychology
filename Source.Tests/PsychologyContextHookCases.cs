using System.Collections.Generic;
using System.Linq;
using RimSynapse;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// The cross-mod context-injection contract (Psychology #26): SynapseCoreContext's
    /// OnInjectGenericContext / GatherGenericContext, keyed by the canonical SynapseContextTypes.
    /// Deterministic — the hook is a pure event. Every case that subscribes a handler detaches it in a
    /// finally block (the event is a shared static; a leaked subscriber would pollute the rest of the
    /// run), and uses its own sentinel + bogus contextTypes so real subscribers (Conversations/Factions)
    /// can't perturb the assertions.
    /// </summary>
    [SynapseTestSet]
    public static class PsychologyContextHookCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // The canonical set is exactly the five documented injection points.
            yield return new SynapseTestCase("Psychology_ContextHooks_CanonicalSet", () =>
            {
                var all = SynapseContextTypes.All;
                Assert.Equal(5, all.Length, "five canonical injection points");
                Assert.True(all.Contains(SynapseContextTypes.BackstoryChildhood), "BackstoryChildhood present");
                Assert.True(all.Contains(SynapseContextTypes.BackstoryAdulthood), "BackstoryAdulthood present");
                Assert.True(all.Contains(SynapseContextTypes.PersonalityProfile), "PersonalityProfile present");
                Assert.True(all.Contains(SynapseContextTypes.RelationshipEvaluation), "RelationshipEvaluation present");
                Assert.True(all.Contains(SynapseContextTypes.DailyReview), "DailyReview present");
                return string.Join(",", all);
            });

            // Zero subscribers for a point → empty string, so generation is byte-for-byte unchanged.
            yield return new SynapseTestCase("Psychology_ContextHooks_ZeroSubscriberEmpty", () =>
            {
                string r = SynapseCoreContext.GatherGenericContext(null, "zz_test_unhandled_point");
                Assert.True(r == "", $"a point nobody handles returns empty (was '{r}')");
                return "unhandled point -> empty";
            });

            // A handler keyed on one named point contributes there and NOWHERE else — no cross-point leak.
            yield return new SynapseTestCase("Psychology_ContextHooks_NamedPointIsolates", () =>
            {
                SynapseCoreContext.ContextInjectionHandler handler = (pawn, contextType, extra) =>
                {
                    if (contextType == SynapseContextTypes.DailyReview) extra.Add("zz_SENTINEL_DAILY");
                };
                SynapseCoreContext.OnInjectGenericContext += handler;
                try
                {
                    string daily = SynapseCoreContext.GatherGenericContext(null, SynapseContextTypes.DailyReview);
                    Assert.Contains(daily, "zz_SENTINEL_DAILY", "the handler's text lands at its own point");

                    string childhood = SynapseCoreContext.GatherGenericContext(null, SynapseContextTypes.BackstoryChildhood);
                    Assert.DoesNotContain(childhood, "zz_SENTINEL_DAILY", "DailyReview text must not leak into BackstoryChildhood");

                    string bogus = SynapseCoreContext.GatherGenericContext(null, "zz_test_other_point");
                    Assert.DoesNotContain(bogus, "zz_SENTINEL_DAILY", "DailyReview text must not leak into an unrelated point");
                    return "daily-keyed handler stayed isolated";
                }
                finally { SynapseCoreContext.OnInjectGenericContext -= handler; }
            });

            // Multiple handlers stack at a point; detaching all restores the empty (no-op) behavior.
            yield return new SynapseTestCase("Psychology_ContextHooks_MultipleHandlersStackAndDetach", () =>
            {
                SynapseCoreContext.ContextInjectionHandler h1 = (p, ct, e) => { if (ct == "zz_test_stack") e.Add("zz_A"); };
                SynapseCoreContext.ContextInjectionHandler h2 = (p, ct, e) => { if (ct == "zz_test_stack") e.Add("zz_B"); };
                SynapseCoreContext.OnInjectGenericContext += h1;
                SynapseCoreContext.OnInjectGenericContext += h2;
                try
                {
                    string both = SynapseCoreContext.GatherGenericContext(null, "zz_test_stack");
                    Assert.Contains(both, "zz_A", "handler 1 contributed");
                    Assert.Contains(both, "zz_B", "handler 2 contributed");
                }
                finally
                {
                    SynapseCoreContext.OnInjectGenericContext -= h1;
                    SynapseCoreContext.OnInjectGenericContext -= h2;
                }

                string after = SynapseCoreContext.GatherGenericContext(null, "zz_test_stack");
                Assert.True(after == "", "after detaching every handler, the point is empty again");
                return "stacked A+B, detached clean";
            });
        }
    }
}
