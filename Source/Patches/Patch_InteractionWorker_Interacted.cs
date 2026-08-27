using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.Models;

namespace RimSynapse.Psychology.Patches
{
    [HarmonyPatch(typeof(InteractionWorker), "Interacted")]
    public static class Patch_InteractionWorker_Interacted
    {
        public static void Postfix(InteractionWorker __instance, Pawn initiator, Pawn recipient)
        {
            if (initiator == null || recipient == null) return;
            
            var initComp = initiator.GetComp<SynapsePawnComp>();
            var recComp = recipient.GetComp<SynapsePawnComp>();
            
            if (initComp != null && recComp != null)
            {
                string recId = recipient.GetUniqueLoadID();
                string initId = initiator.GetUniqueLoadID();
                
                if (!initComp.socialNetwork.ContainsKey(recId)) initComp.socialNetwork[recId] = new SocialRecord();
                if (!recComp.socialNetwork.ContainsKey(initId)) recComp.socialNetwork[initId] = new SocialRecord();
                
                var initRec = initComp.socialNetwork[recId];
                var recRec = recComp.socialNetwork[initId];
                
                // Active Growth: Familiarity goes up on any interaction
                initRec.AddFamiliarity(2f);
                recRec.AddFamiliarity(2f);

                // #23: notify the player once when this pair first crosses a named familiarity milestone.
                RimSynapse.Psychology.API.SynapseFamiliarityMilestones.CheckAndNotify(initiator, recipient, initRec, recRec);
                
                // Trust changes based on interaction type
                var slightDef = DefDatabase<InteractionDef>.GetNamed("Slight", false);
                if (__instance.interaction == InteractionDefOf.Chitchat || __instance.interaction == InteractionDefOf.DeepTalk)
                {
                    float trustGained = __instance.interaction == InteractionDefOf.DeepTalk ? 3f : 1f;
                    initRec.AddTrust(trustGained);
                    recRec.AddTrust(trustGained);
                }
                else if (__instance.interaction == InteractionDefOf.Insult || (__instance.interaction == slightDef && slightDef != null))
                {
                    float trustLost = __instance.interaction == InteractionDefOf.Insult ? -5f : -2f;
                    initRec.AddTrust(trustLost);
                    recRec.AddTrust(trustLost);
                }
            }
        }
    }
}
