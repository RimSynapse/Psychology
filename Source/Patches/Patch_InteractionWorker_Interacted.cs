using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.Models;

namespace RimSynapse.Psychology.Patches
{
    /// <summary>
    /// Every interaction moves the relationship compass (#72). Effects now land on the RIGHT axis: casual
    /// chit-chat and deep talks build WARMTH (liking), deep talks also build TRUST (you open up to people you
    /// rely on), insults/slights sour warmth (and insults, trust). Familiarity still grows on ANY interaction —
    /// it is the depth signal, not a friendship one. Compulsion control makes itself felt here: a VOLATILE pawn
    /// who already dislikes the other can't keep a friendly exchange civil — it curdles — while a controlled
    /// pawn feels the same and stays neutral.
    /// </summary>
    [HarmonyPatch(typeof(InteractionWorker), "Interacted")]
    public static class Patch_InteractionWorker_Interacted
    {
        public static void Postfix(InteractionWorker __instance, Pawn initiator, Pawn recipient)
        {
            if (initiator == null || recipient == null) return;
            if (!SynapseRelationships.TryPair(initiator, recipient, out var initRec, out var recRec)) return;

            var def = __instance.interaction;

            // Familiarity grows on ANY interaction (hostile included) — the depth/confidence signal.
            initRec.AddFamiliarity(2f);
            recRec.AddFamiliarity(2f);

            // #23: notify the player once when this pair first crosses a named familiarity milestone.
            SynapseFamiliarityMilestones.CheckAndNotify(initiator, recipient, initRec, recRec);

            var slightDef = DefDatabase<InteractionDef>.GetNamed("Slight", false);
            if (def == InteractionDefOf.DeepTalk)
            {
                SynapseRelationships.AwardWarmth(initiator, recipient, 2f); // opening up warms AND builds trust
                SynapseRelationships.AwardTrust(initiator, recipient, 2f);
            }
            else if (def == InteractionDefOf.Chitchat)
            {
                SynapseRelationships.AwardWarmth(initiator, recipient, 1f); // pleasant company, no reliance implied
            }
            else if (def == InteractionDefOf.Insult)
            {
                SynapseRelationships.AwardWarmth(initiator, recipient, -4f);
                SynapseRelationships.AwardTrust(initiator, recipient, -3f);
            }
            else if (slightDef != null && def == slightDef)
            {
                SynapseRelationships.AwardWarmth(initiator, recipient, -2f);
            }

            // Compulsion-gated souring (#72): the initiator already resents the recipient (cold warmth) AND is
            // volatile enough to show it — a friendly exchange turns frosty. One-sided: only the initiator lets
            // it show. A controlled pawn with the same resentment keeps it civil, so nothing extra happens.
            if ((def == InteractionDefOf.Chitchat || def == InteractionDefOf.DeepTalk) && initRec.warmth <= -40f)
            {
                float magnitude = (-initRec.warmth) / 100f;
                if (SynapseCompulsion.WouldActOn(magnitude, SynapseCompulsion.Effective(initiator, initiator.GetComp<SynapsePawnComp>())))
                    SynapseRelationships.AwardWarmthDirected(initiator, recipient, -3f);
            }
        }
    }
}
