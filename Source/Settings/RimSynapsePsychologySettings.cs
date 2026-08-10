using Verse;

namespace RimSynapse.Psychology.Settings
{
    public class RimSynapsePsychologySettings : ModSettings
    {
        public bool enableDebugLogging = false;
        public float memoryDecayMultiplier = 1.0f;   // also serves as the short-term decay global multiplier
        public float sensitivityThreshold = 0.5f;

        // ── Stage 3 balance knobs (design §7) ──────────────────────────
        public float shiftThreshold = 1.0f;            // trait pressure needed to fire a change
        public float shiftPressureDecay = 0.2f;        // per-day decay of accumulated trait pressure
        public float traitShiftChancePerDay = 0.005f;  // once at threshold, daily chance a permanent shift fires (0 = never; mood-only)
        public float copingChancePerDay = 0.15f;        // once at threshold on a work-stressor, daily chance a temporary coping (strike/break) fires
        public int aversionBreakDays = 2;               // how long a temporary break/strike lasts before timing out
        public float consolidationThreshold = 1.0f;    // salience needed to consolidate a memory long-term
        public int referenceThreshold = 3;             // reference count that consolidates a memory
        public float abandonmentThreshold = 90f;       // AbandonmentRiskScore above which a pawn may leave
        public float suicideDamageMultiplier = 5.0f;   // damage multiplier on the suicide self-harm job
        public float opinionTrustBlend = 0.5f;         // 0 = pure vanilla opinion, 1 = pure Synapse trust
        public int evalCadence = 1;                    // run the daily eval every N days (1 = nightly)

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableDebugLogging, "enableDebugLogging", false);
            Scribe_Values.Look(ref memoryDecayMultiplier, "memoryDecayMultiplier", 1.0f);
            Scribe_Values.Look(ref sensitivityThreshold, "sensitivityThreshold", 0.5f);

            Scribe_Values.Look(ref shiftThreshold, "shiftThreshold", 1.0f);
            Scribe_Values.Look(ref shiftPressureDecay, "shiftPressureDecay", 0.2f);
            Scribe_Values.Look(ref traitShiftChancePerDay, "traitShiftChancePerDay", 0.005f);
            Scribe_Values.Look(ref consolidationThreshold, "consolidationThreshold", 1.0f);
            Scribe_Values.Look(ref referenceThreshold, "referenceThreshold", 3);
            Scribe_Values.Look(ref abandonmentThreshold, "abandonmentThreshold", 90f);
            Scribe_Values.Look(ref suicideDamageMultiplier, "suicideDamageMultiplier", 5.0f);
            Scribe_Values.Look(ref opinionTrustBlend, "opinionTrustBlend", 0.5f);
            Scribe_Values.Look(ref evalCadence, "evalCadence", 1);
            base.ExposeData();
            ApplyToCore();
        }

        /// <summary>
        /// Mirror Core-owned knobs into Core (Psychology depends on Core, the allowed direction, so Core
        /// exposes statics rather than reaching back). Call after load and whenever a slider changes.
        /// </summary>
        public void ApplyToCore()
        {
            RimSynapse.Comps.SynapseCorePawnComp.MemoryDecayMultiplier = memoryDecayMultiplier;
            RimSynapse.Comps.SynapseCorePawnComp.ConsolidationThreshold = consolidationThreshold;
            RimSynapse.Comps.SynapseCorePawnComp.ReferenceThreshold = referenceThreshold;
            RimSynapse.Comps.SynapseCorePawnComp.TraitPressureDecayPerDay = shiftPressureDecay;
        }
    }
}
