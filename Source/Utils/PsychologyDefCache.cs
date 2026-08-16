using RimWorld;
using Verse;

namespace RimSynapse.Psychology
{
    /// <summary>
    /// Cached def lookups for hot per-pawn paths (break evaluation, mental-state ticks).
    /// Resolving these by name via <c>DefDatabase.GetNamed*</c> on every tick showed up in
    /// the 0.8 performance pass; caching them once removes the per-pawn string->def lookups.
    ///
    /// Resolved with SilentFail because several are optional (expansion- or mod-defined).
    /// Callers null-check where it matters: <see cref="TraitSet.HasTrait(TraitDef)"/> is
    /// false-safe on a null def, and mental-state starts are guarded.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PsychologyDefCache
    {
        public static readonly TraitDef Bipolar = DefDatabase<TraitDef>.GetNamedSilentFail("Bipolar");
        public static readonly TraitDef Synapse_Bipolar = DefDatabase<TraitDef>.GetNamedSilentFail("Synapse_Bipolar");
        public static readonly TraitDef Depressive = DefDatabase<TraitDef>.GetNamedSilentFail("Depressive");
        public static readonly TraitDef Pessimist = DefDatabase<TraitDef>.GetNamedSilentFail("Pessimist");
        public static readonly TraitDef Bloodlust = DefDatabase<TraitDef>.GetNamedSilentFail("Bloodlust");
        public static readonly TraitDef Psychopath = DefDatabase<TraitDef>.GetNamedSilentFail("Psychopath");

        public static readonly MentalStateDef Synapse_EuphoricReckless = DefDatabase<MentalStateDef>.GetNamedSilentFail("Synapse_EuphoricReckless");
    }
}
