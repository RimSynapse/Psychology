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
        /// Bumps a memory's weight when the LLM references it, reinforcing it.
        /// </summary>
        public static void BumpMemory(Pawn pawn, string memorySummaryFragment, float bumpAmount = 0.2f)
        {
            var comp = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
            if (comp == null) return;

            var match = comp.memories.FirstOrDefault(m =>
                m.summary.Contains(memorySummaryFragment));

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


