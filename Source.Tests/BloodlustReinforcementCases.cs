using System.Collections.Generic;
using RimSynapse.Comps;
using RimSynapse.Psychology.API;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Bloodlust as reinforcement (#54): the trait must develop from PROFITABLE, DOMINANT killing — a colonist
    /// who keeps killing while their fortune and mood climb, especially when they out-kill their peers — and
    /// NOT from desperate survival killing in a high-conflict scramble. These pin the three pure pieces of that
    /// mechanic: the Bloodlust reinforcement curve, the kill-dominance math, and the wealth (fortune) trend.
    /// </summary>
    [SynapseTestSet]
    public static class BloodlustReinforcementCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // Positive: a top killer (dominance 1) whose mood AND wealth are climbing builds meaningful pressure.
            yield return new SynapseTestCase("Psychology_Bloodlust_ThrivingDominantKillerBuildsPressure", () =>
            {
                float p = SynapseSkillAxisMap.BloodlustPressure(0.5f, posR: 1f, dominance: 1f, wealthUp: 1f);
                Assert.True(p > 0.3f, $"a thriving dominant killer builds real Bloodlust pressure (got {p:F3})");
                Assert.True(p <= 0.35f, $"and it stays within the curve's ceiling (got {p:F3})");
                return $"thriving dominant killer -> {p:F3}/day";
            },
            tier: "Execution", polarity: "positive",
            scenario: "Top killer, mood and personal wealth both climbing while they keep killing",
            expectation: "Bloodlust pressure is meaningful");

            // Negative (survival scramble): killing with NO positive mood response is trauma, not bloodlust.
            yield return new SynapseTestCase("Psychology_Bloodlust_SurvivalKillingBuildsNone", () =>
            {
                float noMood = SynapseSkillAxisMap.BloodlustPressure(0.5f, posR: 0f, dominance: 1f, wealthUp: 0f);
                Assert.Equal(0f, noMood, "killing while NOT thriving (mood flat/down) builds no Bloodlust");
                // And even a thriving mood does nothing for a pawn who is not a dominant killer.
                float notDominant = SynapseSkillAxisMap.BloodlustPressure(0.5f, posR: 1f, dominance: 0f, wealthUp: 1f);
                Assert.Equal(0f, notDominant, "one of many hands in a scramble (dominance 0) builds no Bloodlust");
                return "survival killing and non-dominant killing both -> 0";
            },
            tier: "Execution", polarity: "negative",
            scenario: "A colonist kills to survive but isn't thriving, or isn't the dominant killer",
            expectation: "No Bloodlust pressure (that path is trauma, handled as mood-down elsewhere)");

            // Negative (object bashing): below the living-violence exposure floor, nothing forms.
            yield return new SynapseTestCase("Psychology_Bloodlust_ObjectBashingBuildsNone", () =>
            {
                float below = SynapseSkillAxisMap.BloodlustPressure(SynapseSkillAxisMap.BloodlustViolenceFloor - 0.01f,
                    posR: 1f, dominance: 1f, wealthUp: 1f);
                Assert.Equal(0f, below, "activity below the living-violence floor (e.g. object bashing) builds no Bloodlust");
                return $"exposure < {SynapseSkillAxisMap.BloodlustViolenceFloor:F2} -> 0";
            },
            tier: "Execution", polarity: "negative",
            scenario: "The day's violence was against objects/mechanoids, under the living-violence floor",
            expectation: "No Bloodlust pressure");

            // Fortune scales the curve: rising wealth strengthens it, but a mood-only thriver still qualifies
            // (wealth gets a 0.5 floor), just more slowly.
            yield return new SynapseTestCase("Psychology_Bloodlust_FortuneScalesPressure", () =>
            {
                float rich = SynapseSkillAxisMap.BloodlustPressure(0.5f, posR: 1f, dominance: 1f, wealthUp: 1f);
                float moodOnly = SynapseSkillAxisMap.BloodlustPressure(0.5f, posR: 1f, dominance: 1f, wealthUp: 0f);
                Assert.True(rich > moodOnly, $"rising wealth strengthens the signal ({rich:F3} > {moodOnly:F3})");
                Assert.True(moodOnly > 0f, "a mood-only thriver still qualifies (wealth 0.5 floor), just slower");
                Assert.True(System.Math.Abs(moodOnly - rich * 0.5f) < 0.001f, "flat wealth is exactly half the full-fortune push");
                return $"wealth-up {rich:F3} vs mood-only {moodOnly:F3}";
            },
            tier: "Execution", polarity: "positive",
            scenario: "Two dominant thriving killers, one also accruing wealth, one not",
            expectation: "The wealth-accruing killer builds Bloodlust faster; mood-only still builds some");

            // Kill dominance: the colony's top killer out-ranks peers; a non-killer scores 0.
            yield return new SynapseTestCase("Psychology_Bloodlust_KillDominanceRanksTopKiller", () =>
            {
                // Colony of 3: mine=10, peers 2 and 1 -> total 13, I'm top killer -> share ~0.77 + 0.25 bonus, clamped 1.
                float top = SynapseCorePawnComp.KillDominanceOf(10, 13, 10, 3);
                Assert.True(top > 0.9f, $"the runaway top killer is dominant (got {top:F2})");
                // Mid-pack: mine=3 of 13 total, best is 10 -> not top killer -> share ~0.23, no bonus.
                float mid = SynapseCorePawnComp.KillDominanceOf(3, 13, 10, 3);
                Assert.True(mid > 0.2f && mid < 0.3f, $"a mid-pack killer scores their bare share (got {mid:F2})");
                Assert.True(top > mid, "the top killer out-ranks the mid-pack killer");
                // A pawn who has never killed, or a colony with no kills at all, is never dominant.
                Assert.Equal(0f, SynapseCorePawnComp.KillDominanceOf(0, 13, 10, 3), "a non-killer has 0 dominance");
                Assert.Equal(0f, SynapseCorePawnComp.KillDominanceOf(5, 0, 0, 3), "no colony kills -> 0 dominance");
                return $"top {top:F2} vs mid {mid:F2}";
            },
            tier: "Execution", polarity: "positive",
            scenario: "One colonist has done most of the colony's killing",
            expectation: "That colonist scores high dominance; peers and non-killers score low/zero");

            // The prompt's fortune-trend words (#54): rising / falling / steady vs the EMA baseline, and the
            // "-1 baseline" sentinel reads as not-yet-tracked rather than a false trend.
            yield return new SynapseTestCase("Psychology_Bloodlust_FortuneTrendWords", () =>
            {
                Assert.Equal("not yet tracked", SynapsePsychology.TrendVs(1000f, -1f), "uninitialised baseline is not a trend");
                Assert.Equal("RISING", SynapsePsychology.TrendVs(1200f, 1000f), "up >5% reads as rising");
                Assert.Equal("falling", SynapsePsychology.TrendVs(800f, 1000f), "down >5% reads as falling");
                Assert.Equal("steady", SynapsePsychology.TrendVs(1020f, 1000f), "within ±5% reads as steady");
                return "sentinel / rising / falling / steady";
            },
            tier: "Execution", polarity: "positive",
            scenario: "The nightly review describes wealth and mood trend to the model",
            expectation: "The trend words track the value vs its baseline, with a safe uninitialised sentinel");

            // Fortune trend: the wealth baseline seeds, then reports rising vs falling personal fortune, clamped.
            yield return new SynapseTestCase("Psychology_Bloodlust_WealthBaselineTracksFortune", () =>
            {
                var comp = new SynapseCorePawnComp();
                float seed = comp.UpdateWealthBaselineAndGetReinforcement(1000f);
                Assert.Equal(0f, seed, "the first sample only seeds the baseline (no trend yet)");
                float rising = comp.UpdateWealthBaselineAndGetReinforcement(1300f); // +30% vs a ~1000 baseline
                Assert.True(rising > 0f, $"a wealthier day than the baseline reads as rising fortune (got {rising:F2})");

                var comp2 = new SynapseCorePawnComp();
                comp2.UpdateWealthBaselineAndGetReinforcement(1000f);
                float falling = comp2.UpdateWealthBaselineAndGetReinforcement(700f);
                Assert.True(falling < 0f, $"a poorer day reads as falling fortune (got {falling:F2})");

                var comp3 = new SynapseCorePawnComp();
                comp3.UpdateWealthBaselineAndGetReinforcement(1000f);
                float huge = comp3.UpdateWealthBaselineAndGetReinforcement(1_000_000f);
                Assert.True(huge <= 1f && huge > 0f, "a windfall is clamped to +1, not unbounded");
                return $"rising {rising:F2}, falling {falling:F2}, windfall {huge:F2}";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A pawn's personal wealth is sampled day over day",
            expectation: "The trend reads positive when fortune rises, negative when it falls, clamped to [-1,1]");
        }
    }
}
