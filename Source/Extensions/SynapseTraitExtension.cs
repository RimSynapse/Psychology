using RimWorld;
using Verse;

namespace RimSynapse.Psychology.Extensions
{
    /// <summary>
    /// Opt-in psychology behaviour a TraitDef can declare via <c>modExtensions</c>. Kept deliberately small:
    /// every field here MUST be read by real code — an unread field is a false affordance (it looks like a
    /// supported hook but silently does nothing), the exact bug #64 fixed. Add a field only together with the
    /// code that consumes it.
    /// </summary>
    public class SynapseTraitExtension : DefModExtension
    {
        /// <summary>The trait drives the euphoric-reckless path (SynapseBreakManager). Data-driven so any
        /// trait — ours or a mod's — can opt in, instead of the manager hardcoding specific defs (#64).</summary>
        public bool causesEuphoria = false;

        /// <summary>True when any of the pawn's traits declares <see cref="causesEuphoria"/> through this
        /// extension. The replacement for hardcoding specific bipolar defs.</summary>
        public static bool AnyTraitCausesEuphoria(Pawn pawn)
        {
            var traits = pawn?.story?.traits?.allTraits;
            if (traits == null) return false;
            for (int i = 0; i < traits.Count; i++)
            {
                var ext = traits[i]?.def?.GetModExtension<SynapseTraitExtension>();
                if (ext != null && ext.causesEuphoria) return true;
            }
            return false;
        }
    }
}
