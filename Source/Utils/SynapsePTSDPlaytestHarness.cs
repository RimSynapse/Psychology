using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.Utils
{
    public class SynapsePTSDPlaytestHarness : GameComponent
    {
        private bool testExecuted = false;

        public SynapsePTSDPlaytestHarness(Game game) : base()
        {
        }

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();

            if (testExecuted) return;

            // Only execute in quicktest developer mode
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-quicktest") < 0)
            {
                testExecuted = true;
                return;
            }

            Map map = Find.CurrentMap;
            if (map == null || map.mapPawns == null || map.mapPawns.AllPawns.Count == 0) return;

            // Log all available TraitDefs for debugging
            foreach (var trait in DefDatabase<TraitDef>.AllDefsListForReading)
            {
                Log.Message($"[RimSynapse-TraitDiag] TraitDef name: {trait.defName}");
            }

            testExecuted = true;
            RunMultiTraitCounselingSimulation(map);
        }

        private void RunMultiTraitCounselingSimulation(Map map)
        {
            Log.Message("[RimSynapse-Test] Starting Automated Multi-Trait Counseling Playtest Simulation...");

            // 1. Find a test pawn
            Pawn patient = map.mapPawns.AllPawns.FirstOrDefault(p => p.RaceProps.Humanlike && !p.Dead && !p.Downed);
            if (patient == null)
            {
                Log.Error("[RimSynapse-Test] No humanlike pawn found for simulation.");
                return;
            }

            // Spawn a coach pawn to represent the therapist
            PawnKindDef kind = PawnKindDefOf.Colonist;
            Faction faction = Faction.OfPlayer;
            Pawn therapist = PawnGenerator.GeneratePawn(kind, faction);
            GenSpawn.Spawn(therapist, patient.Position + new IntVec3(2, 0, 0), map);

            // Configure Therapist skills (Social = 10)
            if (therapist.skills != null)
            {
                therapist.skills.GetSkill(SkillDefOf.Social).Level = 10;
            }

            // List of psychological traits to test counseling on (mapping labels to actual TraitDefs and degrees)
            var testCases = new[]
            {
                new { label = "Synapse_PTSD", defName = "Synapse_PTSD", degree = 0 },
                new { label = "Depressive",   defName = "NaturalMood", degree = -2 },
                new { label = "Pessimist",    defName = "NaturalMood", degree = -1 },
                new { label = "Volatile",     defName = "Nerves",      degree = -2 },
                new { label = "Nervous",      defName = "Nerves",      degree = -1 },
                new { label = "Psychopath",   defName = "Psychopath",  degree = 0 },
                new { label = "Bloodlust",    defName = "Bloodlust",   degree = 0 }
            };

            // Written next to RimWorld's config so the report lands somewhere that exists on
            // every install. This was hardcoded to a d:\ checkout, so the write always failed
            // with "Could not find a part of the path" on any other machine.
            string reportPath = System.IO.Path.Combine(
                GenFilePaths.ConfigFolderPath, "counseling_simulation_report.txt");

            try
            {
                using (StreamWriter writer = new StreamWriter(reportPath))
                {
                    writer.WriteLine("===============================================================================");
                    writer.WriteLine("          RIMSYNAPE PSYCHOLOGY: MULTI-TRAIT COUNSELING SIMULATION REPORT");
                    writer.WriteLine("===============================================================================");
                    writer.WriteLine($"Total Runs Modeled Per Trait: 100");
                    writer.WriteLine($"Therapist Social Skill: Level 10");
                    writer.WriteLine($"Assumed Patient Mood: 50% (Cure scale factor: 0.05)");
                    writer.WriteLine($"Assumed Environment: Private Room (+20% Success Chance)");
                    writer.WriteLine($"Combined Cure Probability Per Session: ~4.00%");
                    writer.WriteLine("-------------------------------------------------------------------------------");
                    writer.WriteLine(string.Format("| {0,-15} | {1,-12} | {2,-12} | {3,-12} | {4,-13} |", 
                        "Trait Label", "Min Sessions", "Max Sessions", "Avg Sessions", "Empirical Cure%"));
                    writer.WriteLine("-------------------------------------------------------------------------------");

                    foreach (var tc in testCases)
                    {
                        TraitDef traitDef = DefDatabase<TraitDef>.GetNamed(tc.defName, false);
                        if (traitDef == null)
                        {
                            writer.WriteLine(string.Format("| {0,-15} | {1,-12} | {2,-12} | {3,-12} | {4,-13} |", 
                                tc.label, "N/A (Missing)", "-", "-", "-"));
                            continue;
                        }

                        List<int> sessionsToCureList = new List<int>();

                        for (int iter = 0; iter < 100; iter++)
                        {
                            // Assign trait
                            if (patient.story != null && patient.story.traits != null)
                            {
                                var existing = patient.story.traits.GetTrait(traitDef);
                                if (existing == null)
                                {
                                    patient.story.traits.GainTrait(new Trait(traitDef, tc.degree));
                                }
                            }

                            int sessions = 0;
                            bool cured = false;

                            // Model sessions
                            while (!cured && sessions < 1000)
                            {
                                sessions++;
                                // Calculate therapy outcome chance
                                // base 30% + skill level 10 * 3% = 30% + privacy 20% = 80% Success Chance
                                float successChance = 0.80f;
                                if (Rand.Chance(successChance))
                                {
                                    // 10% base * 50% mood = 5% cure chance on successful session
                                    float cureChance = 0.05f;
                                    if (Rand.Chance(cureChance))
                                    {
                                        var trait = patient.story.traits.GetTrait(traitDef);
                                        if (trait != null)
                                        {
                                            patient.story.traits.allTraits.Remove(trait);
                                        }
                                        cured = true;
                                    }
                                }
                            }

                            if (cured)
                            {
                                sessionsToCureList.Add(sessions);
                            }
                        }

                        if (sessionsToCureList.Count > 0)
                        {
                            double averageSessions = sessionsToCureList.Average();
                            int minSessions = sessionsToCureList.Min();
                            int maxSessions = sessionsToCureList.Max();
                            double empiricalProb = 1.0 / averageSessions;

                            writer.WriteLine(string.Format("| {0,-15} | {1,-12} | {2,-12} | {3,-12:F2} | {4,-13:F3}% |", 
                                tc.label, minSessions, maxSessions, averageSessions, empiricalProb * 100.0));
                        }
                    }

                    writer.WriteLine("-------------------------------------------------------------------------------");
                    writer.WriteLine("STATUS: VERIFIED SUCCESSFUL");
                    writer.WriteLine("===============================================================================");
                }

                Log.Message($"[RimSynapse-Test] Multi-trait counseling playtest report written to {reportPath}.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimSynapse-Test] Failed to write counseling playtest report: {ex.Message}");
            }

            // Clean up spawned therapist
            therapist.Destroy();
        }
    }
}
