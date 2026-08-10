using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Models;
using RimSynapse.Internal;
using RimSynapse.Psychology.Models;

namespace RimSynapse.Psychology.Comps
{
    public enum BreakCategory
    {
        Default,
        Homicidal,
        Suicidal,
        IssueAverse
    }

    public enum BreakIntensity
    {
        Light,
        Medium,
        Severe
    }

    public partial class SynapsePawnComp : ThingComp
    {
        // Track whether this pawn has had a backstory memory generated yet.
        // This helps queue LLM calls safely instead of freezing the game on spawn.
        public bool hasBackstoryMemory = false;
        private int ticksToGenerateBackstory = 0; // Fire immediately on first tick

        // Active AI-driven modifiers
        public BreakCategory breakCategory = BreakCategory.Default;
        public bool ideologyZealot = false;

        // Async LLM Break Data
        public MentalStateDef predictedBreakState = null;
        public string currentBreakWarning = null;
        public bool isEuphoric = false;
        
        // Long-term Context Modifiers
        public float breakDurationHours = 6f; // Default 6 hours
        public BreakIntensity breakIntensity = BreakIntensity.Medium;

        // Psychological Profile Data
        public Dictionary<string, string> medicalProfile = new Dictionary<string, string>();
        
        // Social / Trust
        public Dictionary<string, SocialRecord> socialNetwork = new Dictionary<string, SocialRecord>();
        private int socialTickCounter = 0;

        // Daily sleep tracking
        private bool wasAsleep = false;
        private float dailyMoodAccumulator = 0f;
        private int moodSamples = 0;
        
        public int lastExtremeNegativeTick = -1;
        public int lastExtremePositiveTick = -1;
        
        public List<RimSynapse.Psychology.Models.DynamicTraitRecord> dynamicTraits = new List<RimSynapse.Psychology.Models.DynamicTraitRecord>();
        
        public List<RimSynapse.Psychology.Models.TherapyTranscript> therapyTranscripts = new List<RimSynapse.Psychology.Models.TherapyTranscript>();
        
        // Therapy Readiness State
        public bool isTherapyReady = true;
        public string therapyBlockReason = "";
        
        public int lastJournalUpdateDay = -1;
        public bool isAwaitingJournalUpdate = false;
        public float savedAverageMood = 0.5f;

        // Stage 2: cooldown gate between AI-driven trait changes (#46). Absolute tick of the last change.
        public long lastTraitChangeTick = -1;

        private const int TickIntervalDay = 60000;
        private const int TickInterval6Hours = 15000;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hasBackstoryMemory, "hasBackstoryMemory", false);
            
            Scribe_Values.Look(ref breakCategory, "breakCategory", BreakCategory.Default);
            Scribe_Values.Look(ref ideologyZealot, "ideologyZealot", false);

            Scribe_Defs.Look(ref predictedBreakState, "predictedBreakState");
            Scribe_Values.Look(ref currentBreakWarning, "currentBreakWarning");
            Scribe_Values.Look(ref isEuphoric, "isEuphoric", false);
            
            Scribe_Values.Look(ref breakDurationHours, "breakDurationHours", 6f);
            Scribe_Values.Look(ref breakIntensity, "breakIntensity", BreakIntensity.Medium);
            Scribe_Values.Look(ref wasAsleep, "wasAsleep", false);
            Scribe_Values.Look(ref lastExtremeNegativeTick, "lastExtremeNegativeTick", -1);
            Scribe_Values.Look(ref lastExtremePositiveTick, "lastExtremePositiveTick", -1);
            Scribe_Values.Look(ref ticksToGenerateBackstory, "ticksToGenerateBackstory", 0);
            Scribe_Values.Look(ref lastJournalUpdateDay, "lastJournalUpdateDay", -1);
            Scribe_Values.Look(ref isAwaitingJournalUpdate, "isAwaitingJournalUpdate", false);
            Scribe_Collections.Look(ref dynamicTraits, "dynamicTraits", LookMode.Deep);
            Scribe_Collections.Look(ref therapyTranscripts, "therapyTranscripts", LookMode.Deep);

            if (dynamicTraits == null) dynamicTraits = new List<RimSynapse.Psychology.Models.DynamicTraitRecord>();
            if (therapyTranscripts == null) therapyTranscripts = new List<RimSynapse.Psychology.Models.TherapyTranscript>();
            Scribe_Values.Look(ref savedAverageMood, "savedAverageMood", 0.5f);
            Scribe_Values.Look(ref lastTraitChangeTick, "lastTraitChangeTick", -1L);
            Scribe_Values.Look(ref hasCheckedAdulthood, "hasCheckedAdulthood", false);

            Scribe_Collections.Look(ref medicalProfile, "medicalProfile", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref socialNetwork, "socialNetwork", LookMode.Value, LookMode.Deep);
            Scribe_Values.Look(ref socialTickCounter, "socialTickCounter", 0);
            
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (medicalProfile == null) medicalProfile = new Dictionary<string, string>();
                if (socialNetwork == null) socialNetwork = new Dictionary<string, SocialRecord>();
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (parent is Pawn pawn && !pawn.Dead)
            {
                // We rely entirely on CompTick for backstory generation
                // Calling PromptAsync during map loading can drop the request and get isGeneratingBackstory stuck.
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            
            if (!parent.IsHashIntervalTick(250)) return;

            if (parent is Pawn pawn && pawn.Spawned && !pawn.Dead)
            {
                // Async Backstory Stub
                if (!hasBackstoryMemory && pawn.Faction == Faction.OfPlayer && !isGeneratingBackstory)
                {
                    ticksToGenerateBackstory -= 250;
                    if (ticksToGenerateBackstory <= 0)
                    {
                        GenerateAIBackstory(pawn);
                    }
                }

                // LLM-driven adulthood backstory for colony-born pawns turning 20
                if (hasBackstoryMemory && !hasCheckedAdulthood)
                {
                    CheckAdulthoodBackstoryNeeded(pawn);
                }

                // Voice backfill (#33): a pawn with a personality but no voice yet (e.g. a save predating
                // voices) gets one derived once. New pawns get their voice with the profile itself.
                if (hasBackstoryMemory && !isGeneratingVoice)
                {
                    var core = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
                    if (core != null && !core.voiceGenerated && !string.IsNullOrEmpty(core.personalitySummary))
                    {
                        DeriveVoiceProfile(pawn, core);
                    }
                }

                // Sleep Tracking & Daily Review (TickRare is 250 ticks). Only for pawns the colony has a
                // relationship with (#39) — raiders and passing traders are gated out cheaply here.
                if (pawn.needs != null && pawn.needs.mood != null
                    && RimSynapse.Psychology.API.SynapsePsychology.IsEligibleForReview(pawn))
                {
                    dailyMoodAccumulator += pawn.needs.mood.CurLevelPercentage;
                    moodSamples++;
                    
                    int currentDay = GenDate.DaysPassed;

                    // Cadence gate (Stage 3, #49): run the eval every N days per colonist. Default 1 = nightly.
                    int cadence = RimSynapse.Psychology.RimSynapsePsychologyMod.Settings?.evalCadence ?? 1;
                    if (cadence < 1) cadence = 1;
                    if (currentDay - lastJournalUpdateDay >= cadence && !isAwaitingJournalUpdate)
                    {
                        bool isAsleep = pawn.jobs != null && pawn.jobs.curDriver != null && pawn.jobs.curDriver.asleep;
                        int currentHour = GenDate.HourOfDay(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(pawn.Map.Tile).x);

                        // Trigger if they just fell asleep OR if it's late (22:00) and they don't sleep
                        if ((isAsleep && !wasAsleep) || currentHour >= 22)
                        {
                            isAwaitingJournalUpdate = true;
                            savedAverageMood = moodSamples > 0 ? (dailyMoodAccumulator / moodSamples) : pawn.needs.mood.CurLevelPercentage;
                            
//

                            // Reset daily tracking immediately so we can start recording the next day
                            dailyMoodAccumulator = 0f;
                            moodSamples = 0;
                            
                            CheckTherapyReadiness(pawn);
                        }
                        
                        wasAsleep = isAsleep;
                    }
                }

                // Passive Familiarity Growth & Decay
                socialTickCounter++;
                if (socialTickCounter >= 10) // Every 2500 ticks (~1 hour)
                {
                    socialTickCounter = 0;
                    UpdateSocialNetwork(pawn);
                }
            }

            // Prune old transcripts (older than 7 days)
            if (therapyTranscripts != null && therapyTranscripts.Count > 0)
            {
                int currentTick = Find.TickManager.TicksGame;
                therapyTranscripts.RemoveAll(t => currentTick - t.sessionTick > 420000); // 7 days * 60000 ticks
            }
        }

        private void UpdateSocialNetwork(Pawn pawn)
        {
            if (pawn.Map == null || pawn.Faction != Faction.OfPlayer) return;

            var room = pawn.GetRoom();
            
            // 1. Decay all familiarity slightly (-0.2 per hour = -4.8 per day)
            // It takes ~20 days to lose 100 familiarity if they never see each other.
            var keys = socialNetwork.Keys.ToList();
            foreach (var key in keys)
            {
                var record = socialNetwork[key];
                record.AddFamiliarity(-0.2f);
            }

            // 2. Grow familiarity for pawns nearby/same room
            foreach (var other in pawn.Map.mapPawns.FreeColonists)
            {
                if (other == pawn) continue;
                
                bool near = false;
                if (room != null && room == other.GetRoom()) near = true;
                else if (pawn.Position.DistanceTo(other.Position) < 10f) near = true;

                if (near)
                {
                    var otherId = other.GetUniqueLoadID();
                    if (!socialNetwork.ContainsKey(otherId)) socialNetwork[otherId] = new SocialRecord();
                    
                    // Add growth (+0.5 per hour = +12 per day max)
                    // Overcomes decay
                    socialNetwork[otherId].AddFamiliarity(0.5f);
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent is Pawn pawn && pawn.Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Psychology",
                    defaultDesc = "View this pawn's psychological profile, traits, and journal of core memories.",
                    icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Commands/PsychologyIcon", true),
                    action = () =>
                    {
                        Find.WindowStack.Add(new UI.Dialog_PawnPsychology(pawn));
                    }
                };
            }
        }

        private void CheckTherapyReadiness(Pawn pawn)
        {
            if (pawn.needs == null || pawn.needs.mood == null)
            {
                isTherapyReady = false;
                therapyBlockReason = "Incapable of feeling mood.";
                return;
            }

            if (pawn.needs.mood.CurLevelPercentage < 0.1f)
            {
                isTherapyReady = false;
                therapyBlockReason = "Mental state too unstable for therapy.";
                return;
            }
            
            // Check for locked traits
            if (pawn.story != null)
            {
                bool isPsychopath = pawn.story.traits.HasTrait(TraitDefOf.Psychopath);
                if (isPsychopath && (pawn.story.Childhood?.identifier?.Contains("Assassin") == true || pawn.story.Adulthood?.identifier?.Contains("Assassin") == true))
                {
                    // This is just an example of a backstory lock.
                    // For now, we won't block the ENTIRE therapy job for a locked trait, because they might still need therapy for mood!
                    // The job itself handles if the trait is cured.
                }
            }

            isTherapyReady = true;
            therapyBlockReason = "";
        }
    }
}




