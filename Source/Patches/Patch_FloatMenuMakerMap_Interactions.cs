using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using RimSynapse.Psychology.Jobs;

namespace RimSynapse.Psychology.Patches
{
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class Patch_FloatMenuMakerMap_Interactions
    {
        public static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, ref List<FloatMenuOption> __result)
        {
            if (selectedPawns == null || selectedPawns.Count != 1) return;
            Pawn pawn = selectedPawns[0];

            if (pawn.Drafted || !pawn.IsColonistPlayerControlled) return;

            IntVec3 clickCell = IntVec3.FromVector3(clickPos);
            foreach (Thing t in clickCell.GetThingList(pawn.Map))
            {
                if (t is Pawn targetPawn && targetPawn != pawn && targetPawn.RaceProps.Humanlike)
                {
                    // Target must be awake and capable
                    if (targetPawn.Downed || targetPawn.Dead || targetPawn.InMentalState || !targetPawn.Awake())
                        continue;

                    var targetComp = targetPawn.GetComp<RimSynapse.Psychology.Comps.SynapsePawnComp>();
                    if (targetComp != null && !targetComp.isTherapyReady)
                    {
                        // Add disabled option
                        __result.Add(new FloatMenuOption($"Cannot initiate therapy ({targetComp.therapyBlockReason})", null, MenuOptionPriority.Default));
                        continue;
                    }

                    string baseLabel = (targetPawn.Faction == pawn.Faction || targetPawn.IsPrisoner || targetPawn.IsSlaveOfColony)
                        ? "Initiate Therapy Session"
                        : "Attempt Recruitment / Conversion";

                    // #17: one entry per session mode. Guiding Hand = you steer each line; Watch = auto-streamed
                    // dialogue in a window; Resolve in Background = fully headless. All resolve the same outcome.
                    Pawn therapist = pawn, patient = targetPawn;
                    __result.Add(new FloatMenuOption($"{baseLabel} (Guiding Hand)", () => OrderTherapy(therapist, patient, TherapyMode.GuidingHand), MenuOptionPriority.Default));
                    __result.Add(new FloatMenuOption($"{baseLabel} (Watch)", () => OrderTherapy(therapist, patient, TherapyMode.Watch), MenuOptionPriority.Default));
                    __result.Add(new FloatMenuOption($"{baseLabel} (Resolve in Background)", () => OrderTherapy(therapist, patient, TherapyMode.Background), MenuOptionPriority.Default));
                }
            }
        }

        /// <summary>Run the acceptance check, then order the therapy job with the chosen mode stashed in
        /// <see cref="Job.count"/> (read once by <see cref="JobDriver_TherapySession"/> at session start).</summary>
        private static void OrderTherapy(Pawn therapist, Pawn patient, TherapyMode mode)
        {
            if (therapist == null || patient == null) return;

            // Slaves/Prisoners always accept. A colonist/visitor who hates the therapist refuses.
            if (!patient.IsPrisoner && !patient.IsSlaveOfColony)
            {
                int opinion = patient.relations?.OpinionOf(therapist) ?? 0;
                if (opinion < -20)
                {
                    MoteMaker.ThrowText(patient.DrawPos, patient.Map, "Hates you", Color.red);
                    if (patient.Faction != therapist.Faction && patient.Faction != null)
                        Messages.Message($"{patient.LabelShort} was insulted by {therapist.LabelShort}'s approach.", MessageTypeDefOf.NegativeEvent, false);
                    return;
                }
            }

            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Synapse_InitiateTherapy"), patient);
            job.count = (int)mode; // #17: carries the mode to the JobDriver
            therapist.jobs.TryTakeOrderedJob(job);
        }
    }
}
