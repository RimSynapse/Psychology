using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Comps;
using RimSynapse.Compat;
using RimSynapse.Psychology;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Comps;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Psychology's halves of case sets that used to mix repos in the central TestRunner: the
    /// compat-registry consumer case, the ceremony deferred-callback guard, the voice-profile
    /// mapping, the skill→trait mapping table, and the trait-policy gate/whitelist (including the
    /// Core-named end-to-end case, which lives here because it exercises SynapseTraitPolicy).
    /// Case names are unchanged — they map to the issues whose test plans they satisfy.
    /// </summary>
    [SynapseTestSet]
    public static class PsychologySplitCases
    {
        private const long Day = 60000L;

        public static IEnumerable<SynapseTestCase> All()
        {
            // The reference consumer: Psychology registered itself at load, compatibly (Core #91).
            yield return new SynapseTestCase("Psychology_RegistersInCompatRegistry", () =>
            {
                var reg = SynapseCompatRegistry.Get("rimsynapse.psychology");
                Assert.NotNull(reg, "Psychology self-registered into Core's registry at load");
                Assert.Equal(PsychologyCompat.RequiredCoreContract, reg.RequiredCoreContract,
                    "the registered required-contract matches Psychology's declared one");
                Assert.True(reg.RequiredCoreContract <= SynapseCompatRegistry.CoreApiContract,
                    $"Psychology (needs API {reg.RequiredCoreContract}) is compatible with Core (API {SynapseCompatRegistry.CoreApiContract})");
                Assert.True(PsychologyCompat.IsCoreCompatible, "Psychology probed Core and found itself compatible");
                return $"psychology needs API {reg.RequiredCoreContract}, core API {SynapseCompatRegistry.CoreApiContract}, compatible";
            });

            // Under -quicktest the LLM is mocked and the ceremony prompt matches none of the
            // mock's keyword branches, so it answers {"success": true} — every field null.
            // The autotest fires a ceremony on load, so if that path still dereferences a
            // missing narrative it shows up here as a logged failure.
            yield return new SynapseTestCase("Psychology_CeremonyRecordSurvivesEmptyResponse", () =>
            {
                var failures = TestLog.RecentLines()
                    .Where(l => l.IndexOf("Failed to apply ceremony record", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                Assert.True(failures.Count == 0,
                    "ceremony record application threw: " + string.Join(" | ", failures.Take(2).Select(Shorten)));

                return "ceremony path handled the response without throwing";
            });

            // Voice mapping (Conversations#33): pace->speed, timbre->blend, base-voice assignment.
            yield return new SynapseTestCase("Psychology_VoiceProfileMapping", () =>
            {
                Assert.True(Math.Abs(VoiceProfileBuilder.SpeedFromPace("fast") - 1.15f) < 0.001f, "fast pace");
                Assert.True(Math.Abs(VoiceProfileBuilder.SpeedFromPace("slow") - 0.9f) < 0.001f, "slow pace");
                Assert.True(Math.Abs(VoiceProfileBuilder.SpeedFromPace("measured") - 1f) < 0.001f, "measured pace");

                Map map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
                Assert.True(map != null, "no map available");
                var p = map.mapPawns.FreeColonists.FirstOrDefault();
                Assert.True(p != null, "no colonist available");
                var core = p.TryGetComp<SynapseCorePawnComp>();
                Assert.True(core != null, "no core comp");

                string sVoice = core.voiceProfile, sKok = core.kokoroVoice, sBlend = core.kokoroBlendVoice;
                float sSpeed = core.kokoroSpeed, sW = core.kokoroBlendWeight; bool sGen = core.voiceGenerated;
                try
                {
                    // Fix a base voice that is neither a firmer nor softer reference, so gruff must blend.
                    core.kokoroVoice = p.gender == Gender.Male ? "am_adam" : "af_bella";
                    core.voiceProfile = null; core.voiceGenerated = false;

                    VoiceProfileBuilder.ApplyVoiceProfile(p, core, "Terse and dry.", "fast", "gruff");
                    Assert.Equal("Terse and dry.", core.voiceProfile, "style stored");
                    Assert.True(global::RimSynapse.KokoroVoices.IsKnown(core.kokoroVoice), "base voice is a known id");
                    Assert.True(Math.Abs(core.kokoroSpeed - 1.15f) < 0.001f, "fast -> 1.15 speed");
                    Assert.True(core.kokoroBlendWeight > 0f && global::RimSynapse.KokoroVoices.IsKnown(core.kokoroBlendVoice)
                        && core.kokoroBlendVoice != core.kokoroVoice, "gruff -> valid non-self blend");
                    Assert.True(core.voiceGenerated, "voiceGenerated set");

                    VoiceProfileBuilder.ApplyVoiceProfile(p, core, null, "slow", "flat");
                    Assert.True(Math.Abs(core.kokoroSpeed - 0.9f) < 0.001f, "slow -> 0.9 speed");
                    Assert.True(core.kokoroBlendWeight == 0f && string.IsNullOrEmpty(core.kokoroBlendVoice), "flat -> no blend");
                    Assert.Equal("Terse and dry.", core.voiceProfile, "null style preserves prior style");
                    return $"speed/blend mapping ok; base={core.kokoroVoice}";
                }
                finally
                {
                    core.voiceProfile = sVoice; core.kokoroVoice = sKok; core.kokoroBlendVoice = sBlend;
                    core.kokoroSpeed = sSpeed; core.kokoroBlendWeight = sW; core.voiceGenerated = sGen;
                }
            });

            // Each WorkDomain resolves to a graduated distaste SPECTRUM (no hard block) and a separate
            // terminal INCAPABLE trait that DOES disable the work type, plus a real WorkTypeDef.
            yield return new SynapseTestCase("Psychology_SkillAxisMap_AversionFamilyResolves", () =>
            {
                int chec1 = 0;
                foreach (var d in SynapseSkillAxisMap.WorkDomains)
                {
                    var distaste = DefDatabase<TraitDef>.GetNamedSilentFail(d.aversionTraitDef);
                    Assert.True(distaste != null, $"distaste trait '{d.aversionTraitDef}' is defined");
                    Assert.True(distaste.degreeDatas != null && distaste.degreeDatas.Count >= 2,
                        $"'{d.aversionTraitDef}' is a graduated spectrum (reluctant -> averse)");
                    bool distasteDisables = (distaste.disabledWorkTypes != null && distaste.disabledWorkTypes.Count > 0)
                        || distaste.disabledWorkTags != WorkTags.None;
                    Assert.False(distasteDisables, $"'{d.aversionTraitDef}' must NOT hard-disable work (graduated, not binary)");

                    var incap = DefDatabase<TraitDef>.GetNamedSilentFail(d.incapableTraitDef);
                    Assert.True(incap != null, $"incapable trait '{d.incapableTraitDef}' is defined");
                    bool incapDisables = (incap.disabledWorkTypes != null && incap.disabledWorkTypes.Count > 0)
                        || incap.disabledWorkTags != WorkTags.None;
                    Assert.True(incapDisables, $"'{d.incapableTraitDef}' disables its work type");

                    var wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(d.workTypeDef);
                    Assert.True(wt != null, $"work type '{d.workTypeDef}' exists");
                    chec1++;
                }
                Assert.True(chec1 >= 10, "the full data-driven aversion family is present");
                return $"validated {chec1} work domains";
            });

            // The unease thought def shipped and has escalating stages.
            yield return new SynapseTestCase("Psychology_SkillEngine_UneaseThoughtHasStages", () =>
            {
                var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("Synapse_Unease");
                Assert.True(def != null, "Synapse_Unease thought def exists");
                Assert.True(def.stages != null && def.stages.Count >= 2, "unease has multiple escalating stages");
                return $"stages={def.stages.Count}";
            });

            // Passion scaling: Major = 1.0, and a stronger (modded) passion scales above it.
            yield return new SynapseTestCase("Psychology_SkillAxisMap_PassionScale", () =>
            {
                Assert.Equal(1.0f, SynapseSkillAxisMap.PassionScale(Passion.Major), "Major passion scales at 1.0");
                Assert.True(SynapseSkillAxisMap.IsStrongPassion(Passion.Major), "Major counts as a strong passion");
                Assert.False(SynapseSkillAxisMap.IsStrongPassion(Passion.Minor), "Minor does not");
                return "scale ok";
            });

            // #45: the consistency gate floors today's evidence when the model says unlikely.
            yield return new SynapseTestCase("Psychology_ConsistencyGateFloorsUnlikely", () =>
            {
                Assert.Equal(0f, SynapseTraitPolicy.GateDailyPressure("none", 0.9f), "none => 0");
                Assert.Equal(0f, SynapseTraitPolicy.GateDailyPressure("low", 0.9f), "low => 0");
                Assert.Equal(0.9f, SynapseTraitPolicy.GateDailyPressure("high", 0.9f), "high passes through");
                Assert.Equal(0.5f, SynapseTraitPolicy.GateDailyPressure("moderate", 0.5f), "moderate passes through");
                return "gate ok";
            });

            // #46: the whitelist blocks dangerous/identity traits; #44: protected traits are flagged.
            yield return new SynapseTestCase("Psychology_TraitPolicyWhitelistAndProtection", () =>
            {
                Assert.True(SynapseTraitPolicy.IsWhitelisted("Bloodlust"), "Bloodlust is AI-modifiable");
                Assert.False(SynapseTraitPolicy.IsWhitelisted("Psychopath"), "Psychopath can never be AI-added");
                Assert.True(SynapseTraitPolicy.IsProtected("Nerves"), "Nerves (nerves of steel) is protected");
                Assert.True(SynapseTraitPolicy.ResistanceFactor("Nerves") > 0.5f, "Nerves strongly resists");
                Assert.False(SynapseTraitPolicy.IsProtected("Kind"), "Kind is not protected");
                return "policy ok";
            });

            // #45 end-to-end at the unit level: a single gated day never crosses; sustained days do.
            // Core-named (Core #79's threshold), but the gate half is SynapseTraitPolicy — so it runs here.
            yield return new SynapseTestCase("Core_SustainedPressureCrossesButOneDayDoesNot", () =>
            {
                // One gated "unlikely" day contributes nothing.
                var lone = new SynapseCorePawnComp();
                float gated = SynapseTraitPolicy.GateDailyPressure("low", 0.9f);
                float p = lone.AccumulateTraitPressure("Bloodlust", gated, "add", 0f, Day);
                Assert.True(p < 1.0f, "a single 'unlikely' day cannot reach the shift threshold");

                // Three sustained real days do cross 1.0 (0.5 -> 0.8 -> 1.1).
                var streak = new SynapseCorePawnComp();
                streak.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, Day);
                streak.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, 2 * Day);
                float p3 = streak.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, 3 * Day);
                Assert.True(p3 >= 1.0f, $"sustained multi-day evidence crosses the threshold (was {p3:0.00})");
                return $"gatedDay={p:0.00}, day3={p3:0.00}";
            });
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "<empty>";
            var oneLine = s.Replace("\r", " ").Replace("\n", " ");
            return oneLine.Length <= 160 ? oneLine : oneLine.Substring(0, 160) + "...";
        }
    }
}
