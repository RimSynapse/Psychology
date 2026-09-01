using System;
using System.Linq;
using System.Collections.Generic;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.Models;
using RimSynapse.Utils;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// The nightly relationship review (#72 Phase 4b): after a colonist's personality eval finishes, they weigh
    /// how they feel about the people they live with — one LLM call, their directed view of their most
    /// significant relationships. The model DETERMINES (reconsiders each bond against both characters + the
    /// history, refines the pawn's compulsion control); the tested C# scaffold (<see cref="SynapseRelationshipReview"/>)
    /// APPLIES it, bounded and with rivalry/betrayal gated. Compatibility is computed in C# and handed in, so the
    /// model reasons on top of it rather than re-deriving it.
    /// </summary>
    public static partial class SynapsePsychology
    {
        private const int MaxRelationshipsPerReview = 5;

        /// <summary>How significant a relationship is (for picking which few to reconsider tonight).</summary>
        private static float RelationshipSignificance(SocialRecord r)
            => r == null ? 0f : r.familiarity + Math.Abs(r.warmth) + Math.Abs(r.trust);

        /// <summary>
        /// Fire one colonist's nightly relationship review, if they have relationships worth reconsidering and
        /// haven't already reviewed today. Chains off the completed daily review, so the personality read feeds in.
        /// </summary>
        public static bool QueueRelationshipReview(Pawn pawn)
        {
            if (pawn?.Map == null) return false;
            var comp = pawn.GetComp<SynapsePawnComp>();
            var core = pawn.GetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (comp == null || core == null || comp.socialNetwork == null) return false;

            int today = GenDate.DaysPassed;
            if (comp.lastRelationshipReviewDay == today) return false; // once a day
            comp.lastRelationshipReviewDay = today;

            // Resolve the colonists this pawn knows, keep the significant ones, take the top few.
            var colonists = pawn.Map.mapPawns.FreeColonists;
            var picks = new List<(Pawn other, SocialRecord rec)>();
            foreach (var kv in comp.socialNetwork)
            {
                if (kv.Value == null || RelationshipSignificance(kv.Value) < 20f) continue;
                var other = colonists.FirstOrDefault(c => c != pawn && c.GetUniqueLoadID() == kv.Key);
                if (other != null) picks.Add((other, kv.Value));
            }
            if (picks.Count == 0) return false;
            picks = picks.OrderByDescending(p => RelationshipSignificance(p.rec)).Take(MaxRelationshipsPerReview).ToList();

            // Name → pawn, for resolving the model's "who" back to a colonist at apply time.
            var byName = new Dictionary<string, Pawn>();
            foreach (var (other, _) in picks) byName[other.Name.ToStringShort] = other;

            string systemPrompt =
$@"You are {pawn.Name.ToStringShort}'s honest inner read on the people they live with. You have just reflected on yourself; now weigh how you truly feel about each person listed.

For EACH, decide whether your current WARMTH (how much you like them, -100..100) and TRUST (how much you rely on them, -100..100) still ring true given BOTH your characters and your shared history. Nudge them only if they have drifted out of line, and give one honest, in-character sentence saying why.

Rules:
- Only the people listed. NEVER invent anyone.
- Change is slow: keep each nightly nudge small. warmthDelta and trustDelta must be within -12..12.
- 'kind' is one of: bonding, friction, jealousy, rivalry, betrayal, reconciliation.
  RESERVE 'rivalry' and 'betrayal' for genuinely serious, EARNED turns — betrayal is a real breach by someone you trusted; rivalry is deep, sustained mutual antagonism between people who know each other well. NEVER use them over something small.
- Also rate your own emotional control: 'compulsionControl' 0..1, where 0 = you act on every feeling and 1 = you keep it all inside. Base it on your temperament.

Respond ONLY as valid JSON, no markdown:
{{ ""compulsionControl"": 0.5, ""relationships"": [ {{ ""who"": ""<name>"", ""warmthDelta"": 0, ""trustDelta"": 0, ""reason"": ""<one sentence>"", ""kind"": ""<kind>"" }} ] }}";

            string me = !string.IsNullOrWhiteSpace(core.personalitySummary)
                ? core.personalitySummary
                : (pawn.story?.traits?.allTraits != null ? string.Join(", ", pawn.story.traits.allTraits.Select(t => t.Label)) : "an ordinary colonist");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"You are {pawn.Name.ToStringShort} ({pawn.gender}).");
            sb.AppendLine($"Your character: {me}");
            sb.AppendLine();
            sb.AppendLine("The people you know:");
            foreach (var (other, rec) in picks)
            {
                var reasons = new List<string>();
                SynapseCompatibility.Score(pawn, other, reasons);
                string traits = other.story?.traits?.allTraits != null
                    ? string.Join(", ", other.story.traits.allTraits.Select(t => t.Label)) : "unknown";
                string recent = rec.relationshipMemories != null && rec.relationshipMemories.Count > 0
                    ? rec.relationshipMemories[rec.relationshipMemories.Count - 1] : "nothing in particular";
                sb.AppendLine($"- {other.Name.ToStringShort} ({other.gender}) — their traits: {traits}. " +
                              $"You feel warmth {rec.warmth:F0}, trust {rec.trust:F0}, familiarity {rec.familiarity:F0}. " +
                              $"Your natures: {(reasons.Count > 0 ? string.Join(", ", reasons) : "neither clash nor click")}. " +
                              $"Recently: \"{recent}\"");
            }
            sb.Append(RimSynapse.SynapseCoreContext.GatherGenericContext(pawn, RimSynapse.SynapseContextTypes.RelationshipEvaluation));

            var options = new ChatOptions { priority = 3, requestName = "Relationship Review", targetName = pawn.Name.ToStringShort };
            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                sb.ToString(),
                result =>
                {
                    if (!result.success) return;
                    var parsed = SynapseRelationshipReview.Parse(result.content);
                    if (parsed == null) return;
                    SynapseGameComponent.Enqueue(() =>
                    {
                        var applied = SynapseRelationshipReview.Apply(pawn, parsed, name => byName.TryGetValue(name, out var t) ? t : null);
                        if (applied.Count > 0)
                            RimSynapse.SynapseLogger.Info("psychology",
                                $"[RimSynapse-Psychology] {pawn.Name.ToStringShort} reconsidered {applied.Count} relationship(s): {string.Join(", ", applied)}.");
                    });
                },
                options);
            return true;
        }
    }
}
