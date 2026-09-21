using Verse;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Compulsion control (#72): a per-pawn "emotional brake" in [0,1] — 0 = volatile/impulsive (acts on
    /// feelings), 1 = controlled/stable (feels them but suppresses). It does NOT change how a pawn FEELS about
    /// someone (that is the warmth/trust compass); it gates whether those feelings ERUPT into action — a social
    /// slight, a refusal to help, a mental break aimed at one pawn.
    ///
    /// <para>Architecture (the mod's house pattern): the DETERMINISTIC baseline comes from the vanilla stability
    /// traits — the same <c>mentalBreakThresholdOffset</c> the game already uses — so it works with no model.
    /// The LLM eval may later REFINE a pawn's temperament by writing <see cref="SynapsePawnComp.compulsionControl"/>;
    /// but every actual TRIGGER is a deterministic C# check that only READS the effective value.</para>
    /// </summary>
    public static class SynapseCompulsion
    {
        /// <summary>Feelings this strong (0..1) or stronger can, in a volatile pawn, erupt into an action.</summary>
        public const float ActThreshold = 0.5f;

        /// <summary>Neutral minor-break threshold (vanilla default) that maps to mid control (0.5).</summary>
        private const float NeutralBreakThreshold = 0.35f;

        /// <summary>
        /// Deterministic control baseline in [0,1], read from the pawn's minor mental-break threshold — which the
        /// game already computes from every stability trait (Iron-Willed/Steadfast steady them → LOWER threshold →
        /// higher control; Nervous/Neurotic → HIGHER threshold → lower control). Reading the folded-in vanilla
        /// value means modded stability traits count too, with no hand-listed trait names. Non-humanlikes (no
        /// mental breaker) read as neutral 0.5.
        /// </summary>
        public static float Baseline(Pawn pawn)
        {
            var breaker = pawn?.mindState?.mentalBreaker;
            if (breaker == null) return 0.5f;
            float minor = breaker.BreakThresholdMinor; // ~0.35 neutral; lower = steadier
            // Map onto [0,1] so the neutral threshold is 0.5 and a steadier pawn scores higher.
            float control = 1f - minor / (NeutralBreakThreshold * 2f);
            return Clamp01(control);
        }

        /// <summary>The effective control: the LLM-refined value stored on the comp if set (>= 0), else the
        /// deterministic <see cref="Baseline"/>. Triggers call this, never the raw fields.</summary>
        public static float Effective(Pawn pawn, SynapsePawnComp comp)
        {
            if (comp != null && comp.compulsionControl >= 0f) return Clamp01(comp.compulsionControl);
            return Baseline(pawn);
        }

        /// <summary>Convenience overload: resolve the comp from the pawn.</summary>
        public static float Effective(Pawn pawn) => Effective(pawn, pawn?.GetComp<SynapsePawnComp>());

        /// <summary>
        /// The C# TRIGGER, pure and unit-testable: given a feeling's magnitude (0..1 — e.g. how strongly negative
        /// the warmth toward someone is) and the pawn's control, does it erupt into an action? The drive to act is
        /// <c>magnitude × (1 - control)</c>; it fires once that reaches <see cref="ActThreshold"/>. So only strong
        /// feelings in low-control pawns act out; a controlled pawn feels the same thing and stays civil.
        /// </summary>
        public static bool WouldActOn(float feelingMagnitude, float control)
        {
            return Drive(feelingMagnitude, control) >= ActThreshold;
        }

        /// <summary>The raw drive-to-act, <c>magnitude × (1 - control)</c>, in [0,1]; exposed for tuning/UI.</summary>
        public static float Drive(float feelingMagnitude, float control)
        {
            return Clamp01(feelingMagnitude) * (1f - Clamp01(control));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
