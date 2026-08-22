using RimWorld;
using Verse;

namespace RimSynapse.Psychology.Models
{
    /// <summary>
    /// The lifecycle of one AI-added trait: when it was gained and why, and — once it is removed —
    /// when and why it was lost (#25). Removal used to delete the record outright, erasing the loss and
    /// its reasoning; instead we close the record out (<see cref="tickRemoved"/> &gt; 0) so both ends of
    /// the trait's life survive for the Patient History timeline. A record with <c>tickRemoved == 0</c>
    /// is still active — that is the set the daily review reports as "currently has".
    /// </summary>
    public class DynamicTraitRecord : IExposable
    {
        public TraitDef traitDef;
        public int tickAdded;
        public string reason;

        /// <summary>Game tick the trait was removed, or 0 while it is still held.</summary>
        public int tickRemoved;
        /// <summary>Why the trait was removed (the reasoning shown on the loss timeline entry); null while held.</summary>
        public string removalReason;

        /// <summary>True while the trait is still on the pawn (never removed).</summary>
        public bool IsActive => tickRemoved == 0;

        public DynamicTraitRecord() { }

        public DynamicTraitRecord(TraitDef traitDef, int tickAdded, string reason)
        {
            this.traitDef = traitDef;
            this.tickAdded = tickAdded;
            this.reason = reason;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref traitDef, "traitDef");
            Scribe_Values.Look(ref tickAdded, "tickAdded");
            Scribe_Values.Look(ref reason, "reason");
            // Default 0 / null so records from a save predating the timeline load as still-active gains.
            Scribe_Values.Look(ref tickRemoved, "tickRemoved", 0);
            Scribe_Values.Look(ref removalReason, "removalReason");
        }
    }
}
