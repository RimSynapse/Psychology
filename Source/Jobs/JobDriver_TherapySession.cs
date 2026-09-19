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
        private Thing SeatA => job.GetTarget(TargetIndex.B).Thing;
        private Thing SeatB => job.GetTarget(TargetIndex.C).Thing;

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

            // Find seating for both
            yield return new Toil
            {
                initAction = delegate
                {
                    Thing seat1 = GenClosest.ClosestThingReachable(TargetPawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial), PathEndMode.OnCell, TraverseParms.For(pawn), 20f, 
                        t => t.def.building != null && t.def.building.isSittable && (t as RimWorld.Building_Bed) == null && pawn.CanReserve(t) && TargetPawn.CanReserve(t));
                    
                    Thing seat2 = null;
                    if (seat1 != null)
                    {
                        seat2 = GenClosest.ClosestThingReachable(seat1.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial), PathEndMode.OnCell, TraverseParms.For(pawn), 4f, 
                            t => t != seat1 && t.def.building != null && t.def.building.isSittable && (t as RimWorld.Building_Bed) == null && pawn.CanReserve(t) && TargetPawn.CanReserve(t));
                    }

                    if (seat1 != null && seat2 != null)
                    {
                        job.SetTarget(TargetIndex.B, seat1);
                        job.SetTarget(TargetIndex.C, seat2);
                    }
                    else
                    {
                        job.SetTarget(TargetIndex.B, pawn.Position);
                        job.SetTarget(TargetIndex.C, TargetPawn.Position);
                    }
                }
            };

            // Path to seating
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

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

                Job waitJob = null;
                if (SeatB != null && SeatB.def != null && SeatB.def.building != null && SeatB.def.building.isSittable)
                {
                    waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat, 4000); 
                    TargetPawn.jobs.StartJob(waitJob, JobCondition.InterruptForced);
                    
                    pawn.pather.StartPath(SeatA, PathEndMode.OnCell);
                }
                else
                {
                    waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat, 4000);
                    TargetPawn.jobs.StartJob(waitJob, JobCondition.InterruptForced);
                    pawn.rotationTracker.FaceCell(TargetPawn.Position);
                    TargetPawn.rotationTracker.FaceCell(pawn.Position);
                }
            };
            
            chatToil.tickAction = delegate
            {
                if (SeatA == null || SeatA.def == null || !SeatA.def.building.isSittable)
                {
                    pawn.rotationTracker.FaceCell(TargetPawn.Position);
                    TargetPawn.rotationTracker.FaceCell(pawn.Position);
                }
                
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

            float baseChance = 0.30f; // 30% Base
            
            // Therapist Skill
            int socialSkill = pawn.skills != null ? pawn.skills.GetSkill(SkillDefOf.Social).Level : 0;
            float skillFactor = socialSkill * 0.03f;
            
            // Privacy Bonus
            float privacyBonus = 0f;
            var room = pawn.GetRoom();
            if (room != null)
            {
                bool isPatientRoom = TargetPawn.ownership != null && TargetPawn.ownership.OwnedRoom == room;
                bool isSecure = room.Role != RoomRoleDefOf.None && room.RegionCount > 0;
                
                int pawnCount = 0;
                foreach (Thing t in room.ContainedAndAdjacentThings)
                {
                    if (t is Pawn p && p.RaceProps.Humanlike && p.Awake())
                        pawnCount++;
                }
                
                if (isPatientRoom || (isSecure && pawnCount == 2))
                {
                    privacyBonus = 0.20f; // 20% bonus for privacy
                }
            }

            // Trust Factor
            float trustBonus = 0f;
            var tComp = TargetPawn.GetComp<SynapsePawnComp>();
            if (tComp != null && tComp.socialNetwork != null)
            {
                string pId = pawn.GetUniqueLoadID();
                if (tComp.socialNetwork.ContainsKey(pId))
                {
                    float trust = tComp.socialNetwork[pId].trust; // -100 to 100
                    trustBonus = (trust / 100f) * 0.15f; // Up to +15% or -15%
                }
            }

            float successChance = baseChance + skillFactor + privacyBonus + trustBonus;
            
            bool success = Rand.Chance(successChance);
            float moodPct = TargetPawn.needs != null && TargetPawn.needs.mood != null ? TargetPawn.needs.mood.CurLevelPercentage : 0.5f;

            if (success)
            {
                // Apply successful thought with inverse mood scaling
                var successDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("Synapse_SuccessfulTherapy");
                if (successDef != null && TargetPawn.needs != null && TargetPawn.needs.mood != null)
                {
                    var memory = (Thought_Memory)ThoughtMaker.MakeThought(successDef);
                    // If miserable (0%), power factor = 2.0. If happy (100%), power factor = ~0.0.
                    memory.moodPowerFactor = (1.0f - moodPct) * 2f; 
                    TargetPawn.needs.mood.thoughts.memories.TryGainMemory(memory);
                }

                MoteMaker.ThrowText(TargetPawn.DrawPos, TargetPawn.Map, "Therapy Successful", 4f);
                
                // Attempt to cure a trait
                float cureChance = 0.10f * moodPct; // Happier = higher cure chance
                if (Rand.Chance(cureChance) && TargetPawn.story != null && TargetPawn.story.traits != null)
                {
                    List<Trait> curableTraits = new List<Trait>();
                    foreach (Trait t in TargetPawn.story.traits.allTraits)
                    {
                        if (IsCurablePsychologicalTrait(TargetPawn, t))
                        {
                            curableTraits.Add(t);
                        }
                    }

                    if (curableTraits.Count > 0)
                    {
                        Trait curedTrait = curableTraits.RandomElement();
                        TargetPawn.story.traits.allTraits.Remove(curedTrait);
                        Messages.Message($"{TargetPawn.NameShortColored} was cured of {curedTrait.Label} thanks to successful therapy from {pawn.NameShortColored}!", TargetPawn, MessageTypeDefOf.PositiveEvent);
                        
                        // Inject into Trait History Timeline
                        var coreComp = TargetPawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
                        if (coreComp != null)
                        {
                            long currentTick = Find.TickManager?.TicksAbs ?? 0;
                            coreComp.AddMemory(new RimSynapse.Models.WeightedMemory
                            {
                                summary = $"Cured of {curedTrait.Label} through successful therapy.",
                                weight = 1.0f,
                                baseWeight = 1.0f,
                                decayRate = 0f,
                                isLongTerm = true, // Timeline memories never decay
                                tags = new List<string> { "TraitShift", "Therapy", "Recovery" },
                                memoryType = "TraitLost",
                                absTick = currentTick,
                                gameTick = Find.TickManager?.TicksGame ?? 0
                            });
                        }
                    }
                }
            }
            else
            {
                var failDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("Synapse_AwkwardTherapy");
                if (failDef != null && TargetPawn.needs != null && TargetPawn.needs.mood != null)
                {
                    TargetPawn.needs.mood.thoughts.memories.TryGainMemory(failDef);
                }
                MoteMaker.ThrowText(TargetPawn.DrawPos, TargetPawn.Map, "Therapy Failed", 4f);
            }

            // #17: every completed session — Guiding Hand, Watch, or Background — is remembered by BOTH
            // participants (a Therapy-tagged memory + transcript). Deterministic backbone; the LLM dialogue,
            // when there was one, only supplies the transcript lines.
            API.SynapseTherapy.RecordSession(pawn, TargetPawn, success, backgroundChatLog);
        }

        private bool IsCurablePsychologicalTrait(Pawn pawn, Trait t)
        {
            string defName = t.def.defName;
            
            // Custom PTSD
            if (defName == "Synapse_PTSD") return true;
            
            // Vanilla Psychopath and Bloodlust
            if (defName == "Psychopath" || defName == "Bloodlust")
            {
                // Check if backstory locked
                string childId = pawn.story?.Childhood?.identifier ?? "";
                string adultId = pawn.story?.Adulthood?.identifier ?? "";
                
                if (childId.Contains("Assassin") || adultId.Contains("Assassin") || 
                    childId.Contains("Killer") || adultId.Contains("Killer"))
                {
                    return false; // Locked by homicidal backstory
                }
                return true;
            }
            
            // Vanilla NaturalMood (Depressive = -2, Pessimist = -1)
            if (defName == "NaturalMood" && (t.Degree == -2 || t.Degree == -1))
            {
                return true;
            }
            
            // Vanilla Nerves (Volatile = -2, Nervous = -1)
            if (defName == "Nerves" && (t.Degree == -2 || t.Degree == -1))
            {
                return true;
            }
            
            return false;
        }
    }
}
