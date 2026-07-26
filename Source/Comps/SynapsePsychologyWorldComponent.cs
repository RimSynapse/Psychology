using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using PawnEventRecord = RimSynapse.PawnEventRecord;

namespace RimSynapse.Psychology.Comps
{
    public class SynapsePsychologyWorldComponent : WorldComponent
    {
        public Dictionary<string, string> funeralRecords = new Dictionary<string, string>();
        private bool autotestRun = false;
        private int checkTicks = -1;

        public SynapsePsychologyWorldComponent(World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref funeralRecords, "funeralRecords", LookMode.Value, LookMode.Value);
            
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (funeralRecords == null) funeralRecords = new Dictionary<string, string>();
            }
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-quicktest") >= 0 && !autotestRun)
            {
                autotestRun = true;
                LongEventHandler.QueueLongEvent(() => RunSynapseAutotest(), "Running Synapse Autotests", false, null);
            }
        }

        private void RunSynapseAutotest()
        {
            RimSynapse.SynapseLogger.Info("psychology", "[RimSynapse-Autotest] Starting Synapse Psychology Autotest...");
            try
            {
                Map map = Find.CurrentMap;
                if (map == null)
                {
                    RimSynapse.SynapseLogger.Warn("psychology", "[RimSynapse-Autotest] Map is null, cannot autotest.");
                    return;
                }

                var pawns = map.mapPawns.FreeColonists.ToList();
                if (pawns.Count < 2)
                {
                    RimSynapse.SynapseLogger.Warn("psychology", "[RimSynapse-Autotest] Not enough colonists to run autotest.");
                    return;
                }

                Pawn speaker = pawns[0];
                Pawn deceased = pawns[1];

                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Autotest] Test speaker: {speaker.Name.ToStringShort}, Deceased: {deceased.Name.ToStringShort}");

                var startState = new Patches.Patch_Funeral_Apply.FuneralStartState
                {
                    weather = map.weatherManager.curWeather.label,
                    timeOfDay = "afternoon",
                    averageMood = 0.5f,
                    moodReason = "grief",
                    averageOpinion = 0f
                };

                var attendees = new HashSet<Pawn> { speaker };

                Patches.Patch_Funeral_Apply.GenerateFuneralRecordAndMemories(deceased, "good", startState, attendees, 0.4f, 0.02f);
                RimSynapse.SynapseLogger.Info("psychology", "[RimSynapse-Autotest] Triggered GenerateFuneralRecordAndMemories.");
                
                checkTicks = 120; // Check in 2 seconds (120 ticks)
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Error("psychology", "[RimSynapse-Autotest] Autotest failed with exception: " + ex.ToString());
            }
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            
            if (checkTicks > 0)
            {
                checkTicks--;
                if (checkTicks == 0)
                {
                    VerifyAutotestResults();
                }
            }
        }

        private void VerifyAutotestResults()
        {
            RimSynapse.SynapseLogger.Info("psychology", "[RimSynapse-Autotest] Verifying autotest results...");
            try
            {
                var map = Find.CurrentMap;
                if (map == null) return;
                var pawns = map.mapPawns.FreeColonists.ToList();
                if (pawns.Count < 2) return;
                Pawn deceased = pawns[1];

                bool hasRecord = funeralRecords.ContainsKey(deceased.GetUniqueLoadID());
                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Autotest] Results checked. HasRecord: {hasRecord}");

                if (hasRecord)
                {
                    RimSynapse.SynapseLogger.Info("psychology", "[RimSynapse-Autotest] SUCCESS: Autotest completed successfully!");
                }
                else
                {
                    RimSynapse.SynapseLogger.Error("psychology", "[RimSynapse-Autotest] FAILURE: Autotest failed, funeral record not found.");
                }
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Error("psychology", "[RimSynapse-Autotest] VerifyAutotestResults exception: " + ex.ToString());
            }
        }
    }
}
