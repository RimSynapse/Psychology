using System.Collections.Generic;
using Verse;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Therapy-session bookkeeping (#17). The deterministic backbone shared by every session mode
    /// (Guiding Hand / Watch / Background): when a session completes, BOTH participants record a
    /// <c>Therapy</c>-tagged memory and a per-pawn transcript. The LLM dialogue (when a windowed or
    /// background session produced one) only supplies the transcript lines; this runs whether or not an
    /// LLM was ever consulted, so an RNG-only session still leaves a record. Split out as a static seam so
    /// the JobDriver, the debug action and the in-game test all drive the identical path.
    /// </summary>
    public static class SynapseTherapy
    {
        /// <summary>Record a completed session on both participants. <paramref name="lines"/> is the session's
        /// chat log when there was one, else a single templated line is stored so the record is never empty.</summary>
        public static void RecordSession(Pawn therapist, Pawn patient, bool success, List<string> lines = null, int? tickOverride = null)
        {
            if (therapist == null || patient == null) return;
            int tick = tickOverride ?? (Find.TickManager?.TicksGame ?? 0);

            var log = (lines != null && lines.Count > 0)
                ? new List<string>(lines)
                : new List<string> { $"[System] {therapist.LabelShort} held a therapy session for {patient.LabelShort}. Outcome: {(success ? "helpful" : "awkward")}." };

            RecordFor(therapist, patient, isTherapist: true, success: success, lines: log, tick: tick);
            RecordFor(patient, therapist, isTherapist: false, success: success, lines: log, tick: tick);
        }

        private static void RecordFor(Pawn owner, Pawn other, bool isTherapist, bool success, List<string> lines, int tick)
        {
            if (owner == null || other == null) return;

            string summary = isTherapist
                ? (success ? $"Guided {other.LabelShort} through a therapy session that seemed to help."
                           : $"Tried to counsel {other.LabelShort}, but the session was awkward and went nowhere.")
                : (success ? $"Opened up to {other.LabelShort} in therapy and came away lighter."
                           : $"Sat through an awkward therapy session with {other.LabelShort}; it didn't land.");

            SynapsePsychology.AddMemory(owner, new RimSynapse.Models.WeightedMemory
            {
                summary = summary,
                memoryType = "Therapy",
                tags = new List<string> { "Therapy", isTherapist ? "Therapist" : "Patient", success ? "Helped" : "Awkward" },
                weight = success ? 0.4f : 0.25f,
                baseWeight = success ? 0.4f : 0.25f,
                decayRate = 0.2f,
                subjectPawnIds = new List<string> { RimSynapse.Comps.SynapseCorePawnComp.MemoryPawnId(other) },
                absTick = RimSynapse.Utils.SynapseDateHelper.GameTickToAbsTick(tick),
                gameTick = tick
            });

            var comp = owner.GetComp<SynapsePawnComp>();
            comp?.therapyTranscripts.Add(new RimSynapse.Psychology.Models.TherapyTranscript
            {
                otherPawnName = other.LabelShort,
                sessionTick = tick,
                lines = new List<string>(lines)
            });
        }
    }
}
