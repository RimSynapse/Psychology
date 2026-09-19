using RimWorld;
using Verse;
using RimSynapse.Psychology.API;

namespace RimSynapse.Psychology.Thoughts
{
    /// <summary>
    /// A passive mood drag while a pawn carries an untreated psychological condition (#17), so an affliction
    /// weighs on them like an illness even before it ever breaks. Scales with the pawn's WORST condition:
    /// mild / moderate / severe map to the situational thought's three stages.
    /// </summary>
    public class ThoughtWorker_PsychCondition : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            var worst = SynapseTherapyConditions.MostSevere(p);
            if (worst == null || worst.Severity <= 0f) return ThoughtState.Inactive;

            float s = worst.Severity;
            int stage = s >= 0.75f ? 2 : (s >= 0.4f ? 1 : 0);
            return ThoughtState.ActiveAtStage(stage);
        }
    }
}
