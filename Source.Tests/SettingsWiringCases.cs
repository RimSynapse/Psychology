using System.Collections.Generic;
using RimSynapse.Comps;
using RimSynapse.Psychology.Settings;
using RimAgentic.Testing;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// Stage 3 (#48): the balance-knob settings must actually reach their behavior. The Core-owned
    /// knobs are mirrored into Core statics via ApplyToCore; this proves that wiring end-to-end.
    /// </summary>
    [SynapseTestSet]
    public static class SettingsWiringCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Psychology_SettingsApplyToCore", () =>
            {
                // Snapshot the globals we are about to mutate, and restore them afterward.
                float mdm = SynapseCorePawnComp.MemoryDecayMultiplier;
                float ct = SynapseCorePawnComp.ConsolidationThreshold;
                int rt = SynapseCorePawnComp.ReferenceThreshold;
                float pd = SynapseCorePawnComp.TraitPressureDecayPerDay;
                try
                {
                    var s = new RimSynapsePsychologySettings
                    {
                        memoryDecayMultiplier = 2.5f,
                        consolidationThreshold = 1.75f,
                        referenceThreshold = 6,
                        shiftPressureDecay = 0.4f,
                    };
                    s.ApplyToCore();

                    Assert.Equal(2.5f, SynapseCorePawnComp.MemoryDecayMultiplier, "decay multiplier reaches Core");
                    Assert.Equal(1.75f, SynapseCorePawnComp.ConsolidationThreshold, "consolidation threshold reaches Core");
                    Assert.Equal(6, SynapseCorePawnComp.ReferenceThreshold, "reference threshold reaches Core");
                    Assert.Equal(0.4f, SynapseCorePawnComp.TraitPressureDecayPerDay, "pressure decay reaches Core");
                    return "all four Core-owned knobs wired";
                }
                finally
                {
                    SynapseCorePawnComp.MemoryDecayMultiplier = mdm;
                    SynapseCorePawnComp.ConsolidationThreshold = ct;
                    SynapseCorePawnComp.ReferenceThreshold = rt;
                    SynapseCorePawnComp.TraitPressureDecayPerDay = pd;
                }
            });
        }
    }
}
