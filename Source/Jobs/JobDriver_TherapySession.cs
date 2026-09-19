using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.Jobs
{
    /// <summary>How a therapy session plays out (#17). Passed in via <see cref="Verse.AI.Job.count"/> when the
    /// session is ordered, then read once at session start. Watch/Guiding open the live dialogue window;
    /// Background runs the whole thing headless. All three converge on the same mechanical outcome and both
    /// participants record a Therapy memory + transcript.</summary>
    public enum TherapyMode { Background = 0, Watch = 1, GuidingHand = 2 }

    public class JobDriver_TherapySession : JobDriver
    {
        private Pawn TargetPawn => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        // #17: the chosen auto-mode. Ordered sessions stash it in job.count; read once at chat start.
        public TherapyMode sessionMode = TherapyMode.Background;
        public bool backgroundResolution = false;
        private List<string> backgroundChatLog = null;
        private UI.Dialog_TherapySession openDialog = null;
        private bool outcomeResolved = false;

        /// <summary>The player pushed a live session to the background: drop the window, let the job finish
        /// headless. The transcript so far is carried into the completion memory.</summary>
        public void EnableBackgroundResolution(List<string> chatLog)
        {
            backgroundResolution = true;
            sessionMode = TherapyMode.Background;
            backgroundChatLog = chatLog;
            openDialog = null;
        }

        public void EndJobManually(List<string> chatLog)
        {
            // Manual window-close finishes the session (its finish action resolves the outcome). Guard against
            // re-entry: once the outcome has resolved, closing the window must not try to end the job again.
            if (outcomeResolved) return;
            if (pawn?.jobs?.curDriver == this)
                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnDowned(TargetIndex.A);
            this.FailOnNotAwake(TargetIndex.A);

            // #17 redesign: therapy is held in the PATIENT'S OWN BEDROOM (not a medical bed). Meet at a cell
            // beside their bed and send them home to wait there; the bedroom is where the privacy bonus lives.
            yield return new Toil
            {
                initAction = delegate
                {
                    IntVec3 meet = TargetPawn.Position;
                    Building_Bed bed = TargetPawn.ownership?.OwnedBed;
                    if (bed != null && bed.Spawned)
                    {
                        foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(bed))
                        {
                            if (c.InBounds(pawn.Map) && c.Standable(pawn.Map)
                                && pawn.CanReserveAndReach(c, PathEndMode.OnCell, Danger.Deadly))
                            { meet = c; break; }
                        }
                        Job goHome = JobMaker.MakeJob(JobDefOf.Goto, bed.Position);
                        TargetPawn.jobs.StartJob(goHome, JobCondition.InterruptForced);
                    }
                    job.SetTarget(TargetIndex.B, meet);
                }
            };

            // Therapist walks to the bedside meeting cell.
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil chatToil = new Toil();
            chatToil.initAction = delegate
            {
                // #17: mode was stashed in job.count by whoever ordered the session (float menu / debug).
                // Background stays headless; Watch/Guiding open the live dialogue window on the therapist.
                sessionMode = (TherapyMode)job.count;
                if (sessionMode != TherapyMode.Background && pawn.IsColonistPlayerControlled && Find.WindowStack != null)
                {
                    openDialog = new UI.Dialog_TherapySession(pawn, TargetPawn, this, sessionMode == TherapyMode.GuidingHand);
                    Find.WindowStack.Add(openDialog);
                }

                Job waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat, 4000);
                TargetPawn.jobs.StartJob(waitJob, JobCondition.InterruptForced);
                pawn.rotationTracker.FaceCell(TargetPawn.Position);
                TargetPawn.rotationTracker.FaceCell(pawn.Position);
            };

            chatToil.tickAction = delegate
            {
                pawn.rotationTracker.FaceCell(TargetPawn.Position);
                TargetPawn.rotationTracker.FaceCell(pawn.Position);
                pawn.needs?.joy?.GainJoy(0.0001f, JoyKindDefOf.Social);
                TargetPawn.needs?.joy?.GainJoy(0.0001f, JoyKindDefOf.Social);
            };

            chatToil.defaultCompleteMode = ToilCompleteMode.Delay;
            chatToil.defaultDuration = 5000;

            chatToil.AddFinishAction(() => 
            {
                CalculateTherapyOutcome();
            });
            
            yield return chatToil;
        }

        private void CalculateTherapyOutcome()
        {
            // Fires once per session — the toil finish action and a manual window-close can both land here.
            if (outcomeResolved) return;
            outcomeResolved = true;

            // A live window (Watch/Guiding) showing a session that has now concluded should close.
            if (openDialog != null)
            {
                try { openDialog.Close(false); } catch { }
                openDialog = null;
            }

            if (TargetPawn.Dead || pawn.Dead) return;

            // #17 redesign: therapy is treatment, not a coin flip. Compute a WEIGHTED quality (skill, the
            // bedroom's privacy, trust, the patient's receptiveness) and apply it to the patient's worst
            // untreated condition. Reaching zero severity cures it (hediff + seeding trait lift). With no
            // condition present, it's a general supportive session whose only effect is the mood thought.
            Room room = TargetPawn.GetRoom() ?? pawn.GetRoom();
            float quality = API.SynapseTherapyConditions.Quality(pawn, TargetPawn, room);
            var target = API.SynapseTherapyConditions.MostSevere(TargetPawn);

            bool helped = target != null
                ? API.SynapseTherapyConditions.Treat(pawn, TargetPawn, target, quality)
                : quality >= 0.4f;

            // Immediate mood feedback scaled by how the session went.
            if (TargetPawn.needs?.mood != null)
            {
                if (quality >= 0.35f)
                {
                    var successDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("Synapse_SuccessfulTherapy");
                    if (successDef != null)
                    {
                        var memory = (Thought_Memory)ThoughtMaker.MakeThought(successDef);
                        memory.moodPowerFactor = 0.5f + quality; // a better session lands harder
                        TargetPawn.needs.mood.thoughts.memories.TryGainMemory(memory);
                    }
                }
                else
                {
                    var failDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("Synapse_AwkwardTherapy");
                    if (failDef != null) TargetPawn.needs.mood.thoughts.memories.TryGainMemory(failDef);
                }
            }

            string mote = target != null
                ? (helped ? $"Therapy: {target.LabelBase} eased" : "Therapy: little progress")
                : (helped ? "Therapy helped" : "Therapy: awkward");
            MoteMaker.ThrowText(TargetPawn.DrawPos, TargetPawn.Map, mote, 4f);

            // #17: every completed session — Guiding Hand, Watch, or Background — is remembered by BOTH
            // participants (a Therapy-tagged memory + transcript). Deterministic backbone; the LLM dialogue,
            // when there was one, only supplies the transcript lines.
            API.SynapseTherapy.RecordSession(pawn, TargetPawn, helped, backgroundChatLog);
        }
    }
}
