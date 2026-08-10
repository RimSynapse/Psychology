using Verse;

namespace RimSynapse.Psychology.Models
{
    /// <summary>
    /// The LLM's pre-staged opinion on one building trait candidate (Phase 2). The measured engine still
    /// decides IF/WHEN a change fires; this only supplies the JUDGEMENT (is it in character?) and the
    /// NARRATION (flavour text) the engine consults at fire time. Keyed by candidate id on the pawn comp.
    /// </summary>
    public class TraitJudgmentRecord : IExposable
    {
        public string verdict;  // "in_character" | "out_of_character" | "uncertain"
        public string flavor;   // one in-character sentence describing the change, for the letter
        public int tick;        // when the LLM produced it (for staleness)

        public void ExposeData()
        {
            Scribe_Values.Look(ref verdict, "verdict");
            Scribe_Values.Look(ref flavor, "flavor");
            Scribe_Values.Look(ref tick, "tick", 0);
        }
    }
}
