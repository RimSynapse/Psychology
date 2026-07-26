using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using RimSynapse.Comps;
using RimSynapse.Psychology.Comps;
using RimSynapse.Utils;

namespace RimSynapse.Psychology.UI
{
    /// <summary>
    /// Tab drawing methods for the Psychology dialog window:
    /// Profile, Memories, and Social Network tabs.
    /// </summary>
    public partial class Dialog_PawnPsychology
    {
        private void DrawProfileTab(Rect rect)
        {
            float curY = rect.y;

            // Draw Portrait
            Rect portraitRect = new Rect(rect.x, curY, 150f, 150f);
            GUI.DrawTexture(portraitRect, PortraitsCache.Get(pawn, new Vector2(150f, 150f), Rot4.South));
            
            // Force Review Button (moved below portrait)
            Rect forceReviewRect = new Rect(rect.x, portraitRect.yMax + 10f, 150f, 30f);
            if (Widgets.ButtonText(forceReviewRect, "Force Psych Review"))
            {
                var cc = pawn.TryGetComp<RimSynapse.Comps.SynapseCorePawnComp>();
                var pc = pawn.TryGetComp<SynapsePawnComp>();
                if (cc != null && pc != null)
                {
                    float mood = pawn.needs?.mood?.CurLevelPercentage ?? 0.5f;
                    var events = cc.memories
                        .Where(m => Find.TickManager.TicksAbs - m.absTick < 60000)
                        .ToList();
                    RimSynapse.Psychology.API.SynapsePsychology.QueueDailyPsychologyReview(pawn, mood, events, null, false);
                    Messages.Message($"Forced psychology review for {pawn.LabelShort}.", MessageTypeDefOf.TaskCompletion, false);
                }
            }

            float infoX = portraitRect.xMax + 15f;
            float infoWidth = rect.width - infoX;

            // Draw key info next to portrait
            Text.Font = GameFont.Tiny;
            float lineY = curY;

            // Hometown
            string hometown = coreComp.hometown ?? "Unknown";
            Widgets.Label(new Rect(infoX, lineY, infoWidth, 20f), $"Hometown: {hometown}");
            lineY += 20f;

            // LLM Traits
            if (coreComp.llmTraits != null && coreComp.llmTraits.Count > 0)
            {
                foreach (string trait in coreComp.llmTraits)
                {
                    Widgets.Label(new Rect(infoX, lineY, infoWidth, 20f), trait);
                    lineY += 18f;
                }
            }

            Text.Font = GameFont.Small;

            // === Personality Summary ===
            float profileY = Mathf.Max(lineY + 15f, portraitRect.yMax + 50f);
            
            // Scrollable Medical Profile Form
            var pawnComp = pawn.TryGetComp<SynapsePawnComp>();
            if (pawnComp == null || pawnComp.medicalProfile == null) return;

            string[] formFields = { "Mood", "Interpersonal", "Trauma", "Cognitive", "Motivations", "Identity", "Morality", "Authority", "Addiction", "Summary" };
            string[] formLabels = { "Emotional Baseline", "Interpersonal Dynamics", "Trauma Profile", "Cognitive Patterns", "Motivations", "Identity", "Moral Compass", "Authority Response", "Addiction Profile", "Clinical Summary" };

            // Calculate scroll height
            float totalFormHeight = 0f;
            for (int i = 0; i < formFields.Length; i++)
            {
                string val = pawnComp.medicalProfile.TryGetValue(formFields[i], out string v) ? v : "[Awaiting Evaluation]";
                totalFormHeight += 22f + Text.CalcHeight(val, rect.width - 30f) + 20f;
            }
            totalFormHeight += 50f; // Extra for personality summary
            
            // Calculate Patient History height
            var historyMemories = coreComp.memories.Where(m => m.tags != null && m.tags.Contains("TraitShift")).OrderBy(m => m.absTick).ToList();
            totalFormHeight += 22f + 10f; // Header
            if (historyMemories.Count == 0)
            {
                totalFormHeight += Text.CalcHeight("No major psychological changes recorded.", rect.width - 30f) + 20f;
            }
            else
            {
                foreach (var m in historyMemories)
                {
                    string dateStr = GenDate.DateFullStringAt(m.absTick, Find.WorldGrid.LongLatOf(pawn.Tile));
                    string entry = $"[{dateStr}] {m.summary}";
                    totalFormHeight += Text.CalcHeight(entry, rect.width - 30f) + 5f;
                }
                totalFormHeight += 20f;
            }

            Rect scrollRect = new Rect(rect.x, profileY, rect.width, rect.yMax - profileY);
            Rect viewRect = new Rect(0f, 0f, rect.width - 20f, totalFormHeight);

            Widgets.BeginScrollView(scrollRect, ref profileScrollPosition, viewRect);

            float formY = 0f;

            // Personality Summary
            if (!string.IsNullOrEmpty(coreComp.personalitySummary))
            {
                GUI.color = new Color(0.9f, 0.9f, 0.7f);
                Text.Font = GameFont.Small;
                float sumHeight = Text.CalcHeight(coreComp.personalitySummary, viewRect.width);
                Widgets.Label(new Rect(0f, formY, viewRect.width, sumHeight), coreComp.personalitySummary);
                GUI.color = Color.white;
                formY += sumHeight + 10f;
            }

            Widgets.DrawLineHorizontal(rect.x, formY, viewRect.width);
            formY += 10f;

            // Draw Daily Evaluation Fields
            for (int i = 0; i < formFields.Length; i++)
            {
                string key = formFields[i];
                string label = formLabels[i];
                string val = pawnComp.medicalProfile.TryGetValue(key, out string v) ? v : "[Awaiting Evaluation]";
                
                // Draw Header
                GUI.color = new Color(0.7f, 0.9f, 1f);
                Text.Font = GameFont.Small;
                Rect headerRect = new Rect(rect.x, formY, viewRect.width, 22f);
                Widgets.Label(headerRect, $"<b>{label}</b>");
                GUI.color = Color.white;
                formY += 22f;
                
                // Draw Value
                float textHeight = Text.CalcHeight(val, viewRect.width);
                Rect valRect = new Rect(rect.x, formY, viewRect.width, textHeight);
                Widgets.Label(valRect, val);
                formY += textHeight + 10f;
                
                // Draw divider
                Widgets.DrawLineHorizontal(rect.x, formY, viewRect.width);
                formY += 10f;
            }
            
            // === Patient History (Trait Timeline) ===
            GUI.color = new Color(0.7f, 0.9f, 1f);
            Text.Font = GameFont.Small;
            Rect historyHeaderRect = new Rect(rect.x, formY, viewRect.width, 22f);
            Widgets.Label(historyHeaderRect, "<b>Patient History</b>");
            GUI.color = Color.white;
            formY += 22f;
            
            if (historyMemories.Count == 0)
            {
                float textHeight = Text.CalcHeight("No major psychological changes recorded.", viewRect.width);
                Rect valRect = new Rect(rect.x, formY, viewRect.width, textHeight);
                Widgets.Label(valRect, "No major psychological changes recorded.");
                formY += textHeight + 10f;
            }
            else
            {
                foreach (var m in historyMemories)
                {
                    string dateStr = GenDate.DateFullStringAt(m.absTick, Find.WorldGrid.LongLatOf(pawn.Tile));
                    string entry = $"[{dateStr}] {m.summary}";
                    float entryHeight = Text.CalcHeight(entry, viewRect.width);
                    Widgets.Label(new Rect(rect.x, formY, viewRect.width, entryHeight), entry);
                    formY += entryHeight + 5f;
                }
                formY += 15f;
            }
            
            Widgets.DrawLineHorizontal(rect.x, formY, viewRect.width);
            
            Widgets.EndScrollView();
        }

        private void DrawMemoriesTab(Rect rect)
        {
            if (coreComp.memories.Count == 0)
            {
                Widgets.Label(rect, "No memories recorded yet.");
                return;
            }

            // Draw Checkbox at the top of the tab
            Rect checkboxRect = new Rect(rect.x, rect.y, rect.width, 24f);
            Widgets.CheckboxLabeled(checkboxRect, "Show Short Term Memories", ref showShortTermMemories);

            // Sort chronologically (oldest first) using absTick
            var sortedMemories = coreComp.memories.OrderBy(m => m.absTick).ToList();

            // Filter out social/conversation memories unless DevMode is on OR showShortTermMemories is enabled
            if (!Prefs.DevMode && !showShortTermMemories)
            {
                sortedMemories = sortedMemories.Where(m => 
                    m.memoryType != "social" && 
                    m.memoryType != "social_chat" && 
                    m.memoryType != "non_response" &&
                    m.memoryType != "conversation" &&
                    m.memoryType != "overheard" &&
                    !(m.memoryType == "EventReflection" && m.tags != null && (m.tags.Contains("Chitchat") || m.tags.Contains("chitchat") || m.tags.Contains("DeepTalk") || m.tags.Contains("deeptalk")))
                ).ToList();
            }

            if (sortedMemories.Count == 0)
            {
                Widgets.Label(new Rect(rect.x, rect.y + 28f, rect.width, rect.height - 28f), "No memories recorded yet.");
                return;
            }

            float viewHeight = 0f;
            foreach (var memory in sortedMemories)
            {
                viewHeight += Text.CalcHeight(memory.summary, rect.width - 20f) + 30f;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 20f, viewHeight);
            Rect scrollRect = new Rect(rect.x, rect.y + 28f, rect.width, rect.height - 28f);
            
            Widgets.BeginScrollView(scrollRect, ref memoriesScrollPosition, viewRect);
            
            float currentEntryY = 0f;
            foreach (var memory in sortedMemories)
            {
                // Draw date label using the chronological absTick
                string dateStr = SynapseDateHelper.FormatAbsTick(memory.absTick);
                
                string sourceLabel = "Event";
                if (!string.IsNullOrEmpty(memory.memoryType))
                {
                    if (memory.memoryType == "BackstoryChildhood")
                    {
                        string title = pawn.story?.Childhood?.title;
                        sourceLabel = !string.IsNullOrEmpty(title) 
                            ? $"Childhood: {title.CapitalizeFirst()}" 
                            : "Childhood Backstory";
                    }
                    else if (memory.memoryType == "BackstoryAdulthood")
                    {
                        string title = pawn.story?.Adulthood?.title;
                        sourceLabel = !string.IsNullOrEmpty(title) 
                            ? $"Adulthood: {title.CapitalizeFirst()}" 
                            : "Adulthood Backstory";
                    }
                    else if (memory.memoryType == "Arrival" || memory.memoryType == "ArrivalFirstImpression") sourceLabel = Find.Scenario?.name ?? "Scenario Start";
                    else if (memory.memoryType == "social" || memory.memoryType == "social_chat") sourceLabel = "Conversation";
                    else if (memory.memoryType == "non_response") sourceLabel = "Ignored Dialogue";
                    else if (memory.memoryType == "funeral" || memory.memoryType == "Funeral") sourceLabel = "Funeral Ceremony";
                    else if (memory.memoryType == "EventReflection")
                    {
                        sourceLabel = "Past Event";
                        if (memory.tags != null && memory.tags.Count > 0)
                        {
                            if (memory.tags.Contains("Chitchat") || memory.tags.Contains("chitchat") || memory.tags.Contains("Chit-Chat"))
                            {
                                sourceLabel = "Chit-Chat";
                            }
                            else if (memory.tags.Contains("DeepTalk") || memory.tags.Contains("deeptalk") || memory.tags.Contains("Deep Conversation"))
                            {
                                sourceLabel = "Deep Conversation";
                            }
                            else if (memory.tags.Contains("Insult") || memory.tags.Contains("insult"))
                            {
                                sourceLabel = "Insult";
                            }
                            else if (memory.tags.Contains("Death") || memory.tags.Contains("death"))
                            {
                                string deadPawnName = null;
                                if (Current.ProgramState == ProgramState.Playing)
                                {
                                    var allPawns = (Find.CurrentMap?.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
                                        .Concat(Find.WorldPawns.AllPawnsAliveOrDead);
                                    foreach (var p in allPawns)
                                    {
                                        if (p.Name != null && !string.IsNullOrEmpty(p.Name.ToStringShort) && 
                                            memory.summary.Contains(p.Name.ToStringShort))
                                        {
                                            deadPawnName = p.Name.ToStringShort;
                                            break;
                                        }
                                    }
                                }
                                sourceLabel = !string.IsNullOrEmpty(deadPawnName) ? $"Death of {deadPawnName}" : "Colonist Death";
                            }
                            else
                            {
                                // Try to resolve human-readable name from vanilla/modded IncidentDefs
                                foreach (var tag in memory.tags)
                                {
                                    var incDef = DefDatabase<IncidentDef>.GetNamed(tag, false);
                                    if (incDef != null)
                                    {
                                        sourceLabel = incDef.LabelCap;
                                        break;
                                    }
                                }

                                // Fallback to our custom categories if no IncidentDef is matched
                                if (sourceLabel == "Past Event")
                                {
                                    if (memory.tags.Contains("Raid")) sourceLabel = "Raid Incident";
                                    else if (memory.tags.Contains("Sickness")) sourceLabel = "Illness";
                                }
                            }
                        }

                        // General fallback for conversation descriptions
                        if (sourceLabel == "Past Event" && (memory.summary.Contains("talking about") || memory.summary.Contains("talked about") || memory.summary.Contains("discussing") || memory.summary.Contains("discussed")))
                        {
                            sourceLabel = "Conversation";
                        }
                    }
                    else
                    {
                        sourceLabel = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(memory.memoryType);
                    }
                }

                float alpha = Mathf.Clamp(memory.weight, 0.3f, 1f);
                
                // Date and type header
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Rect dateRect = new Rect(0f, currentEntryY, viewRect.width, 18f);
                Widgets.Label(dateRect, $"[{dateStr}]  •  {sourceLabel}    Weight: {memory.weight:F2}");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                currentEntryY += 18f;

                // Memory summary text
                float textHeight = Text.CalcHeight(memory.summary, viewRect.width);
                Rect textRect = new Rect(0f, currentEntryY, viewRect.width, textHeight);
                
                // Use alpha to indicate memory strength (stronger = more opaque)
                GUI.color = new Color(1f, 1f, 1f, 0.4f + (memory.weight * 0.6f));
                Widgets.Label(textRect, memory.summary);
                GUI.color = Color.white;

                currentEntryY += textHeight + 10f;
            }
            
            Widgets.EndScrollView();
        }

        private void DrawSocialNetworkTab(Rect rect)
        {
            var pawnComp = pawn.TryGetComp<SynapsePawnComp>();
            if (pawnComp == null || pawnComp.socialNetwork == null || pawnComp.socialNetwork.Count == 0)
            {
                Widgets.Label(rect, "No social relationships recorded yet.");
                return;
            }

            if (cachedTrustPawns == null)
            {
                cachedTrustPawns = new Dictionary<string, Pawn>();
                var allPawns = (Find.Maps != null 
                    ? Find.Maps.SelectMany(m => m.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>()) 
                    : Enumerable.Empty<Pawn>())
                    .Concat(Find.WorldPawns.AllPawnsAliveOrDead);
                foreach (var kvp in pawnComp.socialNetwork)
                {
                    Pawn target = allPawns.FirstOrDefault(p => p.GetUniqueLoadID() == kvp.Key);
                    if (target != null) cachedTrustPawns[kvp.Key] = target;
                }
            }

            float viewHeight = 0f;
            foreach (var kvp in pawnComp.socialNetwork)
            {
                viewHeight += 60f;
                if (kvp.Value.relationshipMemories.Count > 0)
                {
                    string mem = kvp.Value.relationshipMemories.Last();
                    viewHeight += Text.CalcHeight($"\"{mem}\"", rect.width - 90f) + 10f;
                }
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 20f, viewHeight);
            
            Widgets.BeginScrollView(rect, ref socialScrollPosition, viewRect);
            
            float curY = 0f;
            foreach (var kvp in pawnComp.socialNetwork)
            {
                string loadId = kvp.Key;
                var record = kvp.Value;
                
                cachedTrustPawns.TryGetValue(loadId, out Pawn targetPawn);
                
                Rect entryRect = new Rect(0f, curY, viewRect.width, 50f);
                
                if (targetPawn != null)
                {
                    Rect portraitRect = new Rect(0f, curY, 50f, 50f);
                    GUI.DrawTexture(portraitRect, PortraitsCache.Get(targetPawn, new Vector2(50f, 50f), Rot4.South));
                    
                    Rect nameRect = new Rect(60f, curY + 5f, 200f, 20f);
                    Text.Font = GameFont.Small;
                    Widgets.Label(nameRect, targetPawn.Name.ToStringShort);
                }
                else
                {
                    Rect nameRect = new Rect(60f, curY + 5f, 200f, 20f);
                    Text.Font = GameFont.Small;
                    Widgets.Label(nameRect, $"Unknown ({loadId})");
                }
                
                // Get Affinity (Opinion)
                int affinity = targetPawn != null && targetPawn.relations != null ? pawn.relations.OpinionOf(targetPawn) : 0;
                
                // Draw Metrics
                Text.Font = GameFont.Tiny;
                
                // Trust
                Rect trustRect = new Rect(250f, curY + 5f, 100f, 20f);
                if (record.trust >= 50) GUI.color = Color.green;
                else if (record.trust >= 10) GUI.color = new Color(0.6f, 0.9f, 0.6f);
                else if (record.trust > -10) GUI.color = Color.white;
                else if (record.trust > -50) GUI.color = new Color(0.9f, 0.6f, 0.6f);
                else GUI.color = Color.red;
                Widgets.Label(trustRect, $"Trust: {record.trust:F0}");
                
                // Familiarity
                Rect famRect = new Rect(360f, curY + 5f, 100f, 20f);
                GUI.color = new Color(0.6f + (record.familiarity/100f)*0.4f, 0.6f + (record.familiarity/100f)*0.4f, 1f);
                Widgets.Label(famRect, $"Familiarity: {record.familiarity:F0}");
                
                // Affinity
                Rect affRect = new Rect(470f, curY + 5f, 100f, 20f);
                if (affinity >= 50) GUI.color = Color.green;
                else if (affinity >= 10) GUI.color = new Color(0.6f, 0.9f, 0.6f);
                else if (affinity > -10) GUI.color = Color.white;
                else if (affinity > -50) GUI.color = new Color(0.9f, 0.6f, 0.6f);
                else GUI.color = Color.red;
                Widgets.Label(affRect, $"Affinity: {affinity}");
                
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                
                float extraHeight = 0f;
                if (record.relationshipMemories.Count > 0)
                {
                    string mem = record.relationshipMemories.Last();
                    float memHeight = Text.CalcHeight($"\"{mem}\"", viewRect.width - 70f);
                    Rect memRect = new Rect(60f, curY + 25f, viewRect.width - 70f, memHeight);
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.8f, 0.8f, 0.8f);
                    Widgets.Label(memRect, $"\"{mem}\"");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    extraHeight = memHeight + 10f;
                }
                
                curY += 60f + extraHeight;
            }
            
            Widgets.EndScrollView();
        }

        private void DrawFuneralTab(Rect rect)
        {
            var worldComp = Find.World?.GetComponent<SynapsePsychologyWorldComponent>();
            string recordText = "No funeral record found for this colonist.";
            if (worldComp != null)
            {
                string pawnId = pawn.GetUniqueLoadID();
                if (worldComp.funeralRecords.TryGetValue(pawnId, out string record))
                {
                    recordText = record;
                }
            }

            Rect outRect = new Rect(rect.x, rect.y, rect.width, rect.height - 10f);
            float textHeight = Text.CalcHeight(recordText, outRect.width - 20f);
            Rect viewRect = new Rect(0, 0, outRect.width - 20f, textHeight + 20f);

            Widgets.BeginScrollView(outRect, ref funeralScrollPosition, viewRect);
            Widgets.Label(new Rect(0, 0, viewRect.width, viewRect.height), recordText);
            Widgets.EndScrollView();
        }
    }
}
