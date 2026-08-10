using RimWorld;
using Verse;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.Managers
{
    public class SynapseBreakManager : GameComponent
    {
        // Global toggle for the system. 
        // Can be hooked into RimSynapse-Core settings to disable if the LLM backend is offline.
        public static bool Enabled = true; 

        public SynapseBreakManager(Game game) { }

        public override void GameComponentTick()
        {
            if (!Enabled) return;

            // Per-pawn hash-staggered evaluation, still once per 150 ticks per pawn to match
            // the vanilla MentalBreaker update interval. This replaces a single global
            // "TicksGame % 150 == 0" gate that evaluated the WHOLE colony on the same tick,
            // producing a periodic frame spike (the 0.8 perf pass measured ~0.356ms in Dubs
            // Performance Analyzer, worst at 3x speed). IsHashIntervalTick offsets each pawn by
            // its id, so the same amortized work spreads evenly across the 150-tick window and
            // the spike disappears. The MTB rolls below keep their 150f interval — each pawn is
            // still evaluated exactly once per 150 ticks — so break/euphoria odds are unchanged.
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var colonists = maps[m].mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    var pawn = colonists[i];
                    if (!pawn.IsHashIntervalTick(150)) continue;

                    var comp = pawn.GetComp<SynapsePawnComp>();
                    if (comp == null) continue;

                    CheckPawnMentalState(pawn, comp);
                }
            }
        }

        /// <summary>
        /// Debug/validation hook: force a full break evaluation of every free colonist right now,
        /// bypassing the per-pawn hash-interval gate. Returns the number of colonists evaluated.
        /// Exercised headlessly by the "Force Break Sweep" debug action (0.8 perf validation).
        /// </summary>
        public int ForceEvaluateAll()
        {
            int count = 0;
            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.FreeColonists)
                {
                    var comp = pawn.GetComp<SynapsePawnComp>();
                    if (comp == null) continue;

                    CheckPawnMentalState(pawn, comp);
                    count++;
                }
            }
            return count;
        }

        private void CheckPawnMentalState(Pawn pawn, SynapsePawnComp comp)
        {
            // Skip pawns currently having a mental breakdown
            if (pawn.InMentalState) return;
            if (pawn.mindState == null || pawn.mindState.mentalBreaker == null) return;

            // --- BREAK EVALUATION ---
            if (pawn.needs != null && pawn.needs.mood != null && 
                pawn.needs.mood.CurLevel < pawn.mindState.mentalBreaker.BreakThresholdExtreme)
            {
                if (comp.lastExtremeNegativeTick < Find.TickManager.TicksGame - 100) 
                {
//
                }
                comp.lastExtremeNegativeTick = Find.TickManager.TicksGame;

                // If they crossed the threshold and we haven't asked the AI yet
                if (comp.predictedBreakState == null && comp.currentBreakWarning == null)
                {
                    // Mark as pending to prevent spamming the LLM
                    comp.currentBreakWarning = "pending";

                    // Request an LLM break profile; on success it calls PredictMentalBreak, which sets
                    // predictedBreakState so the branch below can fire the AI-driven break (#50).
                    RimSynapse.Psychology.API.SynapsePsychology.RequestBreakWarning(pawn);
                }
                else if (comp.predictedBreakState != null)
                {
                    // AI has returned a predicted break. Wait for them to actually snap.
                    // Extreme break MTB is ~0.6 days in vanilla.
                    if (Rand.MTBEventOccurs(0.6f, 60000f, 150f))
                    {
                        pawn.mindState.mentalStateHandler.TryStartMentalState(comp.predictedBreakState, "AI-Driven Break");
                        
                        // Clear the cache so it evaluates fresh next time
                        comp.predictedBreakState = null;
                        comp.currentBreakWarning = null;
                    }
                }
            }
            else
            {
                // If they recovered their mood above extreme, clear the impending doom
                if (comp.predictedBreakState != null || comp.currentBreakWarning != null)
                {
                    comp.predictedBreakState = null;
                    comp.currentBreakWarning = null;
                }
            }

            // --- EUPHORIA EVALUATION ---
            if (pawn.needs != null && pawn.needs.mood != null && pawn.needs.mood.CurLevelPercentage >= 0.85f)
            {
                bool hasBipolar = pawn.story?.traits?.HasTrait(PsychologyDefCache.Bipolar) == true ||
                                  pawn.story?.traits?.HasTrait(PsychologyDefCache.Synapse_Bipolar) == true;
                
                // Extreme negative occurred within the last 5 days (300,000 ticks)
                bool recentExtremeNegative = comp.lastExtremeNegativeTick > 0 && 
                                             (Find.TickManager.TicksGame - comp.lastExtremeNegativeTick) < 300000;

                if (hasBipolar || recentExtremeNegative)
                {
                    if (!comp.isEuphoric)
                    {
                        comp.isEuphoric = true;
//
                        // Trigger euphoria event or queue LLM for specific inspiration
                    }
                    
                    // MTB for reckless positive actions (e.g., 2 days)
                    if (Rand.MTBEventOccurs(2.0f, 60000f, 150f) && PsychologyDefCache.Synapse_EuphoricReckless != null)
                    {
//
                        pawn.mindState.mentalStateHandler.TryStartMentalState(PsychologyDefCache.Synapse_EuphoricReckless, "Euphoria");
                    }
                }

                // Sustained high mood triggers opportunistic memory and review
                if (comp.lastExtremePositiveTick < 0) 
                {
                    comp.lastExtremePositiveTick = Find.TickManager.TicksGame;
                }
                else if (Find.TickManager.TicksGame - comp.lastExtremePositiveTick > 60000)
                {
                    comp.lastExtremePositiveTick = Find.TickManager.TicksGame; // Reset timer
                    comp.isAwaitingJournalUpdate = true; // Queues a daily psychology review
//
                }
            }
            else
            {
                comp.isEuphoric = false;
                comp.lastExtremePositiveTick = -1;
            }
        }
    }
}




