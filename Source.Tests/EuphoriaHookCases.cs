using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Extensions;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// The euphoria hook is data-driven (#64). Previously SynapseBreakManager hardcoded specific bipolar defs
    /// and SynapseTraitExtension.causesEuphoria was declared but never read (a false affordance). Now the
    /// Synapse_Bipolar def carries the extension and SynapseBreakManager resolves any trait that declares it.
    /// These pin: the def is tagged, detection fires on a tagged trait (and not without it), and null-safety.
    /// </summary>
    [SynapseTestSet]
    public static class EuphoriaHookCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // The def actually declares the hook, so the extension is wired to real data (not a dead field).
            yield return new SynapseTestCase("Psychology_EuphoriaHook_DefIsTagged", () =>
            {
                var def = DefDatabase<TraitDef>.GetNamedSilentFail("Synapse_Bipolar");
                Assert.NotNull(def, "Synapse_Bipolar trait def is loaded");
                var ext = def.GetModExtension<SynapseTraitExtension>();
                Assert.NotNull(ext, "Synapse_Bipolar carries a SynapseTraitExtension");
                Assert.True(ext.causesEuphoria, "and declares causesEuphoria = true");
                return "Synapse_Bipolar → causesEuphoria";
            },
            tier: "Execution", polarity: "positive",
            scenario: "The bipolar trait declares the euphoria hook via its def extension",
            expectation: "The extension is real data, not an unread field");

            // Detection fires on a pawn carrying a tagged trait, and stops once it's removed.
            yield return new SynapseTestCase("Psychology_EuphoriaHook_DetectsTaggedTrait",
                DetectsTaggedTrait,
                skipReason: () =>
                {
                    var cs = (Find.CurrentMap ?? Find.Maps?.FirstOrDefault())?.mapPawns?.FreeColonists;
                    return (cs != null && cs.Count >= 1) ? null : "need a colonist";
                },
                tier: "Execution", polarity: "positive",
                scenario: "A pawn gains, then loses, a trait that declares causesEuphoria",
                expectation: "AnyTraitCausesEuphoria is true while present and false once removed");

            // Null-safe.
            yield return new SynapseTestCase("Psychology_EuphoriaHook_NullSafe", () =>
            {
                Assert.True(!SynapseTraitExtension.AnyTraitCausesEuphoria(null), "null pawn → false, no throw");
                return "null-safe";
            },
            tier: "Execution", polarity: "negative",
            scenario: "The euphoria check is called with no pawn",
            expectation: "Returns false without throwing");
        }

        private static string DetectsTaggedTrait()
        {
            var pawn = (Find.CurrentMap ?? Find.Maps.FirstOrDefault()).mapPawns.FreeColonists.First();
            var def = DefDatabase<TraitDef>.GetNamed("Synapse_Bipolar");
            bool hadBefore = pawn.story.traits.HasTrait(def);
            bool added = false;
            try
            {
                if (!hadBefore) { pawn.story.traits.GainTrait(new Trait(def, 0, true)); added = true; }
                Assert.True(SynapseTraitExtension.AnyTraitCausesEuphoria(pawn), "a tagged trait is detected");

                if (added)
                {
                    var t = pawn.story.traits.GetTrait(def);
                    if (t != null) pawn.story.traits.RemoveTrait(t);
                    Assert.True(!SynapseTraitExtension.AnyTraitCausesEuphoria(pawn), "detection stops once the trait is gone");
                }
                return added ? "detected present, cleared when removed" : "detected (colonist already had it)";
            }
            finally
            {
                if (added)
                {
                    var leftover = pawn.story.traits.GetTrait(def);
                    if (leftover != null) pawn.story.traits.RemoveTrait(leftover);
                }
            }
        }
    }
}
