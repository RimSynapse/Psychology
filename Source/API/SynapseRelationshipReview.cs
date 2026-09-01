using System;
using System.Collections.Generic;
using Verse;
using Newtonsoft.Json;
using RimSynapse.Psychology.Comps;
using RimSynapse.Utils;

namespace RimSynapse.Psychology.API
{
    /// <summary>One proposed relationship change from the nightly relationship eval, in the pawn's own voice.</summary>
    public class RelationshipDelta
    {
        public string who;          // the other colonist (short name); resolved to a pawn at apply time
        public float warmthDelta;   // how their liking shifts (bounded per night)
        public float trustDelta;    // how their reliance shifts (bounded per night)
        public string reason;       // one in-character line — becomes the relationship memory
        public string kind;         // bonding | friction | jealousy | rivalry | betrayal | reconciliation | ...
    }

    /// <summary>The whole result of one colonist's nightly relationship review (#72 Phase 4).</summary>
    public class RelationshipReviewResult
    {
        public float? compulsionControl;                        // the LLM's refined read of this pawn's emotional brake
        public List<RelationshipDelta> relationships = new List<RelationshipDelta>();
    }

    /// <summary>
    /// Applies a nightly relationship review (#72 Phase 4): the LLM DETERMINES how a colonist now feels about the
    /// others (feeding off their just-finished personality eval + the compass + compatibility), and this C# layer
    /// APPLIES it — directed, bounded, and with the SERIOUS escalations gated. Rivalry and betrayal are proposed
    /// by the model but only honoured when the relationship's state earns them; otherwise they degrade to ordinary
    /// friction, so nobody forms a rivalry over something inconsequential.
    /// </summary>
    public static class SynapseRelationshipReview
    {
        /// <summary>No single night may swing a bond by more than this on either axis.</summary>
        public const float MaxNightlyDelta = 12f;

        // Betrayal gate: you can only betray someone who trusted you, and it must be a real breach this night.
        public const float BetrayalPriorTrust = 25f;
        public const float BetrayalBreachDelta = -15f;
        public static bool QualifiesAsBetrayal(float priorTrust, float trustDelta)
            => priorTrust >= BetrayalPriorTrust && trustDelta <= BetrayalBreachDelta;

        // Rivalry gate: sustained, deep antagonism between people who know each other well — never a one-off.
        public const float RivalryWarmth = -50f;
        public const float RivalryFamiliarity = 60f;
        public static bool QualifiesAsRivalry(float warmthAfter, float familiarity, float trust)
            => warmthAfter <= RivalryWarmth && familiarity >= RivalryFamiliarity && trust <= 0f;

        /// <summary>
        /// Apply a parsed review to <paramref name="pawn"/>. <paramref name="resolve"/> maps a delta's "who" to a
        /// pawn (by short name, in production). Returns the RESOLVED kind for each applied relationship (rivalry/
        /// betrayal downgraded to "friction" when the gate isn't met) — for the debug dump and tests.
        /// </summary>
        public static List<string> Apply(Pawn pawn, RelationshipReviewResult result, Func<string, Pawn> resolve)
        {
            var applied = new List<string>();
            var comp = pawn?.GetComp<SynapsePawnComp>();
            if (comp == null || result == null) return applied;

            // The LLM refines this pawn's emotional brake (the C# triggers only ever READ it).
            if (result.compulsionControl.HasValue)
                comp.compulsionControl = Clamp01(result.compulsionControl.Value);

            if (result.relationships == null) return applied;
            foreach (var d in result.relationships)
            {
                if (d == null || string.IsNullOrEmpty(d.who)) continue;
                var other = resolve?.Invoke(d.who);
                if (other == null || other == pawn) continue;
                var rec = SynapseRelationships.TryDirected(pawn, other);
                if (rec == null) continue;

                float priorTrust = rec.trust;
                float w = Clamp(d.warmthDelta, -MaxNightlyDelta, MaxNightlyDelta);
                float t = Clamp(d.trustDelta, -MaxNightlyDelta, MaxNightlyDelta);
                rec.AddWarmth(w);
                rec.AddTrust(t);

                // Gate the serious escalations against the relationship's actual state.
                string kind = (d.kind ?? "friction").ToLowerInvariant();
                if (kind == "betrayal" && !QualifiesAsBetrayal(priorTrust, t)) kind = "friction";
                if (kind == "rivalry" && !QualifiesAsRivalry(rec.warmth, rec.familiarity, rec.trust)) kind = "friction";

                if (!string.IsNullOrEmpty(d.reason) && rec.relationshipMemories.Count < 12)
                    rec.relationshipMemories.Add(d.reason);
                applied.Add(kind);
            }
            return applied;
        }

        /// <summary>Parse the eval's JSON into a result, or null if it can't be read. Tolerant of markdown fences.</summary>
        public static RelationshipReviewResult Parse(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            try
            {
                string json = JsonHelper.ExtractJson(content);
                if (json == null) return null;
                return JsonConvert.DeserializeObject<RelationshipReviewResult>(json);
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Could not parse relationship review: {ex.Message}");
                return null;
            }
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
