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
    /// MCP tool handler implementations for psychology profile retrieval
    /// and social interaction history queries.
    /// </summary>
    public static partial class SynapsePsychologyTools
    {
        private static string GetColonistPsychologyProfileHandler(string argumentsJson)
        {
            try
            {
                var args = JsonConvert.DeserializeAnonymousType(argumentsJson, new { pawnName = "" });
                string pawnName = args?.pawnName;
                if (string.IsNullOrEmpty(pawnName))
                {
                    return "{\"error\": \"Missing required argument 'pawnName'.\"}";
                }

                Pawn pawn = FindPawnByName(pawnName);
                if (pawn == null)
                {
                    return $"{{\"error\": \"Pawn with name '{pawnName}' not found on any map or world pawns.\"}}";
                }

                var coreComp = pawn.TryGetComp<SynapseCorePawnComp>();
                var psychComp = pawn.TryGetComp<SynapsePawnComp>();

                var response = new Dictionary<string, object>();
                response["pawnId"] = pawn.ThingID;
                response["name"] = pawn.LabelShort;
                response["gender"] = pawn.gender.ToString();
                response["age"] = pawn.ageTracker?.AgeBiologicalYears ?? 0;
                response["faction"] = pawn.Faction?.Name ?? "None";
                response["backstoryChildhood"] = pawn.story?.Childhood?.title ?? "Unknown";
                response["backstoryAdulthood"] = pawn.story?.Adulthood?.title ?? "None";
                if (ModsConfig.IdeologyActive && pawn.Ideo != null)
                {
                    response["ideology"] = pawn.Ideo.name;
                }

                if (coreComp != null)
                {
                    response["personalitySummary"] = coreComp.personalitySummary ?? "No summary generated yet.";
                    response["dynamicBackstory"] = coreComp.dynamicBackstory ?? "";
                    response["clinicalAssessment"] = coreComp.clinicalAssessment ?? "";
                    response["hometown"] = coreComp.hometown ?? "";
                    response["llmTraits"] = coreComp.llmTraits ?? new List<string>();
                    response["moodBurdens"] = coreComp.GetTopMemoryBurdens(5);
                    
                    var recentMemories = new List<object>();
                    foreach (var mem in coreComp.memories.OrderByDescending(m => m.absTick).Take(10))
                    {
                        long memGameTick = mem.absTick - RimSynapse.Utils.SynapseDateHelper.GetAdjustmentTick();
                        recentMemories.Add(new {
                            summary = mem.summary,
                            type = mem.memoryType,
                            weight = mem.weight,
                            tags = mem.tags,
                            ageHours = (Find.TickManager.TicksGame - memGameTick) / 2500f
                        });
                    }
                    response["recentMemories"] = recentMemories;
                }

                if (psychComp != null)
                {
                    response["breakCategory"] = psychComp.breakCategory.ToString();
                    response["predictedBreakState"] = psychComp.predictedBreakState?.defName ?? "None";
                    response["currentBreakWarning"] = psychComp.currentBreakWarning ?? "None";
                    response["isEuphoric"] = psychComp.isEuphoric;
                    response["ideologyZealot"] = psychComp.ideologyZealot;
                    
                    if (psychComp.dynamicTraits != null)
                    {
                        response["dynamicTraits"] = psychComp.dynamicTraits.Select(t => new {
                            defName = t.traitDef?.defName,
                            label = t.traitDef?.LabelCap.ToString() ?? t.traitDef?.label,
                            reason = t.reason,
                            tickAdded = t.tickAdded,
                            active = t.IsActive,
                            tickRemoved = t.tickRemoved,
                            removalReason = t.removalReason
                        }).ToList();
                    }

                    var socialNet = new List<object>();
                    if (psychComp.socialNetwork != null)
                    {
                        foreach (var kvp in psychComp.socialNetwork)
                        {
                            string targetPawnId = kvp.Key;
                            SocialRecord record = kvp.Value;
                            Pawn targetPawn = FindPawnById(targetPawnId);
                            if (targetPawn != null)
                            {
                                int opinion = pawn.relations?.OpinionOf(targetPawn) ?? 0;
                                string relationLabel = GetRelationshipLabel(pawn, targetPawn);
                                float? opinionIntegral = SynapsePsychology.GetOpinionIntegral(pawn, targetPawn);
                                float? trajectory = SynapsePsychology.GetRelationshipTrajectory(pawn, targetPawn);

                                socialNet.Add(new {
                                    targetName = targetPawn.LabelShort,
                                    relationship = relationLabel,
                                    opinion = opinion,
                                    trust = record.trust,
                                    familiarity = record.familiarity,
                                    opinionIntegral = opinionIntegral,
                                    trajectory = trajectory,
                                    memories = record.relationshipMemories
                                });
                            }
                        }
                    }
                    response["socialNetwork"] = socialNet;

                    if (psychComp.therapyTranscripts != null)
                    {
                        response["therapyTranscripts"] = psychComp.therapyTranscripts.Select(t => new {
                            otherPawn = t.otherPawnName,
                            sessionTick = t.sessionTick,
                            lines = t.lines
                        }).ToList();
                    }
                }

                return JsonConvert.SerializeObject(response, Formatting.Indented);
            }
            catch (Exception ex)
            {
                return $"{{\"error\": \"Failed to build psychology profile: {ex.Message}\"}}";
            }
        }

        private static string GetRecentSocialInteractionsHandler(string argumentsJson)
        {
            try
            {
                var args = JsonConvert.DeserializeAnonymousType(argumentsJson, new { pawnName = "" });
                string pawnName = args?.pawnName;

                var playLog = Find.PlayLog;
                var results = new List<object>();

                if (playLog != null && playLog.AllEntries != null)
                {
                    int currentTick = Find.TickManager.TicksGame;
                    
                    var initiatorField = typeof(PlayLogEntry_Interaction).GetField("initiator", BindingFlags.NonPublic | BindingFlags.Instance);
                    var recipientField = typeof(PlayLogEntry_Interaction).GetField("recipient", BindingFlags.NonPublic | BindingFlags.Instance);
                    var interactionField = typeof(PlayLogEntry_Interaction).GetField("intDef", BindingFlags.NonPublic | BindingFlags.Instance);

                    foreach (var entry in playLog.AllEntries.AsEnumerable().Reverse())
                    {
                        if (entry is PlayLogEntry_Interaction interactionEntry)
                        {
                            var initiator = initiatorField?.GetValue(interactionEntry) as Pawn;
                            var recipient = recipientField?.GetValue(interactionEntry) as Pawn;
                            var interactionDef = interactionField?.GetValue(interactionEntry) as InteractionDef;

                            string gameString = interactionEntry.ToGameStringFromPOV(null, false);

                            if (!string.IsNullOrEmpty(pawnName))
                            {
                                bool matchesInitiator = initiator != null && initiator.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase);
                                bool matchesRecipient = recipient != null && recipient.LabelShort.Equals(pawnName, StringComparison.OrdinalIgnoreCase);
                                bool matchesText = gameString != null && gameString.IndexOf(pawnName, StringComparison.OrdinalIgnoreCase) >= 0;

                                if (!matchesInitiator && !matchesRecipient && !matchesText)
                                {
                                    continue;
                                }
                            }

                            results.Add(new {
                                initiator = initiator?.LabelShort ?? "Unknown",
                                recipient = recipient?.LabelShort ?? "Unknown",
                                interaction = interactionDef?.defName ?? "Interaction",
                                text = gameString,
                                ageHours = (currentTick - interactionEntry.Tick) / 2500f
                            });

                            if (results.Count >= 20) break;
                        }
                    }
                }

                return JsonConvert.SerializeObject(results, Formatting.Indented);
            }
            catch (Exception ex)
            {
                return $"{{\"error\": \"Failed to retrieve recent social interactions: {ex.Message}\"}}";
            }
        }
    }
}
