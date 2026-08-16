using Verse;

namespace RimSynapse.Psychology.Models
{
    /// <summary>
    /// One active, temporarily-applied coping trait from the skill-driven trait engine — a "break"
    /// (time off a work type) or a "strike" (refusing drudge work as leverage). It lifts either when the
    /// hard <see cref="expiryTick"/> passes, or early when its <see cref="conditionSkillDefName"/> passion
    /// is finally exercised. On lift, the engine removes <see cref="traitDefName"/> from the pawn.
    /// </summary>
    public class CopingState : IExposable
    {
        public string traitDefName;          // the temp trait to remove when this lifts
        public int expiryTick;               // hard timeout (absolute-ish game tick)
        public string conditionSkillDefName; // optional: lift early once this skill is exercised (the demand is met)
        public string label;                 // short human-readable kind ("strike" / "break")

        public void ExposeData()
        {
            Scribe_Values.Look(ref traitDefName, "traitDefName");
            Scribe_Values.Look(ref expiryTick, "expiryTick", 0);
            Scribe_Values.Look(ref conditionSkillDefName, "conditionSkillDefName");
            Scribe_Values.Look(ref label, "label");
        }
    }
}
