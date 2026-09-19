using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Jobs;

namespace RimSynapse.Psychology.UI
{
    public class Dialog_TherapySession : Window
    {
        private Pawn initiator;
        private Pawn target;
        private JobDriver_TherapySession driver;

        private bool guidingHand = false;
        private string playerInput = "";
        
        private List<string> chatLog = new List<string>();
        private Vector2 scrollPos = Vector2.zero;
        private bool waitingForLLM = false;
        private string suggestedStatement = "";
        private float timerStartRealtime = -1f;

        public override Vector2 InitialSize => new Vector2(500f, 600f);

        public Dialog_TherapySession(Pawn initiator, Pawn target, JobDriver_TherapySession driver, bool guidingHand = false)
        {
            this.initiator = initiator;
            this.target = target;
            this.driver = driver;
            this.guidingHand = guidingHand; // #17: Guiding Hand starts manual; Watch starts auto-streaming

            this.forcePause = false;
            this.preventCameraMotion = false;
            this.doCloseX = true;
            this.draggable = true;
            this.resizeable = true;
            
            chatLog.Add($"[System] Therapy session initiated by {initiator.NameShortColored} for {target.NameShortColored}.");
            RequestLLMSuggestion("Initiate therapy session.");
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), $"Therapy Session: {initiator.NameShortColored} -> {target.NameShortColored}");
            Text.Font = GameFont.Small;

            // Chat Log
            Rect logRect = new Rect(0, 40f, inRect.width, inRect.height - 180f);
            Widgets.DrawMenuSection(logRect);
            
            Rect viewRect = new Rect(0, 0, logRect.width - 20f, chatLog.Count * 25f + 50f);
            Widgets.BeginScrollView(logRect, ref scrollPos, viewRect);
            float curY = 5f;
            foreach (string msg in chatLog)
            {
                float height = Text.CalcHeight(msg, viewRect.width - 10f);
                Widgets.Label(new Rect(5f, curY, viewRect.width - 10f, height), msg);
                curY += height + 5f;
            }
            if (waitingForLLM)
            {
                Widgets.Label(new Rect(5f, curY, viewRect.width - 10f, 25f), "<i>Thinking...</i>");
            }
            Widgets.EndScrollView();

            // Controls
            Rect controlsRect = new Rect(0, inRect.height - 130f, inRect.width, 130f);
            
            Rect toggleRect = new Rect(controlsRect.x, controlsRect.y, 150f, 30f);
            Widgets.CheckboxLabeled(toggleRect, "Guiding Hand", ref guidingHand);

            float timeLeft = timerStartRealtime > 0 ? Mathf.Max(0f, 5f - (Time.realtimeSinceStartup - timerStartRealtime)) : 0f;
            if (!guidingHand && !waitingForLLM && !string.IsNullOrEmpty(suggestedStatement) && string.IsNullOrEmpty(playerInput))
            {
                Widgets.Label(new Rect(controlsRect.x + 160f, controlsRect.y, 200f, 30f), $"Auto-send in: {timeLeft:F1}s");
                if (timeLeft <= 0)
                {
                    // Auto-send suggestion
                    chatLog.Add(suggestedStatement);
                    string lastSt = suggestedStatement;
                    suggestedStatement = "";
                    timerStartRealtime = -1f;
                    RequestLLMResponse(lastSt);
                }
            }

            Rect inputRect = new Rect(controlsRect.x, controlsRect.y + 35f, controlsRect.width - 80f, 30f);
            
            // If they start typing, pause the timer
            string newInput = Widgets.TextField(inputRect, playerInput);
            if (newInput != playerInput && !string.IsNullOrEmpty(newInput))
            {
                timerStartRealtime = -1f; // pause timer indefinitely
            }
            playerInput = newInput;
            
            if (!string.IsNullOrEmpty(suggestedStatement) && string.IsNullOrEmpty(playerInput) && !waitingForLLM)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(inputRect.x + 5f, inputRect.y + 5f, inputRect.width, 25f), suggestedStatement);
                GUI.color = Color.white;
            }

            Rect sendRect = new Rect(inputRect.xMax + 10f, inputRect.y, 70f, 30f);
            if (Widgets.ButtonText(sendRect, "Send") && !waitingForLLM)
            {
                string toSend = !string.IsNullOrEmpty(playerInput) ? $"[{initiator.NameShortColored}] {playerInput}" : suggestedStatement;
                if (!string.IsNullOrEmpty(toSend))
                {
                    chatLog.Add(toSend);
                    playerInput = "";
                    suggestedStatement = "";
                    timerStartRealtime = -1f;
                    RequestLLMResponse(toSend);
                }
            }

            Rect bgRect = new Rect(controlsRect.x, controlsRect.y + 75f, controlsRect.width, 30f);
            if (Widgets.ButtonText(bgRect, "Push to Background"))
            {
                driver.EnableBackgroundResolution(chatLog);
                this.Close();
            }
        }

        private void RequestLLMSuggestion(string context)
        {
            waitingForLLM = true;
            string prompt = $"Given the context: '{context}', suggest the next thing the initiator ({initiator.NameShortColored}) should say as a therapist/consoler. Format: [{initiator.NameShortColored}] dialog...";
            
            RimSynapse.SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                "You are an expert counselor in RimWorld.",
                prompt,
                (result) => 
                {
                    if (result.success)
                    {
                        suggestedStatement = result.content.Trim();
                        timerStartRealtime = Time.realtimeSinceStartup;
                    }
                    waitingForLLM = false;
                },
                new RimSynapse.ChatOptions { priority = 1, requestName = "Therapy Suggestion", targetName = initiator.NameShortColored }
            );
        }

        private void RequestLLMResponse(string initiatorDialog)
        {
            waitingForLLM = true;
            string prompt = $"The initiator said: '{initiatorDialog}'. Generate the target's ({target.NameShortColored}) natural response based on their state of mind. Format: [{target.NameShortColored}] dialog...";
            
            RimSynapse.SynapseClient.PromptAsync(
                RimSynapsePsychologyMod.ModHandle,
                "You are simulating a patient in a therapy session in RimWorld.",
                prompt,
                (result) => 
                {
                    if (result.success)
                    {
                        chatLog.Add(result.content.Trim());
                        scrollPos.y = 9999f;
                        if (!guidingHand)
                        {
                            RequestLLMSuggestion(chatLog[chatLog.Count - 1]);
                        }
                    }
                    waitingForLLM = false;
                },
                new RimSynapse.ChatOptions { priority = 1, requestName = "Therapy Response", targetName = target.NameShortColored }
            );
        }

        public override void PostClose()
        {
            base.PostClose();
            if (driver != null && !driver.backgroundResolution)
            {
                driver.EndJobManually(chatLog);
            }
        }
    }
}
