using System;
using System.Linq;
using System.Collections.Generic;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;
using RimSynapse.Models;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Memory management: Adding, bumping, and serializing pawn memories.
    /// </summary>
    public static partial class SynapsePsychology
    {
        /// <summary>
        /// Serializes a pawn's memories to JSON for use by other systems.
        /// </summary>
        public static string GenerateContextSummary(Pawn pawn, List<RimSynapse.Models.WeightedMemory> customMemories = null)
        {
            var comp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (comp == null)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Cannot add memory — SynapseCorePawnComp not found on {pawn.Name}.");
                return "";
            }

            var memoriesToProcess = customMemories ?? comp.memories;
            return JsonConvert.SerializeObject(memoriesToProcess);
        }

        /// <summary>
        /// Adds a weighted memory to a pawn's long-term memory bank.
        /// </summary>
        public static void AddMemory(Pawn pawn, WeightedMemory memory)
        {
            var comp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (comp == null)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Cannot add memory — SynapseCorePawnComp not found on {pawn.Name}.");
                return;
            }

            comp.AddMemory(memory); // routes through Core: normalises weight to 0-1, assigns memId, indexes
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Memory added to {pawn.Name}: \"{memory.summary}\" (type: {memory.memoryType}, weight: {memory.weight})");
        }

        /// <summary>
        /// Plant a near-permanent "defining memory" on a pawn — the kind they would never lose
        /// (Psychology #22). Writes a <c>BackstoryArrival</c> memory: long-term (pruning-exempt, and it
        /// persists across save/load via ExposeData), a nominal ~0.001/day decay, and the given tags plus
        /// a <c>DefiningMemory</c> marker. Those tags become resonance keys the LLM callback path can
        /// strengthen later through <see cref="BumpMemory"/>. Null-safe: a null pawn or missing
        /// <c>SynapseCorePawnComp</c> logs a warning and no-ops. Callable cross-mod (Storyteller/Factions)
        /// with no hard Psychology reference, via the Core-mediated surface.
        /// </summary>
        public static void PlantDefiningMemory(Pawn pawn, string text, IEnumerable<string> tags = null)
        {
            if (pawn == null)
            {
                RimSynapse.SynapseLogger.Warn("psychology", "[RimSynapse-Psychology] PlantDefiningMemory: null pawn — ignored.");
                return;
            }
            var comp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (comp == null)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] PlantDefiningMemory: {pawn.Name} has no SynapseCorePawnComp — ignored.");
                return;
            }

            PlantDefiningMemoryOn(comp, text, tags);
            RimSynapse.SynapseLogger.Info("psychology",
                $"[RimSynapse-Psychology] Planted defining memory on {pawn.Name}: \"{text}\".");
        }

        /// <summary>Comp-level core of <see cref="PlantDefiningMemory"/> (also the test seam). No-ops on a null comp.</summary>
        public static void PlantDefiningMemoryOn(RimSynapse.Comps.SynapseCorePawnComp comp, string text, IEnumerable<string> tags = null)
        {
            if (comp == null) return;

            var tagList = new List<string> { "DefiningMemory" };
            if (tags != null) tagList.AddRange(tags.Where(t => !string.IsNullOrEmpty(t)));

            comp.AddMemory(new WeightedMemory
            {
                summary = text,
                memoryType = "BackstoryArrival",
                weight = 0.7f,
                baseWeight = 0.7f,
                decayRate = 0.001f,     // documentary: long-term memories are never decayed
                isLongTerm = true,
                tags = tagList,
                absTick = Find.TickManager != null ? Find.TickManager.TicksAbs : 0L,
            }); // AddMemory -> NormalizeMemory re-affirms isLongTerm from the bornLongTerm class
        }

        /// <summary>
        /// Bumps a memory's weight when the LLM references it, reinforcing it. Matches by a summary
        /// fragment OR an exact tag (#22 resonance), so a defining memory strengthens when its planted
        /// tag/theme is referenced again.
        /// </summary>
        public static void BumpMemory(Pawn pawn, string memorySummaryFragment, float bumpAmount = 0.2f)
        {
            var comp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            BumpMemoryOn(comp, memorySummaryFragment, bumpAmount);
        }

        /// <summary>Comp-level core of <see cref="BumpMemory"/> (also the test seam). Matches by summary fragment or exact tag.</summary>
        public static void BumpMemoryOn(RimSynapse.Comps.SynapseCorePawnComp comp, string memorySummaryFragment, float bumpAmount = 0.2f)
        {
            if (comp == null || string.IsNullOrEmpty(memorySummaryFragment)) return;

            var match = comp.memories.FirstOrDefault(m =>
                (m.summary != null && m.summary.Contains(memorySummaryFragment)) ||
                (m.tags != null && m.tags.Contains(memorySummaryFragment)));

            if (match != null)
            {
                // Under the normalised 0-1 scale the 1.0 ceiling is correct — reinforcement raises,
                // never lowers, a strong memory (the old crush was purely the scale mismatch).
                match.weight = Math.Min(1.0f, match.weight + bumpAmount);
                match.lastReferencedTick = Find.TickManager?.TicksAbs ?? match.lastReferencedTick;
                match.timesReferenced++;
            }
        }
    }
}


