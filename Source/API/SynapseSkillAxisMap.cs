using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Comps;
using RimSynapse.Models;

namespace RimSynapse.Psychology.API
{
    /// <summary>One measured push toward a trait candidate for a single day.</summary>
    public class SkillSignal
    {
        public string candidateId;   // "NaturalMood#-1", "Bloodlust#+", "Synapse_Aversion_Plants#+"
        public float dailyPressure;  // 0..1 contribution before resistance
        public string direction;     // "add" | "remove" (legacy field on TraitPressure)
        public string reason;        // short human-readable cause, for debug/night-report

        public SkillSignal(string candidateId, float dailyPressure, string direction, string reason)
        {
            this.candidateId = candidateId;
            this.dailyPressure = dailyPressure;
            this.direction = direction;
            this.reason = reason;
        }
    }

    /// <summary>
    /// The skill → trait-axis mapping: the heart of the skill-driven trait engine. Reads a pawn's skills,
    /// passions, rust (via Core's snapshot), and Core's raw activity facts, and produces the day's measured
    /// pushes toward trait candidates. Psychology owns this interpretation; Core owns the raw facts and the
    /// pressure store. Pure and game-light — the only side effect is Core refreshing its skill snapshot in
    /// <see cref="SynapseCorePawnComp.UpdateAndGetRustedSkills"/>.
    /// </summary>
    public static class SynapseSkillAxisMap
    {
        // ── Tuning knobs (Phase 3 will expose these as settings) ──────────────────────────
        public static float PracticeXp => RimSynapse.Psychology.RimSynapsePsychologyMod.Settings?.practiceXpThreshold ?? 800f; // xpSinceMidnight at/above which a passion counts "fulfilled"
        public const float IdleXp = 50f;         // xpSinceMidnight below which a skill counts "untouched today"
        public const int ExpertLevel = 10;       // level at/above which disuse causes rust
        public const float RustCapPerDay = 0.40f;

        // Vanilla spectrum / single axes we push.
        public const string NaturalMood = "NaturalMood";
        public const string Industriousness = "Industriousness";
        public const string Nerves = "Nerves";
        public const string Bloodlust = "Bloodlust";
        public const string Kind = "Kind";
        public const string Abrasive = "Abrasive";
        public const string Greedy = "Greedy";
        public const string Jealous = "Jealous";
        public const string Ascetic = "Ascetic";

        /// <summary>Maps a skill to its work type, the graduated DISTASTE spectrum a pawn moves along as they
        /// come to resent that work (reluctant -> averse, a skill penalty), and the rare terminal INCAPABLE
        /// trait that actually disables the work. Data-driven so modders can extend it.</summary>
        public class WorkDomain
        {
            public string skillDef;
            public string workTypeDef;
            public string aversionTraitDef;   // graduated distaste spectrum (degrees 1..2)
            public string incapableTraitDef;  // rare terminal: disables the work type
            public string devotionTraitDef;   // graduated devotion spectrum (degrees 1..2) — the positive mirror
            public WorkDomain(string skill, string workType, string aversion, string incapable, string devotion)
            { skillDef = skill; workTypeDef = workType; aversionTraitDef = aversion; incapableTraitDef = incapable; devotionTraitDef = devotion; }
        }

        public static readonly List<WorkDomain> WorkDomains = new List<WorkDomain>
        {
            new WorkDomain("Intellectual", "Research",     "Synapse_Aversion_Intellectual", "Synapse_Incapable_Intellectual", "Synapse_Devotion_Intellectual"),
            new WorkDomain("Plants",       "Growing",      "Synapse_Aversion_Plants",       "Synapse_Incapable_Plants",       "Synapse_Devotion_Plants"),
            new WorkDomain("Crafting",     "Crafting",     "Synapse_Aversion_Crafting",     "Synapse_Incapable_Crafting",     "Synapse_Devotion_Crafting"),
            new WorkDomain("Shooting",     "Hunting",      "Synapse_Aversion_Violent",      "Synapse_Incapable_Violent",      "Synapse_Devotion_Violent"),
            new WorkDomain("Melee",        "Hunting",      "Synapse_Aversion_Violent",      "Synapse_Incapable_Violent",      "Synapse_Devotion_Violent"),
            new WorkDomain("Social",       "Warden",       "Synapse_Aversion_Social",       "Synapse_Incapable_Social",       "Synapse_Devotion_Social"),
            new WorkDomain("Mining",       "Mining",       "Synapse_Aversion_Mining",       "Synapse_Incapable_Mining",       "Synapse_Devotion_Mining"),
            new WorkDomain("Cooking",      "Cooking",      "Synapse_Aversion_Cooking",      "Synapse_Incapable_Cooking",      "Synapse_Devotion_Cooking"),
            new WorkDomain("Artistic",     "Art",          "Synapse_Aversion_Artistic",     "Synapse_Incapable_Artistic",     "Synapse_Devotion_Artistic"),
            new WorkDomain("Animals",      "Handling",     "Synapse_Aversion_Animals",      "Synapse_Incapable_Animals",      "Synapse_Devotion_Animals"),
            new WorkDomain("Construction", "Construction", "Synapse_Aversion_Construction", "Synapse_Incapable_Construction", "Synapse_Devotion_Construction"),
        };

        public static WorkDomain DomainForSkill(string skillDefName)
            => WorkDomains.FirstOrDefault(d => d.skillDef == skillDefName);

        /// <summary>The work domain whose terminal incapable trait is <paramref name="incapableDefName"/>.</summary>
        public static WorkDomain DomainByIncapable(string incapableDefName)
            => WorkDomains.FirstOrDefault(d => d.incapableTraitDef == incapableDefName);

        /// <summary>A stable per-domain recurrence key for an aversion axis (distaste or incapable defName).</summary>
        public static string DomainKeyForAxis(string axisId)
        {
            var d = WorkDomains.FirstOrDefault(x => x.aversionTraitDef == axisId || x.incapableTraitDef == axisId);
            return d?.aversionTraitDef ?? axisId;
        }

        /// <summary>The strong passion the pawn most wants to exercise but isn't — the demand a strike leverages for.</summary>
        public static SkillRecord MostStarvedStrongPassion(Pawn pawn)
        {
            if (pawn?.skills?.skills == null) return null;
            return pawn.skills.skills
                .Where(r => r?.def != null && IsStrongPassion(r.passion) && r.xpSinceMidnight < IdleXp)
                .OrderByDescending(r => (int)r.passion).ThenByDescending(r => r.Level)
                .FirstOrDefault();
        }

        /// <summary>1.0 for a Major passion, higher for modded passions above Major (e.g. Burning ≈ 1.5).</summary>
        public static float PassionScale(Passion passion)
        {
            int over = (int)passion - (int)Passion.Major;
            return over <= 0 ? 1.0f : 1.0f + 0.5f * over;
        }

        public static bool IsStrongPassion(Passion passion) => (int)passion >= (int)Passion.Major;

        /// <summary>
        /// Produce the day's measured signals for a pawn. Called once per day at the rest edge. Every signal
        /// is multidimensional: an <b>exposure</b> (the behaviour/condition) times a <b>reinforcement</b>
        /// (whether the pawn's mood rewarded it) — a trait only forms when the pawn both does the thing AND
        /// feels good about it. <paramref name="reinforcement"/> is today's mood vs the pawn's baseline in
        /// [-1,+1]; <paramref name="stress"/> is how close to a mental break they are, in [0,1]. Mutates
        /// Core's skill snapshot (for rust) but nothing else.
        /// </summary>
        public static List<SkillSignal> SampleSignals(Pawn pawn, SynapseCorePawnComp core, float reinforcement, float stress)
        {
            var signals = new List<SkillSignal>();
            if (pawn?.skills?.skills == null || core == null) return signals;

            float posR = reinforcement > 0f ? reinforcement : 0f;   // felt better than usual today
            float negR = reinforcement < 0f ? -reinforcement : 0f;  // felt worse than usual today
            float S = stress < 0f ? 0f : (stress > 1f ? 1f : stress);

            // ── P1/P2: fulfilled passion × lifted mood (Optimist); starved passion × strain (Pessimist) ──
            var strongPassions = new List<SkillRecord>();
            foreach (var rec in pawn.skills.skills)
            {
                if (rec?.def == null || !IsStrongPassion(rec.passion)) continue;
                strongPassions.Add(rec);
                float scale = PassionScale(rec.passion);
                if (rec.xpSinceMidnight >= PracticeXp && posR > 0f)
                {
                    AddSpectrum(pawn, signals, NaturalMood, positive: true, 0.30f * scale * posR,
                        $"fulfilled passion {rec.def.label} + lifted mood");
                    // The min-max loop: doing what they love well AND feeling it grows a real gift.
                    EmitDevotion(pawn, signals, DomainForSkill(rec.def.defName), 0.25f * scale * posR,
                        $"pouring themselves into {rec.def.label}");
                }
                else if (rec.xpSinceMidnight < IdleXp && S > 0f)
                    AddSpectrum(pawn, signals, NaturalMood, positive: false, 0.35f * scale * S,
                        $"starved passion {rec.def.label} under strain");
            }

            // ── R1: rust bothers an expert only when it feeds real strain ─────────────
            var rusted = core.UpdateAndGetRustedSkills(pawn, ExpertLevel, IdleXp);
            if (rusted.Count > 0 && S > 0f)
            {
                float rustPressure = System.Math.Min(RustCapPerDay, 0.20f * rusted.Count) * S;
                AddSpectrum(pawn, signals, NaturalMood, positive: false, rustPressure,
                    "mastery rusting while stressed: " + string.Join(", ", rusted.Select(s => s.label)));
            }

            // ── D1/D2: diligence vs idleness × how the day felt; grinding-but-miserable frays Nerves ──
            core.GetActivityMetrics(out float idleFraction, out float livingViolenceFraction);
            float totalXpToday = pawn.skills.skills.Sum(r => r?.xpSinceMidnight ?? 0f);
            bool workDay = idleFraction <= 0.35f && totalXpToday >= 300f;
            bool idleDay = idleFraction >= 0.60f;
            if (workDay && posR > 0f)
                AddSpectrum(pawn, signals, Industriousness, positive: true, 0.25f * posR, "a productive day that felt good");
            else if (idleDay && posR > 0f)
                AddSpectrum(pawn, signals, Industriousness, positive: false, 0.25f * posR, "an easy day they enjoyed");
            else if (workDay && negR > 0f)
                AddSpectrum(pawn, signals, Nerves, positive: false, 0.15f * negR, "grinding work with no reward");

            // ── V1: violence vs the living — Bloodlust needs the mood spike; trauma otherwise ─────
            if (livingViolenceFraction >= 0.10f)
            {
                if (posR > 0f)
                    AddSingle(pawn, signals, Bloodlust, add: true, 0.30f * posR, "killed the living and felt better for it");
                else if (negR > 0f)
                    AddSpectrum(pawn, signals, NaturalMood, positive: false, 0.20f * negR, "shaken by the violence they did");
            }

            // ── S1/S2: social tone × the pawn's mood response (Kind / Abrasive) ──────
            SynapseCorePawnComp.GetSocialToneToday(pawn, out int socialPos, out int socialNeg);
            if (socialPos >= 2 && posR > 0f)
                AddSingle(pawn, signals, Kind, add: true, 0.20f * posR, "warm and sociable, and glad of it");
            if (socialNeg >= 2 && posR > 0f)
                AddSingle(pawn, signals, Abrasive, add: true, 0.25f * posR, "harsh with others, and untroubled by it");

            // ── F1: fixation (only under genuine strain) → two opposing poles ─────────
            AddFixationSignals(pawn, strongPassions, signals, S, negR);

            // ── Wealth: Greedy / Jealous / Ascetic, each gated by the mood response ───
            AddWealthSignals(pawn, signals, posR, negR);

            return signals;
        }

        /// <summary>
        /// Fixation resolves into opposing coping outcomes, but only when the pawn is actually suffering for
        /// it: Pole A (burnout — aversion to the fixated work) needs mood falling despite doing it (negative
        /// reinforcement); Pole B (rejecting the neglected work) needs real stress. A content, balanced pawn
        /// builds neither.
        /// </summary>
        private static void AddFixationSignals(Pawn pawn, List<SkillRecord> strongPassions,
            List<SkillSignal> signals, float stress, float negR)
        {
            if (strongPassions.Count < 2) return;

            SkillRecord dominant = strongPassions
                .Where(r => r.xpSinceMidnight >= PracticeXp)
                .OrderByDescending(r => r.xpSinceMidnight)
                .FirstOrDefault();
            if (dominant == null) return;

            var starved = strongPassions.Where(r => r != dominant && r.xpSinceMidnight < IdleXp).ToList();
            if (starved.Count == 0) return;

            // Pole A — burnout on the fixation: did it a lot but mood fell.
            if (negR > 0f)
                EmitAversion(pawn, signals, DomainForSkill(dominant.def.defName), 0.30f * negR,
                    $"burning out on {dominant.def.label}");

            // Pole B — rejecting a neglected passion: only when genuinely stressed.
            if (stress > 0f)
            {
                var starvedTop = starved.OrderByDescending(r => (int)r.passion).First();
                EmitAversion(pawn, signals, DomainForSkill(starvedTop.def.defName), 0.30f * stress,
                    $"resenting neglected {starvedTop.def.label} while fixated on {dominant.def.label}");
            }
        }

        /// <summary>
        /// Emit an aversion push for a work domain along the GRADUATED distaste spectrum (reluctant -> averse,
        /// a reversible skill-speed penalty). Only once the pawn is already fully averse — the max distaste
        /// degree — does continued pressure push toward the rare terminal INCAPABLE trait, at a reduced weight
        /// so it takes far longer to reach. A binary "suddenly can't work" is thus the exception, not the rule.
        /// </summary>
        /// <summary>Move the pawn up the graduated DEVOTION spectrum for a work domain (keen -> devoted).</summary>
        private static void EmitDevotion(Pawn pawn, List<SkillSignal> signals, WorkDomain domain, float weight, string reason)
        {
            if (domain?.devotionTraitDef == null || weight <= 0f) return;
            if (DefDatabase<TraitDef>.GetNamedSilentFail(domain.devotionTraitDef) == null) return;
            AddSpectrum(pawn, signals, domain.devotionTraitDef, positive: true, weight, reason);
        }

        private static void EmitAversion(Pawn pawn, List<SkillSignal> signals, WorkDomain domain, float weight, string reason)
        {
            if (domain?.aversionTraitDef == null || weight <= 0f) return;
            var distasteDef = DefDatabase<TraitDef>.GetNamedSilentFail(domain.aversionTraitDef);
            if (distasteDef == null) return;

            int maxDeg = (distasteDef.degreeDatas != null && distasteDef.degreeDatas.Count > 0)
                ? distasteDef.degreeDatas.Max(d => d.degree) : 0;
            int cur = pawn.story?.traits?.DegreeOfTrait(distasteDef) ?? 0;

            if (cur < maxDeg)
                AddSpectrum(pawn, signals, domain.aversionTraitDef, positive: true, weight, reason);
            else if (domain.incapableTraitDef != null)
                AddSingle(pawn, signals, domain.incapableTraitDef, add: true, weight * 0.35f,
                    reason + " — reaching a breaking point");
        }

        /// <summary>
        /// Individual wealth grounds the acquisitive traits, each still multidimensional (× the mood response):
        /// Greedy = richer than peers AND pleased by it; Jealous = poorer than peers AND resentful of it;
        /// Ascetic = owns little yet content. Wealth is a per-pawn share of colony wealth (Core computes it).
        /// </summary>
        private static void AddWealthSignals(Pawn pawn, List<SkillSignal> signals, float posR, float negR)
        {
            if (pawn?.Map == null) return;
            float avg = SynapseCorePawnComp.ColonyAverageIndividualWealth(pawn.Map);
            if (avg <= 0f) return;
            float mine = SynapseCorePawnComp.ComputeIndividualWealth(pawn);
            float ratio = mine / avg;

            if (ratio >= 1.5f && posR > 0f)
                AddSingle(pawn, signals, Greedy, add: true, 0.15f * posR, "wealthier than the others and pleased by it");

            float deficit = (avg - mine) / avg;
            if (deficit >= 0.30f && negR > 0f)
                AddSingle(pawn, signals, Jealous, add: true, 0.20f * System.Math.Min(1f, deficit) * negR,
                    "resentful of wealthier colonists");

            if (ratio <= 0.6f && posR > 0f)
                AddSingle(pawn, signals, Ascetic, add: true, 0.15f * posR, "content despite owning little");
        }

        /// <summary>Emit a spectrum push toward the reachable adjacent degree in the given direction.</summary>
        private static void AddSpectrum(Pawn pawn, List<SkillSignal> signals, string axisId, bool positive,
            float weight, string reason)
        {
            if (weight <= 0f) return;
            var def = DefDatabase<TraitDef>.GetNamedSilentFail(axisId);
            if (def == null) return;
            var axis = TraitAxis.Build(pawn, def);
            int? target = positive ? axis.plusDegree : axis.minusDegree;
            if (!target.HasValue) return; // already at the edge of the spectrum
            signals.Add(new SkillSignal(TraitAxis.SpectrumCandidate(axisId, target.Value), weight, "add", reason));
        }

        /// <summary>Emit an add/remove push for a single (or aversion) trait.</summary>
        private static void AddSingle(Pawn pawn, List<SkillSignal> signals, string axisId, bool add,
            float weight, string reason)
        {
            if (weight <= 0f) return;
            var def = DefDatabase<TraitDef>.GetNamedSilentFail(axisId);
            if (def == null) return;
            if (add && pawn.story?.traits != null && pawn.story.traits.HasTrait(def)) return; // already has it
            signals.Add(new SkillSignal(TraitAxis.SingleCandidate(axisId, add), weight, add ? "add" : "remove", reason));
        }
    }
}
