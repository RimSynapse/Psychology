using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using RimSynapse.Models;
using RimSynapse.Utils;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.Comps
{
    /// <summary>
    /// Memory-first backstory generation pipeline.
    ///
    /// Flow:
    ///   Step 1: Generate a childhood memory (100-200 words) from the vanilla childhood backstory + skill bonuses
    ///   Step 2: Generate an adulthood memory (100-200 words) from the vanilla adulthood backstory + skill bonuses
    ///   Step 3: Generate psychological traits + personality summary using both memories as context
    ///
    /// Prompt construction and LLM callbacks are in SynapsePawnComp_BackstoryPrompts.cs (partial class).
    /// This file contains the entry point, finalization, and utility methods.
    /// </summary>
    public partial class SynapsePawnComp
    {
        private bool isGeneratingBackstory = false;
        private bool isGeneratingVoice = false;   // transient in-flight guard for voice backfill (#33)

        /// <summary>
        /// Entry point — called from CompTickRare when the pawn doesn't have a backstory yet.
        /// Kicks off Step 1 (childhood memory generation).
        /// </summary>
        private void GenerateAIBackstory(Pawn pawn)
        {
            var coreComp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (coreComp == null) return;

            isGeneratingBackstory = true;

            bool hasChildhood = coreComp.memories.Any(m => m.memoryType == "BackstoryChildhood");
            bool hasAdulthood = coreComp.memories.Any(m => m.memoryType == "BackstoryAdulthood");
            bool hasPersonality = !string.IsNullOrEmpty(coreComp.personalitySummary);
            bool hasAssessment = !string.IsNullOrEmpty(coreComp.clinicalAssessment);

            if (!hasChildhood)
            {
                GenerateChildhoodMemory(pawn, coreComp);
            }
            else if (!hasAdulthood && pawn.story?.Adulthood != null)
            {
                GenerateAdulthoodMemory(pawn, coreComp);
            }
            else if (!hasPersonality)
            {
                GeneratePersonalityProfile(pawn, coreComp);
            }
            else
            {
                FinalizeBackstory(pawn, coreComp);
            }
        }

        /// <summary>
        /// Debug hook (#63): force the full profile pipeline to run for this pawn right now, bypassing
        /// the colony-relevance gate — so a prisoner, slave, guest (or even a raider, for the exclusion
        /// test) can be exercised on demand. Resets prior backstory state so it regenerates cleanly.
        /// Generation is async; inspect the result afterwards with the "Dump personality"/"Dump voice"
        /// debug actions.
        /// </summary>
        public void DebugForceProfile(Pawn pawn)
        {
            if (pawn == null) return;
            hasBackstoryMemory = false;
            isGeneratingBackstory = false;
            ticksToGenerateBackstory = 0;
            var coreComp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (coreComp != null)
            {
                // Clear the pieces the pipeline branches on so every step (childhood → adulthood →
                // personality → voice) re-runs rather than short-circuiting on stale data.
                coreComp.memories?.RemoveAll(m => m.memoryType == "BackstoryChildhood" || m.memoryType == "BackstoryAdulthood");
                coreComp.personalitySummary = null;
                coreComp.voiceGenerated = false;
            }
            GenerateAIBackstory(pawn);
        }

        // ────────────────────────────────────────────────────────
        //  Finalization
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Assembles the dynamic backstory from stored memories (no extra LLM call needed).
        /// </summary>
        private void BuildDynamicBackstory(Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            var sb = new StringBuilder();
            
            var childhoodMem = coreComp.memories.LastOrDefault(m => m.memoryType == "BackstoryChildhood");
            var adulthoodMem = coreComp.memories.LastOrDefault(m => m.memoryType == "BackstoryAdulthood");
            
            if (childhoodMem != null)
            {
                sb.AppendLine(childhoodMem.summary);
                sb.AppendLine();
            }
            if (adulthoodMem != null)
            {
                sb.AppendLine(adulthoodMem.summary);
                sb.AppendLine();
            }
            if (!string.IsNullOrEmpty(coreComp.personalitySummary))
            {
                sb.AppendLine(coreComp.personalitySummary);
            }

            coreComp.dynamicBackstory = sb.ToString().Trim();
        }

        /// <summary>
        /// Marks the backstory as complete and notifies the player.
        /// Called after all steps finish (or after partial completion if later steps fail).
        /// </summary>
        private void FinalizeBackstory(Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            isGeneratingBackstory = false;
            hasBackstoryMemory = true;

            if (string.IsNullOrEmpty(coreComp.dynamicBackstory))
            {
                BuildDynamicBackstory(pawn, coreComp);
            }

            // Only colonists announce their backstory with a letter. Prisoners, slaves and guests now
            // generate a full profile too (#63), but a "Backstory Discovered" letter for every captured
            // raider would be spam — their profile is generated silently and viewable on their Psychology
            // tab. The celebratory reveal stays a colonist moment.
            if (!pawn.IsColonist)
            {
                RimSynapse.SynapseLogger.Info("psychology",
                    $"[RimSynapse-Psychology] Profile generated for non-colonist {pawn.Name.ToStringShort} " +
                    $"({ColonyRoleLabel(pawn)}) — no letter; viewable on their Psychology tab.");
                return;
            }

            string title = "Backstory Discovered";
            string text = $"{pawn.Name.ToStringShort} has shared their backstory with you.\n\n" +
                          "Open their Psychology tab to learn more about their past and personality traits.";

            var letter = new RimSynapse.Psychology.UI.ChoiceLetter_OpenPsychology
            {
                def = LetterDefOf.NeutralEvent,
                Label = title,
                Text = text,
                lookTargets = pawn,
                targetPawn = pawn,
                ID = Find.UniqueIDsManager.GetNextLetterID()
            };
            Find.LetterStack.ReceiveLetter(letter);
        }

        // ────────────────────────────────────────────────────────
        //  Utility: Colony-standing framing for profile prompts (#63)
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// How the prompt should address this pawn, given their standing relative to the player's
        /// colony. A captive must NOT be written up as "Colonist" — their situation frames the
        /// profile (and especially the arrival first-impression). Mirrors the IsEligibleForReview
        /// population: colonists, prisoners, slaves, and staying guests (quest lodgers).
        /// </summary>
        private static string ColonyRoleLabel(Pawn pawn)
        {
            if (pawn.IsSlaveOfColony) return "Slave of the colony";
            if (pawn.IsPrisonerOfColony) return "Prisoner of the colony";
            if (pawn.IsColonist) return "Colonist";
            if (pawn.IsQuestLodger()) return "Guest of the colony";
            return "Newcomer to the colony";
        }

        /// <summary>
        /// A concrete instruction for the arrival first-impression, so a prisoner's "arrival" is being
        /// brought in bound and locked away — not a hopeful landing. The identity memories (childhood /
        /// adulthood) are the same regardless of current standing; only the arrival differs, which is
        /// why the profile is generated once at first eligibility and NOT regenerated on status change.
        /// </summary>
        private static string ArrivalSituation(Pawn pawn)
        {
            if (pawn.IsSlaveOfColony)
                return "They are held by the colony as a SLAVE. The arrival is the moment they were taken, sold, or subjugated into bondage here — not a chosen landing. Reflect their captivity and whether they are defiant, broken, or biding their time.";
            if (pawn.IsPrisonerOfColony)
                return "They are a PRISONER of the colony. The arrival is being brought in — often wounded or bound — and locked in a cell, not a hopeful landing. Reflect their captivity and how they regard their captors.";
            if (pawn.IsColonist)
                return "They joined the colony as a member. The arrival is their landing or recruitment (e.g. waking from cryosleep, walking in from the wastes) and the resolve to make a life here.";
            if (pawn.IsQuestLodger())
                return "They are a GUEST staying with the colony for a time. The arrival is their coming to stay as a visitor rather than a member — with their own reasons for being here and somewhere else to return to.";
            return "They have recently come to the colony.";
        }

        // ────────────────────────────────────────────────────────
        //  Utility: Extract skill info from vanilla BackstoryDef
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Formats the skill gains from a BackstoryDef into a human-readable string.
        /// e.g., "+4 Mining, +2 Crafting, -3 Social"
        /// </summary>
        private static string FormatSkillGains(BackstoryDef backstory)
        {
            if (backstory?.skillGains == null || backstory.skillGains.Count == 0)
                return "None";

            var parts = new List<string>();
            foreach (var sg in backstory.skillGains)
            {
                string sign = sg.amount >= 0 ? "+" : "";
                parts.Add($"{sign}{sg.amount} {sg.skill.label}");
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Formats disabled work types from a BackstoryDef.
        /// e.g., "Violent, Caring"
        /// </summary>
        private static string FormatDisabledWork(BackstoryDef backstory)
        {
            if (backstory == null || backstory.workDisables == WorkTags.None)
                return "";

            var disabled = new List<string>();
            foreach (WorkTags tag in Enum.GetValues(typeof(WorkTags)))
            {
                if (tag == WorkTags.None) continue;
                if ((backstory.workDisables & tag) != 0)
                {
                    disabled.Add(tag.ToString());
                }
            }
            return disabled.Count > 0 ? string.Join(", ", disabled) : "";
        }
    }
}
