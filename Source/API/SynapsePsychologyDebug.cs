using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using RimSynapse.Comps;
using RimSynapse.Psychology.Comps;
using RimSynapse.Models;
using RimSynapse.Utils;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Live-LLM debug commands (Psychology#52): generate a memory and run an evaluation against the REAL
    /// configured model, logging and showing the response. Shared by the game tools and the DevMode gizmos.
    /// </summary>
    public static class SynapsePsychologyDebug
    {
        /// <summary>Log the response under a scrapeable tag and show it in-game on the main thread.</summary>
        public static void ShowDebugResponse(string title, string text)
        {
            string body = string.IsNullOrEmpty(text) ? "(empty response)" : text;
            RimSynapse.SynapseLogger.Info("psychology", $"[SYNAPSE-DEBUG] {title} | {body.Replace("\n", " ")}");
            SynapseGameComponent.Enqueue(() =>
            {
                try { Find.WindowStack.Add(new Dialog_MessageBox(body, title: title)); } catch { }
            });
        }

        /// <summary>Generate one memory for a pawn via the live LLM; show the raw response, then add it.</summary>
        public static void GenerateMemory(Pawn pawn, string eventDescription)
        {
            if (pawn == null) return;
            var coreComp = pawn.TryGetComp<SynapseCorePawnComp>();
            if (coreComp == null) { ShowDebugResponse("Debug Memory Gen", "pawn has no SynapseCorePawnComp"); return; }

            string ev = string.IsNullOrEmpty(eventDescription) ? "a notable but otherwise ordinary day in the colony" : eventDescription;
            string traits = pawn.story?.traits?.allTraits != null
                ? string.Join(", ", pawn.story.traits.allTraits.Select(t => t.Label)) : "None";

            string systemPrompt = @"You are generating a single third-person memory for a RimWorld colonist based on an event. Blend their traits and situation. Weight is 0.0-1.0 (minor 0.05-0.25, standard 0.25-0.5, major 0.5-0.8, defining 0.8-1.0).
Respond ONLY as valid JSON, no markdown:
{ ""Summary"": ""third-person 1-2 sentence memory"", ""Tags"": [""Tag1""], ""Weight"": 0.4, ""Decay"": 0.15 }";
            string userMessage = $"Colonist: {pawn.Name.ToStringShort}\nTraits: {traits}\nEvent: {ev}";

            var options = new ChatOptions { priority = 5, requestName = "Debug Memory Gen", targetName = pawn.Name.ToStringShort };

            SynapseClient.PromptAsync(RimSynapsePsychologyMod.ModHandle, systemPrompt, userMessage, result =>
            {
                if (!result.success) { ShowDebugResponse("Debug Memory Gen — FAILED", result.error ?? "unknown error"); return; }
                ShowDebugResponse($"Debug Memory Gen — {pawn.Name.ToStringShort}", result.content);
                try
                {
                    string json = JsonHelper.ExtractJson(result.content);
                    if (json == null) return;
                    var d = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (d == null || !d.ContainsKey("Summary")) return;
                    var tags = new List<string>();
                    if (d.TryGetValue("Tags", out var tg) && tg is Newtonsoft.Json.Linq.JArray arr)
                        tags = arr.Select(x => x.ToString()).ToList();
                    float weight = d.TryGetValue("Weight", out var w) && w != null ? Convert.ToSingle(w) : 0.4f;
                    SynapseGameComponent.Enqueue(() => SynapsePsychology.AddMemory(pawn, new WeightedMemory
                    {
                        summary = d["Summary"].ToString(),
                        weight = weight,
                        baseWeight = weight,
                        tags = tags,
                        memoryType = "EventReflection",
                        absTick = Find.TickManager?.TicksAbs ?? 0L
                    }));
                }
                catch (Exception ex) { RimSynapse.SynapseLogger.Warn("psychology", $"[SYNAPSE-DEBUG] memory parse failed: {ex.Message}"); }
            }, options);
        }

        /// <summary>Run the daily psychology evaluation via the live LLM; show the parsed result when done.</summary>
        public static void RunEvaluation(Pawn pawn)
        {
            if (pawn == null) return;
            var pawnComp = pawn.TryGetComp<SynapsePawnComp>();
            var coreComp = pawn.TryGetComp<SynapseCorePawnComp>();
            if (pawnComp == null || coreComp == null) { ShowDebugResponse("Debug Evaluation", "pawn is missing a required comp"); return; }

            float mood = pawn.needs?.mood?.CurLevelPercentage ?? 0.5f;
            var events = coreComp.memories.Where(m => (Find.TickManager.TicksAbs - m.absTick) < 60000).ToList();

            SynapsePsychology.QueueDailyPsychologyReview(pawn, mood, events, success =>
            {
                if (!success) { ShowDebugResponse("Debug Evaluation — FAILED", "the evaluation call failed (see log)"); return; }
                var sb = new StringBuilder();
                if (pawnComp.medicalProfile != null)
                    foreach (var kv in pawnComp.medicalProfile) sb.AppendLine($"{kv.Key}: {kv.Value}");
                ShowDebugResponse($"Debug Evaluation — {pawn.Name.ToStringShort}", sb.Length > 0 ? sb.ToString().TrimEnd() : "(no fields parsed)");
            });
        }

        // ── game-tool handlers (async: trigger + report the response via log/in-game) ──
        public static string GenerateMemoryHandler(string args)
        {
            try
            {
                var d = JsonConvert.DeserializeObject<Dictionary<string, object>>(args) ?? new Dictionary<string, object>();
                var pawn = SynapseCoreDebug.FindPawn(d.TryGetValue("pawnName", out var pn) ? pn?.ToString() : null);
                if (pawn == null) return "{\"error\": \"pawn not found\"}";
                GenerateMemory(pawn, d.TryGetValue("event", out var e) ? e?.ToString() : null);
                return "{\"status\": \"queued\", \"note\": \"LLM response logged as [SYNAPSE-DEBUG] and shown in-game\"}";
            }
            catch (Exception ex) { return JsonConvert.SerializeObject(new { error = ex.Message }); }
        }

        public static string RunEvaluationHandler(string args)
        {
            try
            {
                var d = JsonConvert.DeserializeObject<Dictionary<string, object>>(args) ?? new Dictionary<string, object>();
                var pawn = SynapseCoreDebug.FindPawn(d.TryGetValue("pawnName", out var pn) ? pn?.ToString() : null);
                if (pawn == null) return "{\"error\": \"pawn not found\"}";
                RunEvaluation(pawn);
                return "{\"status\": \"queued\", \"note\": \"evaluation result logged as [SYNAPSE-DEBUG] and shown in-game\"}";
            }
            catch (Exception ex) { return JsonConvert.SerializeObject(new { error = ex.Message }); }
        }
    }
}
