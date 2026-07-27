using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Comps;
using RimSynapse.Models;
using RimSynapse.Psychology;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.Patches
{
    [HarmonyPatch(typeof(MarriageCeremonyUtility), "Married")]
    public static class Patch_MarriageCeremonyUtility_Married
    {
        public static void Postfix(Pawn firstPawn, Pawn secondPawn)
        {
            if (firstPawn == null || secondPawn == null) return;

            var comp1 = firstPawn.GetComp<SynapsePawnComp>();
            if (comp1 != null && comp1.socialNetwork.TryGetValue(secondPawn.GetUniqueLoadID(), out var record1))
            {
                if (record1.relationshipMemories.Count > 0)
                {
                    string mem1 = record1.relationshipMemories.RandomElement();
                    Find.LetterStack.ReceiveLetter($"{firstPawn.Name.ToStringShort}'s Vows", $"During the ceremony, {firstPawn.Name.ToStringShort} spoke from the heart:\n\n\"{mem1}\"", LetterDefOf.PositiveEvent, firstPawn);
                }
            }
            
            var comp2 = secondPawn.GetComp<SynapsePawnComp>();
            if (comp2 != null && comp2.socialNetwork.TryGetValue(firstPawn.GetUniqueLoadID(), out var record2))
            {
                if (record2.relationshipMemories.Count > 0)
                {
                    string mem2 = record2.relationshipMemories.RandomElement();
                    Find.LetterStack.ReceiveLetter($"{secondPawn.Name.ToStringShort}'s Vows", $"During the ceremony, {secondPawn.Name.ToStringShort} spoke from the heart:\n\n\"{mem2}\"", LetterDefOf.PositiveEvent, secondPawn);
                }
            }
        }
    }

    [HarmonyPatch(typeof(LordJob_Ritual), "LordJobTick")]
    public static class Patch_LordJob_Ritual_Tick
    {
        public static void Postfix(LordJob_Ritual __instance)
        {
            int ticksPassed = Traverse.Create(__instance).Field("ticksPassed").GetValue<int>();
            if (ticksPassed == 1)
            {
                if (__instance.Ritual != null && __instance.Ritual.def.defName.Contains("Funeral"))
                {
                    Patch_Funeral_Apply.CaptureStartState(__instance);
                }
            }
        }
    }

    public static class Patch_Funeral_Apply
    {
        public class FuneralStartState
        {
            public string weather;
            public string timeOfDay;
            public float averageMood;
            public string moodReason;
            public float averageOpinion;
            public List<Pawn> attendees = new List<Pawn>();
            public Dictionary<string, string> preGeneratedEulogies = new Dictionary<string, string>();
        }

        private static Dictionary<LordJob_Ritual, FuneralStartState> s_StartStates = new Dictionary<LordJob_Ritual, FuneralStartState>();

        private static bool IsConversationsActive()
        {
            return AccessTools.TypeByName("RimSynapse.Conversations.Patches.Patch_Pawn_InteractionsTracker_TryInteractWith") != null;
        }

        private static Pawn ResolveTargetPawn(LordJob_Ritual jobRitual)
        {
            if (jobRitual == null) return null;

            if (jobRitual.selectedTarget.HasThing)
            {
                var thing = jobRitual.selectedTarget.Thing;
                if (thing is Pawn pawn) return pawn;
                if (thing is Corpse corpse) return corpse.InnerPawn;
                if (thing is Building_Grave grave) return grave.Corpse?.InnerPawn;
            }

            if (jobRitual.assignments != null)
            {
                var target = jobRitual.assignments.FirstAssignedPawn("target") ??
                             jobRitual.assignments.FirstAssignedPawn("recipient") ??
                             jobRitual.assignments.FirstAssignedPawn("organizer") ??
                             jobRitual.assignments.FirstAssignedPawn("speaker") ??
                             jobRitual.assignments.FirstAssignedPawn("leader") ??
                             jobRitual.assignments.FirstAssignedPawn("moralist");
                if (target != null) return target;
            }

            try 
            {
                var obligation = Traverse.Create(jobRitual).Field("obligation").GetValue();
                if (obligation != null) 
                {
                    var targetInfo = Traverse.Create(obligation).Field("targetA").GetValue();
                    if (targetInfo != null)
                    {
                        var targetThing = Traverse.Create(targetInfo).Property("Thing").GetValue<Thing>();
                        if (targetThing is Pawn p) return p;
                        if (targetThing is Corpse corpse) return corpse.InnerPawn;
                    }
                }
            } 
            catch {}

            return null;
        }

        public static void CaptureStartState(LordJob_Ritual jobRitual)
        {
            if (jobRitual == null || jobRitual.Map == null || jobRitual.lord == null) return;
            
            string eventName = "Ceremony";
            if (jobRitual.Ritual != null)
            {
                eventName = jobRitual.Ritual.def.label ?? jobRitual.Ritual.def.defName;
            }
            else if (!jobRitual.RitualLabel.NullOrEmpty())
            {
                eventName = jobRitual.RitualLabel;
            }

            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] CaptureStartState triggered for event {eventName}: {jobRitual.RitualLabel}");
            
            var attendees = jobRitual.lord.ownedPawns.Where(p => p.RaceProps.Humanlike && !p.Dead && p.Spawned).ToList();
            if (attendees.Count == 0) return;

            Pawn targetPawn = ResolveTargetPawn(jobRitual);
            if (targetPawn == null)
            {
                RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] CaptureStartState: Could not identify target pawn for {eventName}.");
                return;
            }

            var state = new FuneralStartState
            {
                weather = jobRitual.Map.weatherManager.curWeather.label,
                timeOfDay = GenLocalDate.HourOfDay(jobRitual.Map) < 12 ? "morning" : (GenLocalDate.HourOfDay(jobRitual.Map) < 17 ? "afternoon" : "evening"),
                averageMood = (float)attendees.Average(p => p.needs?.mood?.CurLevel ?? 0.5f),
                averageOpinion = (float)attendees.Average(p => p.relations?.OpinionOf(targetPawn) ?? 0),
                attendees = attendees
            };

            var reasons = new List<string>();
            if (attendees.Any(p => p.needs?.food?.Starving == true)) reasons.Add("starving");
            if (attendees.Any(p => p.needs?.rest?.CurCategory == RestCategory.Exhausted)) reasons.Add("exhausted");
            state.moodReason = reasons.Count > 0 ? string.Join(" and ", reasons) : (eventName.ToLower().Contains("funeral") ? "grief" : "joy");

            s_StartStates[jobRitual] = state;
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] CaptureStartState complete for target: {targetPawn.Name.ToStringShort}. Attendees: {attendees.Count}");

            if (IsConversationsActive())
            {
                PreGenerateEulogies(targetPawn, attendees, state, eventName);
            }
        }

        public class PreGenEulogyResponse
        {
            public Dictionary<string, string> eulogies;
        }

        private static void PreGenerateEulogies(Pawn targetPawn, List<Pawn> attendees, FuneralStartState state, string eventName)
        {
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Pre-generating speeches/remarks for {attendees.Count} attendees for event {eventName}...");
            
            string statementName = eventName.ToLower().Contains("funeral") ? "eulogy statement" : "reaction/congratulatory statement";
            string systemPrompt = $"You are role-playing as the minds of a group of RimWorld pawns preparing to attend the ceremony: {eventName}.\n" +
                                   $"For each pawn, write a short, personal, one-sentence {statementName} they would say if they step forward to speak about the event and {targetPawn.Name.ToStringShort}.\n" +
                                   $"The statement must be in the first-person (\"I remember...\", \"{targetPawn.Name.ToStringShort} is...\").\n" +
                                   $"Make it fit their opinion and relationship.\n\n" +
                                   $"Response must be strictly in JSON format:\n" +
                                   $"{{\n" +
                                   $"  \"eulogies\": {{\n" +
                                   $"    \"PawnID_1\": \"Statement text...\",\n" +
                                   $"    \"PawnID_2\": \"Statement text...\"\n" +
                                   $"  }}\n" +
                                   $"}}";

            string attendeesList = "";
            foreach (var p in attendees)
            {
                string rel = p.relations?.OpinionOf(targetPawn).ToString() ?? "0";
                string traits = string.Join(", ", p.story?.traits?.allTraits?.Select(t => t.Label) ?? Enumerable.Empty<string>());
                string relationLabel = "Guest";
                if (p.relations != null && p.relations.DirectRelations != null)
                {
                    var relInfo = p.relations.DirectRelations.FirstOrDefault(r => r.otherPawn == targetPawn);
                    if (relInfo != null && relInfo.def != null)
                    {
                        relationLabel = relInfo.def.label;
                    }
                }
                attendeesList += $"- {p.Name.ToStringShort} (ID: {p.ThingID}): Traits: {traits}, Opinion: {rel}, Relation: {relationLabel}\n";
            }

            string userMessage = $"Target: {targetPawn.Name.ToStringShort} (ID: {targetPawn.ThingID})\n" +
                                 $"Attendees:\n{attendeesList}";

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result =>
                {
                    if (result.success && !string.IsNullOrEmpty(result.content))
                    {
                        try
                        {
                            string json = RimSynapse.Utils.JsonHelper.ExtractJson(result.content);
                            if (json != null)
                            {
                                var response = JsonConvert.DeserializeObject<PreGenEulogyResponse>(json);
                                if (response != null && response.eulogies != null)
                                {
                                    SynapseGameComponent.Enqueue(() =>
                                    {
                                        // Runs later on the main thread, so the catch below does not
                                        // cover it; without this, failures surface as a bare
                                        // "Callback error" from ProcessMainThreadQueue.
                                        try
                                        {
                                            if (state?.preGeneratedEulogies == null) return;

                                            foreach (var kvp in response.eulogies)
                                            {
                                                state.preGeneratedEulogies[kvp.Key] = kvp.Value;
                                            }
                                            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Successfully pre-generated {state.preGeneratedEulogies.Count} speeches.");
                                        }
                                        catch (Exception ex)
                                        {
                                            RimSynapse.SynapseLogger.Error("psychology", $"[RimSynapse-Psychology] Failed to store pre-generated speeches: {ex}");
                                        }
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("psychology", $"Failed to parse pre-generated speeches: {ex.Message}");
                        }
                    }
                },
                new ChatOptions { priority = 4, requestName = "Pre-Generate Ceremony Speeches" }
            );
        }

        public static void Postfix(LordJob_Ritual jobRitual, Dictionary<Pawn, int> totalPresence)
        {
            if (jobRitual == null || jobRitual.assignments == null) return;
            
            string eventName = "Ceremony";
            if (jobRitual.Ritual != null)
            {
                eventName = jobRitual.Ritual.def.label ?? jobRitual.Ritual.def.defName;
            }
            else if (!jobRitual.RitualLabel.NullOrEmpty())
            {
                eventName = jobRitual.RitualLabel;
            }

            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Postfix triggered for LordJob_Ritual event {eventName}: {jobRitual.RitualLabel}");
            
            var speaker = jobRitual.assignments.FirstAssignedPawn("speaker") ?? jobRitual.assignments.FirstAssignedPawn("organizer");
            
            Pawn targetPawn = ResolveTargetPawn(jobRitual);
            if (targetPawn == null && speaker != null && speaker.Map != null)
            {
                // Fallback for funerals: search nearby dead pawns
                foreach (var p in speaker.Map.mapPawns.AllPawns)
                {
                    if (p.Dead)
                    {
                        var corpse = p.Corpse;
                        if (corpse != null)
                        {
                            if (corpse.Spawned && corpse.Position.DistanceTo(speaker.Position) < 25f)
                            {
                                targetPawn = p;
                                break;
                            }
                            else if (corpse.ParentHolder is Building_Grave grave && grave.Spawned && grave.Position.DistanceTo(speaker.Position) < 25f)
                            {
                                targetPawn = p;
                                break;
                            }
                        }
                    }
                }
            }

            if (targetPawn == null)
            {
                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Postfix exited early: target pawn not found for event {eventName}.");
                return;
            }

            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Postfix processing target: {targetPawn.Name.ToStringShort} for event {eventName}");

            FuneralStartState startState = null;
            if (s_StartStates.TryGetValue(jobRitual, out var captured))
            {
                startState = captured;
                s_StartStates.Remove(jobRitual);
            }

            var attendees = new HashSet<Pawn>();
            if (totalPresence != null)
            {
                foreach (var p in totalPresence.Keys)
                {
                    if (p != null && p.RaceProps.Humanlike && !p.Dead && p.Spawned) attendees.Add(p);
                }
            }
            if (startState != null)
            {
                foreach (var p in startState.attendees)
                {
                    if (p != null && p.RaceProps.Humanlike && !p.Dead && p.Spawned) attendees.Add(p);
                }
            }
            if (jobRitual.lord?.ownedPawns != null)
            {
                foreach (var p in jobRitual.lord.ownedPawns)
                {
                    if (p != null && p.RaceProps.Humanlike && !p.Dead && p.Spawned) attendees.Add(p);
                }
            }

            if (attendees.Count == 0) return;

            string outcomeLabel = "boring";
            float memoryWeight = 0.4f;
            float memoryDecay = 0.02f;
            var outcomeEffect = jobRitual.Ritual?.outcomeEffect;
            if (outcomeEffect != null && outcomeEffect.def != null && outcomeEffect.def.outcomeChances != null)
            {
                foreach (var poss in outcomeEffect.def.outcomeChances)
                {
                    if (poss.memory != null)
                    {
                        foreach (var attendee in attendees)
                        {
                            if (attendee.needs?.mood?.thoughts?.memories?.NumMemoriesOfDef(poss.memory) > 0)
                            {
                                outcomeLabel = poss.label;
                                if (outcomeLabel == "terrible") memoryWeight = 0.6f;
                                else if (outcomeLabel == "boring") memoryWeight = 0.2f;
                                else if (outcomeLabel == "good") memoryWeight = 0.4f;
                                else if (outcomeLabel == "heartwarming") memoryWeight = 0.7f;
                                break;
                            }
                        }
                    }
                }
            }

            string memory = null;
            if (speaker != null)
            {
                if (startState != null && startState.preGeneratedEulogies.TryGetValue(speaker.ThingID, out string preGen))
                {
                    memory = preGen;
                }
                else
                {
                    var comp = speaker.GetComp<SynapsePawnComp>();
                    if (comp != null && comp.socialNetwork.TryGetValue(targetPawn.GetUniqueLoadID(), out var record))
                    {
                        if (record.relationshipMemories.Count > 0)
                        {
                            memory = record.relationshipMemories.RandomElement();
                        }
                    }
                }
            }

            if (speaker != null && !string.IsNullOrEmpty(memory))
            {
                string letterTitle = eventName.ToLower().Contains("funeral") ? $"{speaker.Name.ToStringShort}'s Eulogy" : $"{speaker.Name.ToStringShort}'s Speech";
                string letterDesc = eventName.ToLower().Contains("funeral")
                    ? $"During the funeral, {speaker.Name.ToStringShort} shared a personal memory of {targetPawn.Name.ToStringShort}:\n\n\"{memory}\""
                    : $"During the ceremony, {speaker.Name.ToStringShort} spoke about the event:\n\n\"{memory}\"";

                if (outcomeLabel == "terrible" || outcomeLabel == "boring")
                {
                    Find.LetterStack.ReceiveLetter(
                        letterTitle + " (Awkward)",
                        letterDesc + $"\n\nHowever, the ceremony was received poorly ({outcomeLabel}).",
                        LetterDefOf.NegativeEvent,
                        speaker
                    );
                }
                else
                {
                    Find.LetterStack.ReceiveLetter(
                        letterTitle,
                        letterDesc,
                        LetterDefOf.PositiveEvent,
                        speaker
                    );
                }
            }

            GenerateEventRecordAndMemories(targetPawn, outcomeLabel, startState, attendees, memoryWeight, memoryDecay, eventName);
        }

        public class FuneralEulogy
        {
            public string speaker;
            public string text;
        }

        public class FuneralComment
        {
            public string commenter;
            public string text;
        }

        public class FuneralResponse
        {
            public string overallRecord;
            public List<FuneralEulogy> eulogies;
            public List<FuneralComment> comments;
            public Dictionary<string, string> pawnMemories;
        }

        public static void GenerateFuneralRecordAndMemories(Pawn deceased, string outcomeLabel, FuneralStartState startState, HashSet<Pawn> attendees, float memoryWeight, float memoryDecay)
        {
            GenerateEventRecordAndMemories(deceased, outcomeLabel, startState, attendees, memoryWeight, memoryDecay, "Funeral");
        }

        public static void GenerateEventRecordAndMemories(Pawn targetPawn, string outcomeLabel, FuneralStartState startState, HashSet<Pawn> attendees, float memoryWeight, float memoryDecay, string eventName)
        {
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Generating ceremony record and memories for {attendees.Count} attendees. Event: {eventName}, Target: {targetPawn.Name.ToStringShort}, Outcome: {outcomeLabel}");
            
            var placeholderTags = new Dictionary<string, string>();
            foreach (var attendee in attendees)
            {
                var coreComp = attendee.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
                if (coreComp != null)
                {
                    string placeholderId = Guid.NewGuid().ToString("N");
                    string placeholderTag = "ceremony_placeholder_" + placeholderId;
                    placeholderTags[attendee.ThingID] = placeholderTag;

                    var placeholder = new WeightedMemory
                    {
                        summary = $"Attended the {eventName} of {targetPawn.Name.ToStringShort}. The ceremony was {outcomeLabel}.",
                        memoryType = "social",
                        tags = new List<string> { "ceremony", targetPawn.ThingID, placeholderTag },
                        absTick = RimSynapse.Utils.SynapseDateHelper.GameTickToAbsTick(Find.TickManager.TicksGame),
                        gameTick = Find.TickManager.TicksGame,
                        weight = memoryWeight,
                        baseWeight = memoryWeight,
                        decayRate = memoryDecay
                    };
                    coreComp.memories.Add(placeholder);
                }
            }

            string weather = startState?.weather ?? "clear";
            string timeOfDay = startState?.timeOfDay ?? "afternoon";
            string moodReason = startState?.moodReason ?? "grief";
            float averageMood = startState?.averageMood ?? 0.5f;
            float averageOpinion = startState?.averageOpinion ?? 0f;

            string systemPrompt = $"You are role-playing as the narrator and subconscious minds of a group of RimWorld pawns attending a ceremony: {eventName}.\n" +
                                   $"The focus/target of this ceremony was {targetPawn.Name.ToStringShort}.\n" +
                                   $"The ceremony occurred during a {weather} {timeOfDay}. The overall mood of attendees was {moodReason} (average: {averageMood:F2}), and their average opinion of {targetPawn.Name.ToStringShort} was {averageOpinion:F1}.\n" +
                                   $"The overall outcome of the ceremony was: {outcomeLabel}.\n\n" +
                                   $"You must generate the following details:\n" +
                                   $"1. An overall detailed narrative of the ceremony. Describe the atmosphere, the significance of the event, and how the attendees reacted to the {outcomeLabel} outcome. Keep it engaging, beautiful, and thematic.\n" +
                                   $"2. A list of speeches, remarks, or vows spoken by the attendees. Every attendee (listed below) should deliver a brief spoken reaction or memory (1-2 sentences) about the event and {targetPawn.Name.ToStringShort}.\n" +
                                   $"3. A list of comments and remarks made by attendees during the ceremony (reacting to the speeches or sharing quiet thoughts).\n" +
                                   $"4. A short first-person journal entry/memory for EACH attendee (used as their personal in-game memory).\n" +
                                   $"   - Standard memory: 1-2 sentences.\n" +
                                   $"   - Lover / Spouse / Close Friend / Bitter Rival: 3-4 sentences, expressing deeper feelings.\n" +
                                   $"   - Write in the first person (\"I felt...\", \"Seeing them...\").\n\n" +
                                   $"Response must be strictly in JSON format:\n" +
                                   $"{{\n" +
                                   $"  \"overallRecord\": \"The detailed narrative of the ceremony...\",\n" +
                                   $"  \"eulogies\": [\n" +
                                   $"    {{ \"speaker\": \"PawnName\", \"text\": \"Speech/vow/reaction text...\" }}\n" +
                                   $"  ],\n" +
                                   $"  \"comments\": [\n" +
                                   $"    {{ \"commenter\": \"PawnName\", \"text\": \"Comment text...\" }}\n" +
                                   $"  ],\n" +
                                   $"  \"pawnMemories\": {{\n" +
                                   $"    \"PawnID_1\": \"Memory text...\",\n" +
                                   $"    \"PawnID_2\": \"Memory text...\"\n" +
                                   $"  }}\n" +
                                   $"}}";

            string attendeesList = "";
            foreach (var p in attendees)
            {
                string rel = p.relations?.OpinionOf(targetPawn).ToString() ?? "0";
                string traits = string.Join(", ", p.story?.traits?.allTraits?.Select(t => t.Label) ?? Enumerable.Empty<string>());
                string relationLabel = "Guest";
                
                if (p.relations != null && p.relations.DirectRelations != null)
                {
                    var relInfo = p.relations.DirectRelations.FirstOrDefault(r => r.otherPawn == targetPawn);
                    if (relInfo != null && relInfo.def != null)
                    {
                        relationLabel = relInfo.def.label;
                    }
                }
                
                attendeesList += $"- {p.Name.ToStringShort} (ID: {p.ThingID}): Traits: {traits}, Opinion: {rel}, Relation: {relationLabel}\n";
            }

            string userMessage = $"Target: {targetPawn.Name.ToStringShort} (ID: {targetPawn.ThingID})\n" +
                                 $"Attendees:\n{attendeesList}";

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result =>
                {
                    if (result.success && !string.IsNullOrEmpty(result.content))
                    {
                        try
                        {
                            string json = RimSynapse.Utils.JsonHelper.ExtractJson(result.content);
                            if (json != null)
                            {
                                var response = JsonConvert.DeserializeObject<FuneralResponse>(json);
                                if (response != null)
                                {
                                    SynapseGameComponent.Enqueue(() =>
                                    {
                                      // This body runs later on the main thread, so the catch around
                                      // PromptAsync's callback does not cover it. Without a local catch,
                                      // anything thrown here escapes to ProcessMainThreadQueue and is
                                      // reported as a bare "Callback error" with no context.
                                      try
                                      {
                                         var worldComp = Find.World?.GetComponent<SynapsePsychologyWorldComponent>();
                                         var coreWorldComp = Find.World?.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
                                         // A model can answer without the narrative field — the quicktest
                                         // mock returns a bare {"success": true} — so treat a missing
                                         // record as "nothing to save" rather than dereferencing null.
                                         bool hasRecord = !string.IsNullOrEmpty(response.overallRecord)
                                             || (response.eulogies != null && response.eulogies.Count > 0)
                                             || (response.comments != null && response.comments.Count > 0);

                                         if (worldComp != null && coreWorldComp != null && hasRecord)
                                         {
                                             string finalRecord = response.overallRecord ?? "";

                                             if (response.eulogies != null && response.eulogies.Count > 0)
                                             {
                                                 string title = eventName.ToLower().Contains("funeral") ? "Spoken Eulogies" : "Speeches and Remarks";
                                                 finalRecord += $"\n\n───────────────────────────────────────\n\n{title}:\n";
                                                 foreach (var eulogy in response.eulogies)
                                                 {
                                                     finalRecord += $"\n{eulogy.speaker}:\n  \"{eulogy.text}\"";
                                                 }
                                             }

                                             if (response.comments != null && response.comments.Count > 0)
                                             {
                                                 finalRecord += "\n\n───────────────────────────────────────\n\nComments and Remarks:\n";
                                                 foreach (var comment in response.comments)
                                                 {
                                                     finalRecord += $"\n{comment.commenter}:\n  \"{comment.text}\"";
                                                 }
                                             }

                                             // Save in legacy funeralRecords if it's a funeral
                                             if (eventName.ToLower().Contains("funeral"))
                                             {
                                                 worldComp.funeralRecords[targetPawn.GetUniqueLoadID()] = finalRecord;
                                             }

                                             // Save generic PawnEventRecord
                                             string dateStr = GenDate.DateReadoutStringAt(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(targetPawn.Tile));
                                             var newRecord = new PawnEventRecord(
                                                 Guid.NewGuid().ToString("N"),
                                                 eventName,
                                                 dateStr,
                                                 targetPawn.Name.ToStringShort,
                                                 finalRecord
                                             );
                                             coreWorldComp.pawnEventRecords.Add(newRecord);
                                             RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Successfully saved ceremony record for {targetPawn.Name.ToStringShort}. Length: {finalRecord.Length}");
                                         }
                                         else if (!hasRecord)
                                         {
                                             RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Ceremony response for {targetPawn?.Name?.ToStringShort ?? "unknown"} contained no narrative, eulogies or comments; skipping record.");

                                             // This is exactly the "nothing went as expected" moment
                                             // the agent exists for. A cheap no-op unless the player
                                             // enabled escalation; the warning above always logs.
                                             RimSynapse.SynapseAgentEscalation.Escalate(new RimSynapse.SynapseEscalationContext
                                             {
                                                 Origin = "Psychology.CeremonyRecord",
                                                 Expectation = $"A narrative record with eulogies for the {eventName} of {targetPawn?.Name?.ToStringShort ?? "a colonist"}",
                                                 Observation = "The model's response deserialised, but overallRecord, eulogies and comments were all empty — the ceremony would leave no trace.",
                                                 SuggestedGoal = "Decide whether this ceremony deserves a record; if so, produce a brief one-paragraph narrative and store it, otherwise conclude that skipping is acceptable."
                                             });
                                         }

                                        if (response.pawnMemories != null)
                                        {
                                            int updated = 0;
                                            foreach (var kvp in response.pawnMemories)
                                            {
                                                string pId = kvp.Key;
                                                string explanation = kvp.Value;
                                                Pawn p = attendees.FirstOrDefault(a => a.ThingID == pId);
                                                if (p != null && placeholderTags.TryGetValue(pId, out string pTag))
                                                {
                                                    var targetComp = p.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
                                                    if (targetComp?.memories != null)
                                                    {
                                                        // Memories restored from a save can carry null tags.
                                                        var targetMem = targetComp.memories.FirstOrDefault(m => m?.tags != null && m.tags.Contains(pTag));
                                                        if (targetMem != null)
                                                        {
                                                            targetMem.summary = explanation;
                                                            targetMem.tags.Remove(pTag);
                                                            updated++;
                                                        }
                                                    }
                                                }
                                            }
                                            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Successfully updated {updated} attendee memories.");
                                        }
                                      }
                                      catch (Exception ex)
                                      {
                                          RimSynapse.SynapseLogger.Error("psychology", $"[RimSynapse-Psychology] Failed to apply ceremony record for {targetPawn?.Name?.ToStringShort ?? "unknown"}: {ex}");
                                      }
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            RimSynapse.SynapseLogger.Warn("psychology", $"Failed to process ceremony record and memories: {ex.Message}");
                        }
                    }
                },
                new ChatOptions 
                { 
                    priority = 3, 
                    requestName = "Personalized Ceremony Memory",
                    targetName = targetPawn?.Name?.ToStringShort ?? "Ceremony"
                }
            );
        }
    }

    [HarmonyPatch(typeof(Dialog_NamePlayerSettlement), "Named")]
    public static class Patch_Dialog_NamePlayerSettlement_Named
    {
        public static void Postfix(string s)
        {
            Patch_Dialog_ColonyNaming_Helper.OnColonyNamed(s, null);
        }
    }

    [HarmonyPatch(typeof(Dialog_NamePlayerFaction), "Named")]
    public static class Patch_Dialog_NamePlayerFaction_Named
    {
        public static void Postfix(string s)
        {
            Patch_Dialog_ColonyNaming_Helper.OnColonyNamed(null, s);
        }
    }

    public static class Patch_Dialog_ColonyNaming_Helper
    {
        public static void OnColonyNamed(string settlementName, string factionName)
        {
            try
            {
                var worldComp = Find.World?.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
                if (worldComp == null) return;

                string sName = settlementName ?? Find.CurrentMap?.Parent?.Label ?? "the settlement";
                string fName = factionName ?? Faction.OfPlayer?.Name ?? "the faction";

                string dateStr = RimWorld.GenDate.DateReadoutStringAt(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(Find.CurrentMap?.Tile ?? 0));

                string logText = $"After surviving the initial landing and securing a foothold, the colonists gathered to formally establish their presence.\n\n" +
                                 $"They agreed to call their new settlement '{sName}', and their union will be known across this world as '{fName}'.\n\n" +
                                 $"This declaration marks a permanent commitment to this soil and the beginning of their shared history.";

                // Save the Colony Naming event
                string eventId = "ColonyNaming_" + Guid.NewGuid().ToString();
                var record = new PawnEventRecord(
                    eventId,
                    "Colony Named: " + sName,
                    dateStr,
                    "Colony",
                    logText
                );

                worldComp.pawnEventRecords.Add(record);
                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse] Successfully generated Colony Named event record: {eventId}");

                // Send a positive event letter
                Find.LetterStack.ReceiveLetter(
                    "Colony Formalized",
                    logText,
                    LetterDefOf.PositiveEvent
                );
            }
            catch (Exception ex)
            {
                RimSynapse.SynapseLogger.Warn("psychology", "Failed to generate Colony Named event record: " + ex.Message);
            }
        }
    }
}
