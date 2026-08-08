using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Stage 2 guardrails and inertia for AI-driven personality change (design §5.6, §6):
    ///   • trait resistance — existing traits act as inertia and protect themselves from removal;
    ///   • a whitelist of traits the AI may add/remove (so a hallucinated response can't add Psychopath);
    ///   • the consistency gate — an "unlikely/none" overall likelihood floors today's evidence to 0.
    /// Keyed by trait defName (case-insensitive), with a few narrative aliases that are harmless if absent.
    /// </summary>
    public static class SynapseTraitPolicy
    {
        public struct Resistance
        {
            public float factor;       // 0..1 — how strongly the trait resists change (raises the bar)
            public bool protectedTrait; // true — shielded from removal unless overwhelming, sustained pressure
        }

        // Steadfast / hard-to-shift traits. "Nerves" (degree "nerves of steel") is vanilla; IronWilled/
        // Steadfast are narrative aliases that only match if a mod supplies them.
        private static readonly Dictionary<string, Resistance> ResistanceMap =
            new Dictionary<string, Resistance>(StringComparer.OrdinalIgnoreCase)
            {
                { "Nerves",      new Resistance { factor = 0.8f, protectedTrait = true } },
                { "IronWilled",  new Resistance { factor = 0.8f, protectedTrait = true } },
                { "Steadfast",   new Resistance { factor = 0.7f, protectedTrait = true } },
                { "Psychopath",  new Resistance { factor = 0.6f, protectedTrait = true } },
                { "TooSmart",    new Resistance { factor = 0.3f, protectedTrait = false } },
                { "Sanguine",    new Resistance { factor = 0.3f, protectedTrait = false } },
                { "Ascetic",     new Resistance { factor = 0.2f, protectedTrait = false } },
            };

        // Traits the AI is permitted to add or remove. Deliberately excludes dangerous/identity traits
        // (Psychopath, Cannibal, genes, etc.) — those can never be granted by an evaluation.
        private static readonly HashSet<string> Whitelist =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Bloodlust", "Kind", "Abrasive", "Greedy", "Jealous", "Ascetic",
                "Gourmand", "Nerves", "TooSmart", "Sanguine", "Optimist", "Pessimist",
            };

        public static float ResistanceFactor(string traitDefName)
            => traitDefName != null && ResistanceMap.TryGetValue(traitDefName, out var r) ? r.factor : 0f;

        public static bool IsProtected(string traitDefName)
            => traitDefName != null && ResistanceMap.TryGetValue(traitDefName, out var r) && r.protectedTrait;

        public static bool IsWhitelisted(string traitDefName)
            => traitDefName != null && Whitelist.Contains(traitDefName);

        /// <summary>
        /// The consistency gate (design §5.7 step 4): if the model's own overall likelihood is low/none,
        /// today's evidence is floored to ~0 so an "unlikely" narrative can never flip a trait same-day.
        /// </summary>
        public static float GateDailyPressure(string likelihood, float dailyPressure)
        {
            if (string.IsNullOrEmpty(likelihood)) return dailyPressure;
            string l = likelihood.Trim().ToLowerInvariant();
            if (l == "none" || l == "low") return 0f;
            return dailyPressure;
        }

        /// <summary>
        /// Human-readable resistance dimension for the eval prompt: the pawn's current traits, each
        /// annotated with its resistance and whether it is protected from removal.
        /// </summary>
        public static string DescribeResistanceForPrompt(Pawn pawn)
        {
            var traits = pawn?.story?.traits?.allTraits;
            if (traits == null || traits.Count == 0) return "None";
            var parts = new List<string>();
            foreach (var t in traits)
            {
                string def = t.def?.defName;
                float f = ResistanceFactor(def);
                if (f > 0f)
                {
                    string prot = IsProtected(def) ? ", PROTECTED (do not remove without overwhelming, sustained evidence)" : "";
                    parts.Add($"{t.Label} (resistance {f:0.0}{prot})");
                }
                else
                {
                    parts.Add($"{t.Label} (resistance 0.0)");
                }
            }
            return string.Join(", ", parts);
        }
    }
}
