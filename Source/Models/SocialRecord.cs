using System;
using Verse;

namespace RimSynapse.Psychology.Models
{
    public class SocialRecord : IExposable
    {
        // The relationship "compass" (#72). Two independent axes plus a depth scalar:
        //  • trust      (-100..100): do they RELY on each other? Earned through shared trials (fighting
        //                 together, tending, rescue, deep talks) — NOT chit-chat volume.
        //  • warmth     (-100..100): do they LIKE each other? Seeded by personality compatibility and moved
        //                 by affectionate/hostile interactions. Cold+trusted and warm+untrusted are both valid.
        //  • familiarity (0..100):   how well they KNOW each other. Grows on ANY interaction (hostile included),
        //                 so it is the confidence/depth gate, never a friendship signal on its own.
        public float trust = 0f;
        public float warmth = 0f;
        public float familiarity = 0f;
        public System.Collections.Generic.List<string> relationshipMemories = new System.Collections.Generic.List<string>();

        /// <summary>Highest named familiarity milestone this relationship has reached (index into
        /// <see cref="RimSynapse.Psychology.API.SynapseFamiliarityMilestones.Milestones"/>), or -1 for none (#23).
        /// Persisted so each threshold notifies the player exactly once — never again on load, and never re-firing
        /// if familiarity dips below the band and climbs back.</summary>
        public int highestFamiliarityMilestone = -1;


        public SocialRecord()
        {
        }

        public void AddFamiliarity(float amount)
        {
            familiarity = UnityEngine.Mathf.Clamp(familiarity + amount, 0f, 100f);
        }

        public void AddTrust(float amount)
        {
            trust = UnityEngine.Mathf.Clamp(trust + amount, -100f, 100f);
        }

        public void AddWarmth(float amount)
        {
            warmth = UnityEngine.Mathf.Clamp(warmth + amount, -100f, 100f);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref trust, "trust", 0f);
            Scribe_Values.Look(ref warmth, "warmth", 0f);
            Scribe_Values.Look(ref familiarity, "familiarity", 0f);
            // Default -1 so a save predating milestones loads as "no milestone reached yet".
            Scribe_Values.Look(ref highestFamiliarityMilestone, "highestFamiliarityMilestone", -1);
            Scribe_Collections.Look(ref relationshipMemories, "relationshipMemories", LookMode.Value);
            
            if (Scribe.mode == LoadSaveMode.LoadingVars && relationshipMemories == null)
            {
                relationshipMemories = new System.Collections.Generic.List<string>();
            }
        }
    }
}
