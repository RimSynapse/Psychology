using System;
using Verse;

namespace RimSynapse.Psychology.Models
{
    public class SocialRecord : IExposable
    {
        public float trust = 0f;
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

        public void ExposeData()
        {
            Scribe_Values.Look(ref trust, "trust", 0f);
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
