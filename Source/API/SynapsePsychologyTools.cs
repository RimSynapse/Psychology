using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Newtonsoft.Json;
using RimSynapse.Psychology.Comps;
using RimSynapse.Comps;
using RimSynapse.Psychology.Models;
using RimSynapse.Models;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Registers MCP tools for psychology profile queries and social interactions.
    /// Handler implementations are in SynapsePsychologyTools_Handlers.cs (partial class).
    /// </summary>
    public static partial class SynapsePsychologyTools
    {
        public static void RegisterTools()
        {
            // Tool 1: get_colonist_psychology_profile
            SynapseToolRegistry.RegisterTool(
                "get_colonist_psychology_profile",
                "Retrieves traits, sanity breaks predictions, weighted memories, burdens, social network relationship stats (trust/familiarity), and therapy sessions history for a colonist.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        pawnName = new
                        {
                            type = "string",
                            description = "Name of the colonist (e.g. John)"
                        }
                    },
                    required = new[] { "pawnName" }
                },
                GetColonistPsychologyProfileHandler
            );

            // Tool 2: get_recent_social_interactions
            SynapseToolRegistry.RegisterTool(
                "get_recent_social_interactions",
                "Retrieves a chronological list of recent social chatter logs (insults, chit-chat, deep talks) on the map.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        pawnName = new
                        {
                            type = "string",
                            description = "Optional: Filter social interactions involving a specific colonist name"
                        }
                    }
                },
                GetRecentSocialInteractionsHandler
            );

            // Debug tools (Psychology#52): live-LLM memory generation and evaluation.
            SynapseToolRegistry.RegisterTool(
                "debug_generate_memory",
                "DEBUG: generate a memory for a colonist using the live LLM from a described event; the raw model response is logged ([SYNAPSE-DEBUG]) and shown in-game, then the memory is added.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        pawnName = new { type = "string", description = "Colonist to generate a memory for" },
                        @event = new { type = "string", description = "Optional event description to base the memory on" }
                    },
                    required = new[] { "pawnName" }
                },
                SynapsePsychologyDebug.GenerateMemoryHandler, false, null, true);

            SynapseToolRegistry.RegisterTool(
                "debug_run_evaluation",
                "DEBUG: run the daily psychology evaluation for a colonist using the live LLM; the parsed result (clinical assessment + personality trajectory) is logged and shown in-game.",
                new
                {
                    type = "object",
                    properties = new { pawnName = new { type = "string", description = "Colonist to evaluate" } },
                    required = new[] { "pawnName" }
                },
                SynapsePsychologyDebug.RunEvaluationHandler, false, null, true);

            SynapseLogger.Message("[RimSynapse Psychology] Dynamic MCP tools registered with Core.");
        }

        private static Pawn FindPawnByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            foreach (var map in Find.Maps)
            {
                if (map.mapPawns == null) continue;
                var p = map.mapPawns.AllPawns.FirstOrDefault(x => x.LabelShort.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (p != null) return p;
            }

            var worldPawn = Find.WorldPawns?.AllPawnsAlive?.FirstOrDefault(x => x.LabelShort.Equals(name, StringComparison.OrdinalIgnoreCase));
            return worldPawn;
        }

        private static Pawn FindPawnById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            foreach (var map in Find.Maps)
            {
                if (map.mapPawns == null) continue;
                var p = map.mapPawns.AllPawns.FirstOrDefault(x => x.ThingID == id);
                if (p != null) return p;
            }

            var worldPawn = Find.WorldPawns?.AllPawnsAlive?.FirstOrDefault(x => x.ThingID == id);
            return worldPawn;
        }

        private static string GetRelationshipLabel(Pawn pawn, Pawn target)
        {
            if (pawn.relations == null || target == null) return "Acquaintance";
            var rel = pawn.relations.DirectRelations.FirstOrDefault(r => r.otherPawn == target);
            if (rel != null)
            {
                return rel.def.LabelCap.ToString();
            }
            return "Acquaintance";
        }

        public static bool HandleCustomBreak(Pawn pawn, string action, string targetPawnName, int? targetX, int? targetZ)
        {
            if (pawn == null) return false;

            if (action.Equals("TrashClean", StringComparison.OrdinalIgnoreCase))
            {
                string stateDefName = "Synapse_SuicidalAntagonize";
                if (targetX.HasValue && targetZ.HasValue)
                {
                    string[] options = { "Synapse_SuicidalAntagonize", "Synapse_SuicidalBurn", "Synapse_SuicidalStarve" };
                    stateDefName = options[Rand.Range(0, options.Length)];
                }

                var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail(stateDefName);
                if (stateDef != null)
                {
                    pawn.mindState?.mentalStateHandler?.TryStartMentalState(stateDef, "AI-Driven Suicidal Break");
                    return true;
                }
            }
            else if (action.Equals("OverWrite", StringComparison.OrdinalIgnoreCase))
            {
                var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("Synapse_MentalState_Homicidal");
                if (stateDef != null)
                {
                    pawn.mindState?.mentalStateHandler?.TryStartMentalState(stateDef, "AI-Driven Homicidal Break");
                    return true;
                }
            }
            else if (action.Equals("Depart", StringComparison.OrdinalIgnoreCase))
            {
                var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("Synapse_MentalState_AbandonColony");
                if (stateDef != null)
                {
                    pawn.mindState?.mentalStateHandler?.TryStartMentalState(stateDef, "AI-Driven Departure Break");
                    return true;
                }
            }
            else if (action.Equals("TraumaSnap", StringComparison.OrdinalIgnoreCase))
            {
                var stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("Synapse_TraumaTrigger");
                if (stateDef != null)
                {
                    pawn.mindState?.mentalStateHandler?.TryStartMentalState(stateDef, "AI-Driven PTSD Trauma Break");
                    return true;
                }
            }

            return false;
        }
    }
}
