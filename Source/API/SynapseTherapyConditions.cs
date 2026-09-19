using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Psychological conditions as treatable illnesses (#17 redesign). Each condition is a per-pawn Hediff
    /// seeded from the pawn's psyche (a curable trait), which — unlike a normal illness — never resolves on
    /// its own: it persists, drags the pawn into the linked mental state more and more often the worse it is,
    /// and only lifts when therapy drives its severity to zero. Therapy's effect is a WEIGHTED quality score
    /// (therapist skill, bedroom privacy, trust, patient receptiveness), not a coin flip.
    /// </summary>
    public static class SynapseTherapyConditions
    {
        /// <summary>A treatable condition: its hediff, the trait it is seeded from, and the mental state it
        /// inflicts while untreated. Only genuinely SYMPTOMATIC conditions belong here — personality traits
        /// (greedy, abrasive, jealous, work aversions) are NOT illnesses and are left to the trait engine.</summary>
        public sealed class Condition
        {
            public string hediffDefName;
            public string traitDefName;
            public int[] qualifyingDegrees;   // empty = any degree
            public string chaosStateDefName;  // mental state triggered while untreated

            /// <summary>Curable conditions clear at severity 0 (hediff + trait lift). A CHRONIC condition
            /// (curable=false, like pyromania / a genetic dependency) is never cured — therapy only holds it at
            /// or above <see cref="managedFloor"/>, and if <see cref="driftsUntreated"/> the compulsion creeps
            /// back on its own until treated again.</summary>
            public bool curable = true;
            public float managedFloor = 0f;
            public bool driftsUntreated = false;
        }

        // Symptomatic mental illnesses only. PTSD → trauma; negative NaturalMood → depression; weak Nerves →
        // anxiety; Synapse_Bipolar → mood instability; Neurotic → neurosis (all curable). Pyromania is CHRONIC —
        // managed, never cured, and the urge drifts back untreated.
        public static readonly Condition[] Conditions =
        {
            new Condition { hediffDefName = "Synapse_Hediff_Trauma",     traitDefName = "Synapse_PTSD",    qualifyingDegrees = new int[0],       chaosStateDefName = "Synapse_TraumaTrigger" },
            new Condition { hediffDefName = "Synapse_Hediff_Depression", traitDefName = "NaturalMood",     qualifyingDegrees = new[] { -1, -2 }, chaosStateDefName = "Wander_Sad" },
            new Condition { hediffDefName = "Synapse_Hediff_Anxiety",    traitDefName = "Nerves",          qualifyingDegrees = new[] { -1, -2 }, chaosStateDefName = "PanicFlee" },
            new Condition { hediffDefName = "Synapse_Hediff_Bipolar",    traitDefName = "Synapse_Bipolar", qualifyingDegrees = new int[0],       chaosStateDefName = "Synapse_EuphoricReckless" },
            new Condition { hediffDefName = "Synapse_Hediff_Neurotic",   traitDefName = "Neurotic",        qualifyingDegrees = new int[0],       chaosStateDefName = "Wander_Sad" },
            new Condition { hediffDefName = "Synapse_Hediff_Pyromania",  traitDefName = "Pyromaniac",      qualifyingDegrees = new int[0],       chaosStateDefName = "FireStartingSpree",
                            curable = false, managedFloor = 0.1f, driftsUntreated = true },
            // Event-driven, no trait: seeded by SeedGrief on a death, fades on its own via the hediff comp.
            new Condition { hediffDefName = "Synapse_Hediff_Grief",      traitDefName = "",                qualifyingDegrees = new int[0],       chaosStateDefName = "Wander_Sad" },
        };

        private const float TreatPower = 0.25f;            // best-case severity removed by one ideal session
        private const float WorsenPerLowMoodTick = 0.02f;  // symptomatic illnesses fester while the pawn is miserable
        private const float ChronicDriftPerTick = 0.006f;  // a chronic compulsion creeps back untreated (~0.14/day)

        public static HediffDef HediffOf(Condition c) => DefDatabase<HediffDef>.GetNamedSilentFail(c.hediffDefName);

        /// <summary>The trait that seeds/qualifies a condition, present on the pawn at a qualifying degree, else null.</summary>
        public static Trait QualifyingTrait(Pawn pawn, Condition c)
        {
            if (pawn?.story?.traits == null) return null;
            var def = DefDatabase<TraitDef>.GetNamedSilentFail(c.traitDefName);
            if (def == null) return null;
            var trait = pawn.story.traits.GetTrait(def);
            if (trait == null) return null;
            if (c.qualifyingDegrees.Length > 0 && !c.qualifyingDegrees.Contains(trait.Degree)) return null;
            return trait;
        }

        /// <summary>Severity a freshly-seeded condition starts at, from how deep the seeding trait runs. Keys on
        /// the trait's MAGNITUDE so it works for negative degrees (NaturalMood/Nerves) and positive ones
        /// (Neurotic) alike.</summary>
        public static float SeedSeverity(Trait trait)
        {
            if (trait == null) return 0.7f;
            int m = Mathf.Abs(trait.Degree);
            if (m >= 2) return 0.85f;
            if (m == 1) return 0.55f;
            return 0.9f; // single-degree conditions (PTSD, Bipolar, Pyromania) run deep
        }

        /// <summary>Give the pawn any condition hediff its psyche warrants but that it doesn't yet carry. The
        /// onset side of "both onset and progression": a pawn with the trait IS afflicted until therapy cures it.</summary>
        public static void EnsureSeeded(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead) return;
            foreach (var c in Conditions)
            {
                var trait = QualifyingTrait(pawn, c);
                if (trait == null) continue;
                var hediffDef = HediffOf(c);
                if (hediffDef == null || pawn.health.hediffSet.HasHediff(hediffDef)) continue;
                var hediff = HediffMaker.MakeHediff(hediffDef, pawn);
                hediff.Severity = SeedSeverity(trait);
                pawn.health.AddHediff(hediff);
            }
        }

        /// <summary>The pawn's worst untreated condition — the natural target for a session — or null.</summary>
        public static Hediff MostSevere(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return null;
            Hediff worst = null;
            foreach (var c in Conditions)
            {
                var def = HediffOf(c);
                if (def == null) continue;
                var h = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (h != null && (worst == null || h.Severity > worst.Severity)) worst = h;
            }
            return worst;
        }

        public static Condition ConditionForHediff(Hediff h)
            => h == null ? null : Conditions.FirstOrDefault(c => c.hediffDefName == h.def.defName);

        /// <summary>
        /// The weighted quality of a session, 0..1 — REPLACES the old success/fail coin flip. Blends therapist
        /// Social skill, the privacy/impressiveness of the (bedroom) room the session is held in, the pair's
        /// trust, and how reachable the patient is right now (very low mood is harder to reach).
        /// </summary>
        public static float Quality(Pawn therapist, Pawn patient, Room room)
        {
            if (therapist == null || patient == null) return 0f;

            float q = 0.15f; // a caring but unskilled attempt in a bare room still does a little

            int social = therapist.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            q += social * 0.03f;                                   // up to +0.60 at 20 Social

            q += PrivacyBonus(patient, room);                      // up to +0.25 in their own, impressive room

            float trust = TrustToward(patient, therapist);         // -100..100
            q += (trust / 100f) * 0.15f;                           // -0.15..+0.15

            float mood = patient.needs?.mood?.CurLevelPercentage ?? 0.5f;
            q += (mood - 0.5f) * 0.2f;                             // -0.10..+0.10

            return Mathf.Clamp01(q);
        }

        /// <summary>Privacy/comfort contribution: biggest when the session is in the patient's OWN bedroom, plus
        /// a slice of the room's impressiveness, and only if it's just the two of them.</summary>
        public static float PrivacyBonus(Pawn patient, Room room)
        {
            if (room == null || room.PsychologicallyOutdoors) return 0f;

            float bonus = 0f;
            bool ownRoom = patient?.ownership?.OwnedRoom == room;
            if (ownRoom) bonus += 0.15f;                           // their own space — the core of the redesign

            float impressive = room.GetStat(RoomStatDefOf.Impressiveness); // ~0..100+
            bonus += Mathf.Clamp(impressive / 100f, 0f, 1f) * 0.10f;

            int humanlikes = 0;
            foreach (Thing t in room.ContainedAndAdjacentThings)
                if (t is Pawn p && p.RaceProps.Humanlike && p.Awake()) humanlikes++;
            if (humanlikes > 2) bonus *= 0.5f;                     // not private — half credit

            return Mathf.Clamp(bonus, 0f, 0.25f);
        }

        private static float TrustToward(Pawn patient, Pawn therapist)
        {
            var comp = patient?.GetComp<SynapsePawnComp>();
            if (comp?.socialNetwork == null) return 0f;
            string id = therapist.GetUniqueLoadID();
            return comp.socialNetwork.TryGetValue(id, out var rec) ? rec.trust : 0f;
        }

        /// <summary>
        /// Apply one session of treatment to <paramref name="target"/> at the given <paramref name="quality"/>.
        /// A good session drops severity proportionally; a poor one (quality &lt; 0.2) does nothing or slightly
        /// worsens it. Reaching zero CURES the condition — the hediff and its seeding trait are removed. Returns
        /// true when the session helped (severity fell), for the participants' session memory.
        /// </summary>
        public static bool Treat(Pawn therapist, Pawn patient, Hediff target, float quality)
        {
            if (patient == null || target == null) return false;
            var condition = ConditionForHediff(target);

            float delta = quality >= 0.2f
                ? -quality * TreatPower                     // helped: remove up to TreatPower severity
                : (0.2f - quality) * 0.10f;                 // botched: a small setback

            // A chronic condition can only be MANAGED down to its floor, never cleared.
            float floor = condition?.managedFloor ?? 0f;
            float before = target.Severity;
            target.Severity = Mathf.Max(floor, before + delta);
            bool helped = target.Severity < before - 0.0001f;

            // Only a curable condition resolves at zero; a chronic one stays (held at its floor).
            if ((condition == null || condition.curable) && target.Severity <= 0.001f)
                Cure(patient, target, therapist);

            return helped;
        }

        /// <summary>Fully resolved: drop the hediff, lift the seeding trait, and leave a permanent recovery mark.</summary>
        public static void Cure(Pawn patient, Hediff target, Pawn therapist)
        {
            var condition = ConditionForHediff(target);
            string label = target.LabelBase;

            if (patient.health.hediffSet.HasHediff(target.def))
                patient.health.RemoveHediff(target);

            if (condition != null)
            {
                var trait = QualifyingTrait(patient, condition);
                if (trait != null && patient.story?.traits != null)
                    patient.story.traits.allTraits.Remove(trait);
            }

            Messages.Message($"{patient.NameShortColored} has recovered from {label}"
                + (therapist != null ? $" through therapy with {therapist.NameShortColored}." : "."),
                patient, MessageTypeDefOf.PositiveEvent);

            var core = patient.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            core?.AddMemory(new RimSynapse.Models.WeightedMemory
            {
                summary = $"Recovered from {label} through sustained therapy.",
                memoryType = "TraitLost",
                tags = new List<string> { "TraitShift", "Therapy", "Recovery" },
                weight = 1.0f, baseWeight = 1.0f, decayRate = 0f, isLongTerm = true,
                absTick = Find.TickManager?.TicksAbs ?? 0L,
                gameTick = Find.TickManager?.TicksGame ?? 0
            });
        }

        /// <summary>
        /// The "falls into chaos more frequently until treated" side. Over one <paramref name="interval"/> of
        /// ticks, each untreated condition rolls to throw the pawn into its linked mental state, with a mean time
        /// between events that shortens sharply as severity rises (a severe condition breaks days sooner than a
        /// mild one). Returns the state it started, if any (for the debug action).
        /// </summary>
        public static MentalStateDef TickChaos(Pawn pawn, int interval)
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead || pawn.InMentalState || !pawn.Spawned) return null;
            if (pawn.needs?.mood == null) return null;

            foreach (var c in Conditions)
            {
                var def = HediffOf(c);
                var h = def != null ? pawn.health.hediffSet.GetFirstHediffOfDef(def) : null;
                if (h == null || h.Severity <= 0f) continue;

                // severity 1.0 → ~0.5 day MTB (frequent chaos); severity 0.1 → ~5 day MTB (rare).
                float mtbDays = Mathf.Lerp(5f, 0.5f, h.Severity);
                if (!Rand.MTBEventOccurs(mtbDays, GenDate.TicksPerDay, interval)) continue;

                var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail(c.chaosStateDefName);
                if (stateDef != null && TryStartChaos(pawn, stateDef))
                    return stateDef;
            }
            return null;
        }

        /// <summary>Force the linked mental state (the whole point is that untreated conditions break the pawn).</summary>
        public static bool TryStartChaos(Pawn pawn, MentalStateDef stateDef)
        {
            if (pawn?.mindState?.mentalStateHandler == null || stateDef == null) return false;
            return pawn.mindState.mentalStateHandler.TryStartMentalState(stateDef, "untreated psychological condition", forced: true);
        }

        /// <summary>Progression, called each rare tick. Two kinds: a CHRONIC compulsion (pyromania) creeps back up
        /// on its own until treated again — this is the "without treatment they go burn stuff" pressure. A
        /// symptomatic illness only festers while the pawn is miserable (very low mood), so a cared-for colony's
        /// conditions at least hold steady. Bounded to [floor, 1].</summary>
        public static void TickProgression(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead) return;
            bool miserable = (pawn.needs?.mood?.CurLevelPercentage ?? 1f) < 0.15f;

            foreach (var c in Conditions)
            {
                if (string.IsNullOrEmpty(c.traitDefName)) continue; // event-driven (grief) — its comp handles decline
                var def = HediffOf(c);
                var h = def != null ? pawn.health.hediffSet.GetFirstHediffOfDef(def) : null;
                if (h == null) continue;

                if (c.driftsUntreated)
                    h.Severity = Mathf.Min(1f, h.Severity + ChronicDriftPerTick);
                else if (miserable)
                    h.Severity = Mathf.Min(1f, h.Severity + WorsenPerLowMoodTick);
            }
        }

        /// <summary>Event onset for grief: when someone dies, a mourner's grief is seeded from how close they were —
        /// blood/love relations run deepest, then the warmth they held on the #72 compass. Returns the severity set
        /// (0 if not close enough to grieve). Stacks toward the worse of an existing grief and this loss.</summary>
        public static float SeedGrief(Pawn mourner, Pawn deceased)
        {
            if (mourner?.health?.hediffSet == null || deceased == null || mourner == deceased || mourner.Dead) return 0f;
            var def = DefDatabase<HediffDef>.GetNamedSilentFail("Synapse_Hediff_Grief");
            if (def == null) return 0f;

            float closeness = 0f;
            if (mourner.relations != null)
            {
                foreach (var rel in mourner.relations.DirectRelations)
                {
                    if (rel.otherPawn != deceased) continue;
                    string d = rel.def.defName;
                    if (d == "Spouse" || d == "Lover" || d == "Fiance") closeness = Mathf.Max(closeness, 0.9f);
                    else if (d == "Parent" || d == "Child" || d == "Sibling") closeness = Mathf.Max(closeness, 0.75f);
                    else closeness = Mathf.Max(closeness, 0.5f);
                }
            }
            var comp = mourner.GetComp<SynapsePawnComp>();
            if (comp?.socialNetwork != null && comp.socialNetwork.TryGetValue(deceased.GetUniqueLoadID(), out var rec))
                closeness = Mathf.Max(closeness, Mathf.Clamp01(rec.warmth / 100f) * 0.7f);

            if (closeness < 0.25f) return 0f; // not close enough to truly grieve

            var existing = mourner.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null) { existing.Severity = Mathf.Max(existing.Severity, closeness); return existing.Severity; }

            var h = HediffMaker.MakeHediff(def, mourner);
            h.Severity = closeness;
            mourner.health.AddHediff(h);
            return closeness;
        }
    }
}
