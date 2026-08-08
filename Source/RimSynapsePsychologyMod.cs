using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.Settings;
using HarmonyLib;
using UnityEngine;

namespace RimSynapse.Psychology
{
    public class RimSynapsePsychologyMod : Mod
    {
        public static RimSynapsePsychologySettings Settings;
        public static SynapseModHandle ModHandle;

        public RimSynapsePsychologyMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimSynapsePsychologySettings>();
            var harmony = new Harmony("RimSynapse.Psychology");
            harmony.PatchAll();
            
            // Manual Patches for Ritual/Ceremony Outcomes (Ideology and Royalty)
            string[] outcomeWorkerTypes = new string[]
            {
                "RimWorld.RitualOutcomeEffectWorker_FromQuality",
                "RimWorld.RitualOutcomeEffectWorker_FromQualityWithReason",
                "RimWorld.RitualOutcomeEffectWorker_Speech",
                "RimWorld.RitualOutcomeEffectWorker_RoleChange",
                "RimWorld.RitualOutcomeEffectWorker_Conversion",
                "RimWorld.RitualOutcomeEffectWorker_BestowingCeremony"
            };

            foreach (var typeName in outcomeWorkerTypes)
            {
                var type = AccessTools.TypeByName(typeName);
                if (type != null)
                {
                    var original = AccessTools.Method(type, "Apply");
                    var postfix = AccessTools.Method(typeof(Patches.Patch_Funeral_Apply), "Postfix");
                    if (original != null && postfix != null)
                    {
                        harmony.Patch(original, null, new HarmonyMethod(postfix));
                        RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Successfully applied manual patch for {typeName}.Apply");
                    }
                }
            }
            
            // Register with Core
            ModHandle = SynapseCore.Register("RimSynapsePsychology", "RimSynapse Psychology");
            API.SynapsePsychologyTools.RegisterTools();
            SynapseToolRegistry.CustomBreakHandler = API.SynapsePsychologyTools.HandleCustomBreak;
            
            // Register opportunistic background tasks with scheduling metadata
            RimSynapse.SynapseClient.RegisterOpportunisticTask(ModHandle, "Psychology_OpportunisticMemory",
                (System.Func<bool>)API.SynapsePsychology.TriggerOpportunisticMemory,
                new RimSynapse.Internal.OpportunisticTaskConfig
                {
                    Label = "Memory Generation",
                    Description = "Generates personalized AI-written memories for colonists based on recent events.",
                    Priority = 5,
                    Weight = 2.0f,
                    CooldownTicks = 15000
                });
            RimSynapse.SynapseClient.RegisterOpportunisticTask(ModHandle, "Psychology_VisitorBackstory",
                (System.Func<bool>)API.SynapsePsychology.TriggerOpportunisticVisitorBackstory,
                new RimSynapse.Internal.OpportunisticTaskConfig
                {
                    Label = "Visitor Backstory",
                    Description = "Creates AI backstories for important NPCs during idle processing time.",
                    Priority = 2,
                    Weight = 1.0f,
                    CooldownTicks = 10000
                });
            
            RimSynapse.SynapseClient.RegisterOpportunisticTask(ModHandle, "Psychology_ProfileEvaluation",
                (System.Func<bool>)API.SynapsePsychology.TriggerOpportunisticProfileEvaluation,
                new RimSynapse.Internal.OpportunisticTaskConfig
                {
                    Label = "Clinical Evaluation",
                    Description = "Evaluates the psychological profile of colonists in the background based on their daily mood and recent events.",
                    Priority = 8, // High priority because this is core to the pawn's psychological state
                    Weight = 1.5f,
                    CooldownTicks = 5000 // Check frequently, since it only fires if a pawn is flagged
                });


            RimSynapse.SynapseClient.RegisterOpportunisticTask(ModHandle, "Psychology_RelationshipEvaluation",
                (System.Func<bool>)API.SynapsePsychology.TriggerRelationshipEvaluation,
                new RimSynapse.Internal.OpportunisticTaskConfig
                {
                    Label = "Relationship Evaluation",
                    Description = "Generates LLM relationship memories between pawns based on high familiarity or trust changes.",
                    Priority = 3,
                    Weight = 1.0f,
                    CooldownTicks = 15000
                });
            
            RimSynapse.SynapseLogger.Info("psychology", "[RimSynapse-Psychology] Mod initialized.");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            
            listingStandard.Label("Note: Debug logging is now globally configured in RimSynapse Core settings.");
            // ── Mechanics ───────────────────────────────────────────
            listingStandard.Label("Mechanics");
            listingStandard.GapLine();

            listingStandard.Label($"Memory Decay Speed: {Settings.memoryDecayMultiplier:F1}x", tooltip: "How fast colonists forget past events. Higher means they let go of grudges and trauma faster.");
            Settings.memoryDecayMultiplier = listingStandard.Slider(Settings.memoryDecayMultiplier, 0.1f, 5.0f);

            listingStandard.Label($"Sensitivity Minimum Burden Threshold: {Settings.sensitivityThreshold:F1}");
            Settings.sensitivityThreshold = listingStandard.Slider(Settings.sensitivityThreshold, 0.1f, 5.0f);

            // ── Personality & memory tuning (Stage 3) ───────────────
            listingStandard.GapLine();
            listingStandard.Label("Personality & memory tuning");

            listingStandard.Label($"Trait Shift Threshold: {Settings.shiftThreshold:F1}", tooltip: "Accumulated multi-day pressure needed before a personality trait actually changes. Higher = rarer changes.");
            Settings.shiftThreshold = listingStandard.Slider(Settings.shiftThreshold, 0.5f, 3.0f);

            listingStandard.Label($"Trait Pressure Decay/day: {Settings.shiftPressureDecay:F2}", tooltip: "How fast trait pressure fades on days without fresh evidence. Higher = a bad day lingers less.");
            Settings.shiftPressureDecay = listingStandard.Slider(Settings.shiftPressureDecay, 0.0f, 1.0f);

            listingStandard.Label($"Memory Consolidation Threshold: {Settings.consolidationThreshold:F1}", tooltip: "Relational salience needed before a memory becomes long-term. Higher = fewer memories stick.");
            Settings.consolidationThreshold = listingStandard.Slider(Settings.consolidationThreshold, 0.5f, 3.0f);

            listingStandard.Label($"Reference Consolidation Count: {Settings.referenceThreshold}", tooltip: "How many times a memory must be surfaced/referenced before it consolidates long-term.");
            Settings.referenceThreshold = (int)listingStandard.Slider(Settings.referenceThreshold, 1f, 10f);

            listingStandard.Label($"Abandonment Risk Threshold: {Settings.abandonmentThreshold:F0}", tooltip: "AbandonmentRiskScore (0-100) above which a colonist may leave. Lower = pawns leave more readily.");
            Settings.abandonmentThreshold = listingStandard.Slider(Settings.abandonmentThreshold, 50f, 100f);

            listingStandard.Label($"Suicide Damage Multiplier: {Settings.suicideDamageMultiplier:F1}x", tooltip: "Damage multiplier applied by the suicide self-harm job.");
            Settings.suicideDamageMultiplier = listingStandard.Slider(Settings.suicideDamageMultiplier, 1.0f, 10.0f);

            listingStandard.Label($"Opinion/Trust Blend: {Settings.opinionTrustBlend:F2}", tooltip: "0 = pure vanilla opinion, 1 = pure Synapse trust. 0.5 blends them evenly.");
            Settings.opinionTrustBlend = listingStandard.Slider(Settings.opinionTrustBlend, 0.0f, 1.0f);

            listingStandard.Label($"Evaluation Cadence: every {Settings.evalCadence} day(s)", tooltip: "How often the nightly psychology evaluation runs per colonist. Higher = fewer LLM calls / lower token cost.");
            Settings.evalCadence = (int)listingStandard.Slider(Settings.evalCadence, 1f, 7f);

            Settings.ApplyToCore();

            listingStandard.Gap(12f);
            if (listingStandard.ButtonText("Open Encyclopedia"))
            {
                Find.WindowStack.Add(new RimSynapse.UI.Dialog_Wiki());
            }

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "RimSynapse - Psychology";
        }
    }

    [StaticConstructorOnStartup]
    public static class PsychologyInjector
    {
        static PsychologyInjector()
        {
            InjectComp();
        }

        private static void InjectComp()
        {
            int injectedCount = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.Where(d => d.race != null && d.race.Humanlike))
            {
                if (def.comps == null)
                {
                    def.comps = new List<CompProperties>();
                }
                
                // Add the SynapsePawnComp properties if it doesn't already exist
                if (!def.comps.Any(c => c.compClass == typeof(SynapsePawnComp)))
                {
                    var props = new CompProperties
                    {
                        compClass = typeof(SynapsePawnComp)
                    };
                    def.comps.Add(props);
                    injectedCount++;
                }
            }
            
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Injected SynapsePawnComp into {injectedCount} humanlike ThingDefs.");
        }
    }
}


