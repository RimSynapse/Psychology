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
