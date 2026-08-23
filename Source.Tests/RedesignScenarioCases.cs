using System;
using System.Collections.Generic;
using RimSynapse;
using RimSynapse.Comps;
using RimSynapse.Models;
using RimSynapse.Psychology.API;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Curated two-tier acceptance scenarios for the 0.7.1 redesign, feeding the wiki Test Report.
    ///   • Execution (downstream) — given a decision, does the engine execute it right? Deterministic.
    ///   • Determination (upstream) — does the LLM decide right? Live-LLM; skipped under -quicktest.
    /// These carry human-readable scenario/expectation text; the granular unit tests live elsewhere.
    /// </summary>
    [SynapseTestSet]
    public static class RedesignScenarioCases
    {
        private static SynapseTestCase Exec(string name, string polarity, string scenario, string expectation, Func<string> run)
            => new SynapseTestCase(name, run, null, "Execution", polarity, scenario, expectation);
        // The Determination (upstream) tier is owned by DeterminationRunnerGameComponent, which runs it
        // live (-synapse-determination) and writes that section of the report; the deterministic suite
        // here contributes only the Execution tier.

        public static IEnumerable<SynapseTestCase> All()
        {
            // ── Execution (downstream, deterministic) ──────────────────────────────
            yield return Exec("Scenario_ObjectViolenceIsNonLethal", "negative",
                "A colonist spends the day attacking a wrecked vehicle (an object).",
                "The activity reads as one deduped, non-lethal, object-targeted line — not violent signal.",
                () =>
                {
                    var comp = new SynapseCorePawnComp { lastJobStartedTick = -1 };
                    int now = Verse.Find.TickManager.TicksGame;
                    for (int i = 0; i < 50; i++) comp.recentJobs.Add(new JobInterval("AttackStatic", now - 5000 + i * 10, 40, "object"));
                    string s = comp.GetRecentJobsSummary();
                    Assert.Contains(s, "non-lethal", "object violence must be non-lethal");
                    return s;
                });

            yield return Exec("Scenario_LivingViolenceIsNotMasked", "positive",
                "A colonist attacks a person and an animal.",
                "Combat against the living is NOT labelled non-lethal.",
                () =>
                {
                    var comp = new SynapseCorePawnComp { lastJobStartedTick = -1 };
                    int now = Verse.Find.TickManager.TicksGame;
                    comp.recentJobs.Add(new JobInterval("AttackMelee", now - 5000, 4000, "humanlike"));
                    string s = comp.GetRecentJobsSummary();
                    Assert.DoesNotContain(s, "non-lethal", "attacking the living is not non-lethal");
                    return s;
                });

            yield return Exec("Scenario_IdleChitChatDecaysOut", "negative",
                "A colonist has one idle chit-chat memory and nothing links to it.",
                "It prunes within the daily maintenance pass (noise does not accumulate).",
                () =>
                {
                    SynapseCorePawnComp.MemoryDecayMultiplier = 1.0f;
                    var comp = new SynapseCorePawnComp();
                    SynapseCoreDebug.AddMemory(comp, "idle small talk", "social", 0.1f, null, null, false);
                    string r = SynapseCoreDebug.RunMaintenance(comp);
                    Assert.Equal(0, comp.memories.Count, "lone chit-chat must prune");
                    return r;
                });

            yield return Exec("Scenario_ChitChatLinkedToDeathIsKept", "positive",
                "Chit-chat about a colonist who then dies (both share the pawn's id).",
                "The chit-chat is consolidated to long-term the day the death lands.",
                () =>
                {
                    SynapseCorePawnComp.MemoryDecayMultiplier = 1.0f;
                    var comp = new SynapseCorePawnComp();
                    SynapseCoreDebug.AddMemory(comp, "idle banter with Tynan", "social", 0.1f, null, "Tynan", false);
                    SynapseCoreDebug.AddMemory(comp, "Tynan died in the raid", "EventReflection", 1.0f, new List<string> { "Death" }, "Tynan", false);
                    SynapseCoreDebug.RunMaintenance(comp);
                    var chat = comp.memories.Find(m => m.summary.StartsWith("idle banter"));
                    Assert.NotNull(chat, "linked chit-chat survives");
                    Assert.True(chat.isLongTerm, "linked chit-chat consolidates to long-term");
                    return $"salience={chat.salience:0.00}";
                });

            yield return Exec("Scenario_UnlikelyDayNeverFlipsTrait", "negative",
                "The daily evaluation calls a Bloodlust shift 'unlikely' (low likelihood).",
                "Today contributes zero pressure — an unlikely day can never flip a trait.",
                () =>
                {
                    float gated = SynapseTraitPolicy.GateDailyPressure("low", 0.9f);
                    var comp = new SynapseCorePawnComp();
                    float p = comp.AccumulateTraitPressure("Bloodlust", gated, "add", 0f, 60000L);
                    Assert.True(p < 1.0f, "an unlikely day cannot reach the shift threshold");
                    return $"pressure={p:0.00}";
                });

            yield return Exec("Scenario_SustainedViolenceBuildsToAShift", "positive",
                "Genuine violence against the living recurs over three days.",
                "Trait pressure accumulates across days and crosses the shift threshold.",
                () =>
                {
                    var comp = new SynapseCorePawnComp();
                    comp.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, 60000L);
                    comp.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, 120000L);
                    float p = comp.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, 180000L);
                    Assert.True(p >= 1.0f, "sustained multi-day evidence crosses the threshold");
                    return $"day3 pressure={p:0.00}";
                });

            yield return Exec("Scenario_DangerousTraitCannotBeGranted", "negative",
                "A malformed/over-eager evaluation tries to add Psychopath.",
                "The whitelist blocks it; only sanctioned traits may be added/removed.",
                () =>
                {
                    Assert.False(SynapseTraitPolicy.IsWhitelisted("Psychopath"), "Psychopath can never be AI-granted");
                    Assert.True(SynapseTraitPolicy.IsWhitelisted("Bloodlust"), "Bloodlust is a sanctioned shift");
                    return "whitelist enforced";
                });
        }
    }
}
