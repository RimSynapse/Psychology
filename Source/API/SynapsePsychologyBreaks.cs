using System;
using System.Linq;
using System.Collections.Generic;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;
using RimSynapse.Models;
using RimSynapse.Utils;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Mental break management: Prediction, break profiles, and AI-driven trait directives.
    /// </summary>
    public static partial class SynapsePsychology
    {
        /// <summary>
        /// Updates the pawn's mental break category and zealotry flags.
        /// </summary>
        public static void UpdateBreakProfile(Pawn pawn, BreakCategory category, bool isZealot)
        {
            var comp = pawn.TryGetComp<SynapsePawnComp>();
            if (comp != null)
            {
                comp.breakCategory = category;
                comp.ideologyZealot = isZealot;
            }
        }

        /// <summary>
        /// Applies a trait directive from the AI, adding or removing a trait dynamically.
        /// Sends a letter to the player explaining the psychological reasoning.
        /// </summary>
        /// <summary>Sentinel target degree meaning "remove the trait from this axis" (become absent).</summary>
        public const int AbsentDegree = int.MinValue;

        /// <summary>
        /// Whole-trait add/remove (single/aversion traits). Kept for binary compat and existing callers;
        /// now a thin shim over the degree/effect-aware <see cref="ApplyTraitStep"/>, which routes removal
        /// through the proper engine path so disabled-work/skill effects actually take (and untake) hold.
        /// </summary>
        public static void ApplyTraitDirective(Pawn pawn, string traitDefName, bool add, string aiReasoning)
        {
            TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(traitDefName);
            if (traitDef == null)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Trait {traitDefName} not found.");
                return;
            }
            int authored = (traitDef.degreeDatas != null && traitDef.degreeDatas.Count > 0) ? traitDef.degreeDatas[0].degree : 0;
            ApplyTraitStep(pawn, traitDef, add ? authored : AbsentDegree, aiReasoning);
        }

        /// <summary>
        /// Move a pawn along a trait axis by applying a real trait whose real effects (stat offsets, skill
        /// gains, work-disable) then follow. Any existing trait on <paramref name="axis"/> is removed via
        /// <see cref="TraitSet.RemoveTrait"/> — NOT a raw list mutation — and if <paramref name="targetDegree"/>
        /// names an authored degree, that degree is gained. Either way the pawn's cached disabled-work / skill
        /// sets are invalidated so an added (or removed) incapability trait takes effect immediately. Pass
        /// <see cref="AbsentDegree"/> (or any non-authored degree) to end at "no trait on this axis".
        /// </summary>
        public static void ApplyTraitStep(Pawn pawn, TraitDef axis, int targetDegree, string aiReasoning)
        {
            if (pawn?.story?.traits == null || axis == null) return;
            var traits = pawn.story.traits;

            bool changed = false;
            string removedLabel = null;

            var existing = traits.GetTrait(axis);
            if (existing != null)
            {
                removedLabel = existing.CurrentData?.GetLabelFor(pawn) ?? axis.label;
                traits.RemoveTrait(existing, true); // unsuppressConflicts: restore anything this trait masked
                changed = true;
            }

            bool wantGain = targetDegree != AbsentDegree
                && axis.degreeDatas != null && axis.degreeDatas.Any(d => d.degree == targetDegree);
            string gainedLabel = null;
            if (wantGain && !traits.HasTrait(axis, targetDegree))
            {
                traits.GainTrait(new Trait(axis, targetDegree));
                gainedLabel = axis.DataAtDegree(targetDegree)?.GetLabelFor(pawn) ?? axis.label;
                changed = true;
            }

            if (!changed) return;

            // Recompute the cached disabled-work/skill sets so a work-disabling aversion trait (or its
            // removal) is reflected in the work tab and job assignment right away.
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.skills?.Notify_SkillDisablesChanged();

            var comp = pawn.TryGetComp<SynapsePawnComp>();
            if (comp != null)
            {
                comp.dynamicTraits.RemoveAll(x => x.traitDef == axis);
                if (wantGain)
                    comp.dynamicTraits.Add(new RimSynapse.Psychology.Models.DynamicTraitRecord(axis, Find.TickManager.TicksGame, aiReasoning));
            }

            string title = $"Personality Shift: {pawn.Name.ToStringShort}";
            if (Prefs.DevMode) title = "[RimSynapse Psychology] " + title;
            string actionStr = wantGain
                ? $"gained the trait '{gainedLabel}'"
                : $"lost the trait '{removedLabel}'";
            string letterText = $"{pawn.Name.ToStringShort} has {actionStr}.\n\nReasoning:\n{aiReasoning}";
            Find.LetterStack.ReceiveLetter(title, letterText, LetterDefOf.NeutralEvent, pawn);
        }

        // Confirmed extreme/major MentalStateDef defNames (NOT MentalBreakDefs — e.g. "Catatonic" is a
        // MentalBreakDef and would fail the MentalStateDef lookup, so it is deliberately excluded).
        // Exposed so a test can assert every candidate resolves — an invalid name silently no-ops the break.
        public static readonly string[] BreakCandidates =
            { "Berserk", "Slaughterer", "FireStartingSpree", "InsultingSpree", "Binging_Food", "Wander_Sad", "GiveUpExit" };

        /// <summary>
        /// Ask the LLM which mental break best fits this pawn's psychology as they cross the extreme
        /// threshold, then hand a validated MentalStateDef to <see cref="PredictMentalBreak"/> (#50 — this
        /// was the commented-out request that left the AI-driven break path inert). On any failure the
        /// caller's "pending" flag is left in place, so there is no re-request spam; it clears when the
        /// pawn's mood recovers.
        /// </summary>
        public static void RequestBreakWarning(Pawn pawn)
        {
            if (pawn == null) return;
            var coreComp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            float threshold = RimSynapsePsychologyMod.Settings != null ? RimSynapsePsychologyMod.Settings.sensitivityThreshold : 0.5f;
            string burdens = coreComp != null ? coreComp.GetTopMemoryBurdens(5, threshold) : "None";
            string traits = pawn.story?.traits?.allTraits != null
                ? string.Join(", ", pawn.story.traits.allTraits.Select(t => t.Label)) : "None";
            float mood = pawn.needs?.mood?.CurLevelPercentage ?? 0f;

            string systemPrompt = $@"You are modelling a RimWorld colonist who has crossed their EXTREME mental-break threshold and is about to snap.
Choose the single mental break their psychology, traits and burdens best justify, from this exact list of RimWorld MentalStateDef defNames:
{string.Join(", ", BreakCandidates)}.
Respond ONLY as valid JSON, no markdown:
{{ ""BreakDefName"": ""<one defName from the list>"", ""Warning"": ""<1-2 sentence in-character warning of what is coming>"" }}";

            string userMessage = $"Name: {pawn.Name.ToStringShort}\nMood: {mood:P0} (below the extreme break threshold)\nTraits: {traits}\nPsychological burdens: {burdens}";

            var options = new ChatOptions { priority = 2, requestName = "Break Prediction", targetName = pawn.Name.ToStringShort };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result =>
                {
                    if (!result.success) return; // leave 'pending' — no spam; clears on mood recovery
                    try
                    {
                        string json = JsonHelper.ExtractJson(result.content);
                        if (json == null) return;
                        var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                        if (parsed == null) return;

                        string breakName = parsed.TryGetValue("BreakDefName", out var b) ? b : null;
                        string warning = parsed.TryGetValue("Warning", out var w) && !string.IsNullOrEmpty(w)
                            ? w : $"{pawn.Name.ToStringShort} is on the verge of snapping.";

                        var breakDef = DefDatabase<MentalStateDef>.GetNamedSilentFail(breakName);
                        if (breakDef == null)
                        {
                            RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Break prediction returned invalid MentalStateDef '{breakName}' for {pawn.Name?.ToStringShort}; leaving pending.");
                            return;
                        }
                        SynapseGameComponent.Enqueue(() => PredictMentalBreak(pawn, breakDef.defName, warning));
                    }
                    catch (Exception ex)
                    {
                        RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to parse break prediction: {ex.Message}");
                    }
                },
                options);
        }

        /// <summary>
        /// Called by the LLM Bridge when it has determined what specific mental break
        /// a pawn will suffer if they snap. Displays a warning threat to the player.
        /// </summary>
        public static void PredictMentalBreak(Pawn pawn, string breakDefName, string warningText)
        {
            var comp = pawn.TryGetComp<SynapsePawnComp>();
            if (comp == null) return;

            MentalStateDef breakDef = DefDatabase<MentalStateDef>.GetNamedSilentFail(breakDefName);
            if (breakDef == null)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Predicted break {breakDefName} not found.");
                return;
            }

            comp.predictedBreakState = breakDef;
            comp.currentBreakWarning = warningText;

            string title = $"Break Risk: {pawn.Name.ToStringShort}";
            Find.LetterStack.ReceiveLetter(title, warningText, LetterDefOf.ThreatSmall, pawn);
        }
    }
}


