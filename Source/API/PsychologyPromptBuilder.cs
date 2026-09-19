using System;
using System.Linq;
using System.Collections.Generic;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;
using RimSynapse.Models;
using RimSynapse.Utils;
using RimSynapse.Psychology.Models;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Prompt construction and LLM callbacks for opportunistic tasks:
    /// visitor backstory generation, relationship evaluation, and visitor memory parsing.
    /// </summary>
    public static partial class SynapsePsychology
    {
        /// <summary>#21: build the visitor CHILDHOOD prompt, weaving in cross-mod leader context. Factions
        /// injects faction history / ideology / leadership style via <see cref="RimSynapse.SynapseCoreContext.GatherGenericContext"/>
        /// (the same hook the colonist path uses), and the leadership hierarchy (who they report to / their role)
        /// via <see cref="RimSynapse.SynapseCoreHierarchy"/>. Both are zero-subscriber / zero-provider safe, so
        /// with nothing registered the prompt is exactly as before. Split out so tests can assert the injection.</summary>
        internal static string BuildVisitorChildhoodUserMessage(Pawn pawn, string factionName, string factionType)
        {
            var childhood = pawn.story?.Childhood;
            string childhoodTitle = childhood?.title ?? "Unknown";
            string childhoodDesc = childhood?.description ?? "An unremarkable childhood.";
            string skillBonuses = FormatSkillGains(childhood);
            string hierarchy = HierarchyContext(pawn);
            string context = RimSynapse.SynapseCoreContext.GatherGenericContext(pawn, RimSynapse.SynapseContextTypes.BackstoryChildhood);

            return $@"Visitor: {pawn.Name.ToStringShort} from {factionName} ({factionType})
Childhood: ""{childhoodTitle}""
Description: ""{childhoodDesc}""
Skills: {skillBonuses}{hierarchy}{context}";
        }

        /// <summary>#21: build the visitor ADULTHOOD prompt with the same cross-mod leader context + hierarchy,
        /// plus childhood/hometown continuity. Testable seam.</summary>
        internal static string BuildVisitorAdulthoodUserMessage(Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp, string factionName, string factionType)
        {
            var adulthood = pawn.story?.Adulthood;
            string adulthoodTitle = adulthood?.title ?? "Unknown";
            string adulthoodDesc = adulthood?.description ?? "An uneventful adult life.";
            string skillBonuses = FormatSkillGains(adulthood);

            var childhoodMem = coreComp?.memories?.LastOrDefault(m => m.memoryType == "BackstoryChildhood");
            string childhoodContext = childhoodMem != null
                ? $"\nChildhood Memory (maintain continuity): \"{childhoodMem.summary}\""
                : "";
            string hometownContext = !string.IsNullOrEmpty(coreComp?.hometown)
                ? $"\nHometown: {coreComp.hometown}"
                : "";
            string hierarchy = HierarchyContext(pawn);
            string context = RimSynapse.SynapseCoreContext.GatherGenericContext(pawn, RimSynapse.SynapseContextTypes.BackstoryAdulthood);

            return $@"Visitor: {pawn.Name.ToStringShort} from {factionName} ({factionType})
Adulthood: ""{adulthoodTitle}""
Description: ""{adulthoodDesc}""
Skills: {skillBonuses}{hometownContext}{childhoodContext}{hierarchy}{context}";
        }

        /// <summary>#21: a one-line hierarchy note from the (open-ended) leadership hook — the pawn's role and who
        /// they report to — so an LLM-authored leader reflects their rank. Empty until a mod (Factions) registers
        /// a hierarchy provider, so this adds nothing today.</summary>
        internal static string HierarchyContext(Pawn pawn)
        {
            if (pawn == null) return "";
            string role = RimSynapse.SynapseCoreHierarchy.RoleTitle(pawn);
            Pawn superior = RimSynapse.SynapseCoreHierarchy.ReportsToPawn(pawn);
            if (string.IsNullOrEmpty(role) && superior == null) return "";

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(role)) parts.Add($"Role: {role}");
            if (superior != null) parts.Add($"Reports to: {superior.Name?.ToStringShort ?? superior.LabelShort}");
            return "\nLeadership: " + string.Join("; ", parts);
        }

        private static void GenerateVisitorChildhoodMemory(Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp, string factionName, string factionType)
        {
            string systemPrompt = @"You are writing a vivid third-person memory for a visitor in the RimWorld universe, as if the AI Storyteller is describing their childhood.
This memory is from their CHILDHOOD. Keep it brief and grounded.

RULES:
- Write 80-120 words, third person (using their name or ""he/she"", never ""I"" or ""my"")
- Ground the memory in their skill bonuses — their abilities come from real experience
- Generate a ""Hometown"" — their place of origin, matching their faction type:
  - Outlander → a named settlement (e.g., ""Port Valen"")
  - Tribal → a geographic feature or camp (e.g., ""the Ashen Ridge camp"")
  - Pirate → a ship or den (e.g., ""the Rust Fang"")
  - Imperial → a city or estate
  - Nomadic/orphan → something vague (e.g., ""the trade roads south of Helixon"")

You MUST respond in valid JSON:
{
  ""Memory"": ""Josema remembered the first time he...(80-120 words)..."",
  ""Hometown"": ""Port Valen"",
  ""Tags"": [""Origin"", ""Childhood""]
}";

            string userMessage = BuildVisitorChildhoodUserMessage(pawn, factionName, factionType);

            var options = new ChatOptions { priority = 6, requestName = "Visitor Childhood", targetName = pawn.Name.ToStringShort };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => OnVisitorChildhoodGenerated(result, pawn, coreComp, factionName, factionType),
                options
            );
        }

        private static void OnVisitorChildhoodGenerated(ChatResult result, Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp, string factionName, string factionType)
        {
            if (result.success)
            {
                try
                {
                    string json = JsonHelper.ExtractJson(result.content);
                    if (json != null)
                    {
                        var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (parsed != null && parsed.ContainsKey("Memory"))
                        {
                            string memoryText = parsed["Memory"].ToString();
                            var tags = new List<string> { "Childhood", "Origin" };
                            if (parsed.ContainsKey("Tags") && parsed["Tags"] is Newtonsoft.Json.Linq.JArray arr)
                                tags = arr.Select(t => t.ToString()).ToList();

                            if (parsed.ContainsKey("Hometown"))
                                coreComp.hometown = parsed["Hometown"].ToString();

                            long childTick = SynapseDateHelper.GetChildhoodMemoryTick(pawn);
                            coreComp.AddMemory(new WeightedMemory
                            {
                                summary = memoryText,
                                weight = 0.8f,
                                baseWeight = 0.8f,
                                decayRate = 0f,
                                tags = tags,
                                memoryType = "BackstoryChildhood",
                                absTick = childTick,
                                gameTick = (int)(childTick - SynapseDateHelper.GetAdjustmentTick())
                            });

                            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Visitor childhood memory for {pawn.Name.ToStringShort}. Hometown: {coreComp.hometown ?? "none"}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to parse visitor childhood memory: {ex.Message}");
                }
            }

            // Chain to adulthood if available, otherwise finalize
            if (pawn.story?.Adulthood != null)
            {
                GenerateVisitorAdulthoodMemory(pawn, coreComp, factionName, factionType);
            }
            else
            {
                MarkBackstoryCreated(pawn);
                RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Visitor backstory complete for {pawn.Name.ToStringShort} (childhood only).");
            }
        }

        private static void GenerateVisitorAdulthoodMemory(Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp, string factionName, string factionType)
        {
            string systemPrompt = @"You are writing a vivid third-person memory for a visitor in the RimWorld universe, as if the AI Storyteller is describing their adulthood.
This memory is from their ADULTHOOD. Keep it brief and grounded.

RULES:
- Write 80-120 words, third person (using their name or ""he/she"", never ""I"" or ""my"")
- Ground the memory in their skill bonuses
- If a childhood memory is provided, maintain narrative continuity
- This should be a defining adult moment — what made them who they are

You MUST respond in valid JSON:
{
  ""Memory"": ""The day Josema first...(80-120 words)..."",
  ""Tags"": [""Adulthood"", ""Defining""]
}";

            string userMessage = BuildVisitorAdulthoodUserMessage(pawn, coreComp, factionName, factionType);

            var options = new ChatOptions { priority = 7, requestName = "Visitor Adulthood", targetName = pawn.Name.ToStringShort };

            SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                systemPrompt,
                userMessage,
                result => OnVisitorAdulthoodGenerated(result, pawn, coreComp),
                options
            );
        }

        private static void OnVisitorAdulthoodGenerated(ChatResult result, Pawn pawn, RimSynapse.Comps.SynapseCorePawnComp coreComp)
        {
            if (result.success)
            {
                try
                {
                    string json = JsonHelper.ExtractJson(result.content);
                    if (json != null)
                    {
                        var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (parsed != null && parsed.ContainsKey("Memory"))
                        {
                            string memoryText = parsed["Memory"].ToString();
                            var tags = new List<string> { "Adulthood", "Defining" };
                            if (parsed.ContainsKey("Tags") && parsed["Tags"] is Newtonsoft.Json.Linq.JArray arr)
                                tags = arr.Select(t => t.ToString()).ToList();

                            long adultTick = SynapseDateHelper.GetAdulthoodMemoryTick(pawn);
                            coreComp.AddMemory(new WeightedMemory
                            {
                                summary = memoryText,
                                weight = 0.8f,
                                baseWeight = 0.8f,
                                decayRate = 0f,
                                tags = tags,
                                memoryType = "BackstoryAdulthood",
                                absTick = adultTick,
                                gameTick = (int)(adultTick - SynapseDateHelper.GetAdjustmentTick())
                            });

                            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Visitor adulthood memory for {pawn.Name.ToStringShort} ({memoryText.Length} chars).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    RimSynapse.SynapseLogger.Warn("psychology", $"[RimSynapse-Psychology] Failed to parse visitor adulthood memory: {ex.Message}");
                }
            }

            MarkBackstoryCreated(pawn);
            RimSynapse.SynapseLogger.Info("psychology", $"[RimSynapse-Psychology] Visitor backstory complete for {pawn.Name.ToStringShort}.");
        }
    }
}
