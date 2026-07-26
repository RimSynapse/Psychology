using UnityEngine;
using Verse;
using RimWorld;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.UI
{
    /// <summary>
    /// A window that displays the full commemoration log of a pawn ceremony or event.
    /// </summary>
    public class Dialog_CommemoratedEvent : Window
    {
        private PawnEventRecord record;
        private Vector2 scrollPosition = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(600f, 650f);

        public Dialog_CommemoratedEvent(PawnEventRecord record)
        {
            this.record = record;
            this.doCloseX = true;
            this.draggable = true;
            this.resizeable = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = false;
            this.preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (record == null) return;

            // Title
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect titleRect = new Rect(0f, 0f, inRect.width, 35f);
            Widgets.Label(titleRect, record.eventName);

            // Subtitle
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Rect subtitleRect = new Rect(0f, 35f, inRect.width, 25f);
            Widgets.Label(subtitleRect, $"Honoring {record.targetPawnName} — {record.dateString}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // Divider Line
            Rect dividerRect = new Rect(0f, 65f, inRect.width, 2f);
            Widgets.DrawLineHorizontal(dividerRect.x, dividerRect.y, dividerRect.width);

            // Scrolling View area
            Rect outRect = new Rect(0f, 75f, inRect.width, inRect.height - 85f);
            float textHeight = Text.CalcHeight(record.fullLog, outRect.width - 20f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, textHeight + 20f);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            Widgets.Label(viewRect, record.fullLog);
            Widgets.EndScrollView();
        }
    }
}
