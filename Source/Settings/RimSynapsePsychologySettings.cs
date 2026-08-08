using Verse;

namespace RimSynapse.Psychology.Settings
{
    public class RimSynapsePsychologySettings : ModSettings
    {
        public bool enableDebugLogging = false;
        public float memoryDecayMultiplier = 1.0f;
        public float sensitivityThreshold = 0.5f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableDebugLogging, "enableDebugLogging", false);
            Scribe_Values.Look(ref memoryDecayMultiplier, "memoryDecayMultiplier", 1.0f);
            Scribe_Values.Look(ref sensitivityThreshold, "sensitivityThreshold", 0.5f);
            base.ExposeData();
            ApplyToCore();
        }

        /// <summary>
        /// Mirror the player's "Memory Decay Speed" into Core, which owns memory decay. Psychology
        /// depends on Core (the allowed direction), so Core exposes a static rather than reaching back.
        /// Call after load and whenever the slider changes.
        /// </summary>
        public void ApplyToCore()
        {
            RimSynapse.Comps.SynapseCorePawnComp.MemoryDecayMultiplier = memoryDecayMultiplier;
        }
    }
}
