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
    /// Main psychology dialog window showing a tabbed view of a pawn's
    /// psychological profile, memories, and social network.
    /// Tab drawing logic is in Dialog_PawnPsychology_Tabs.cs (partial class).
    /// </summary>
    public partial class Dialog_PawnPsychology : Window
    {
        private enum PsychologyTab
        {
            Profile,
            Memories,
            SocialNetwork
        }

        private Pawn pawn;
        private SynapseCorePawnComp coreComp;
        private PsychologyTab currentTab = PsychologyTab.Profile;
        
        private Vector2 memoriesScrollPosition = Vector2.zero;
        public static bool showShortTermMemories = false;
        private Vector2 profileScrollPosition = Vector2.zero;
        private Vector2 socialScrollPosition = Vector2.zero;
        private Vector2 funeralScrollPosition = Vector2.zero;
        private Dictionary<string, Pawn> cachedTrustPawns = null;
        
        public override Vector2 InitialSize => new Vector2(650f, 750f);

        public Dialog_PawnPsychology(Pawn pawn)
        {
            this.pawn = pawn;
            this.coreComp = pawn.TryGetComp<SynapseCorePawnComp>();
            this.forcePause = false;
            this.doCloseX = true;
            this.draggable = true;
            this.resizeable = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = false;
            this.preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (pawn == null || coreComp == null) return;

            // Draw header area
            Rect headerRect = new Rect(0f, 0f, inRect.width, 45f);
            Widgets.DrawWindowBackground(headerRect);
            
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;

            string fullName = pawn.LabelShort;
            if (pawn.Name is NameTriple nameTriple)
            {
                if (!string.IsNullOrEmpty(nameTriple.Nick))
                {
                    fullName = $"{nameTriple.First} \"{nameTriple.Nick}\" {nameTriple.Last}";
                }
                else
                {
                    fullName = $"{nameTriple.First} {nameTriple.Last}";
                }
            }
            else if (pawn.Name != null)
            {
                fullName = pawn.Name.ToStringFull;
            }

            string title = pawn.Dead ? "In Memoriam: " + fullName : fullName;
            Widgets.Label(headerRect, title);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            // Setup Tab space
            Rect tabRect = new Rect(0f, headerRect.yMax + 30f, inRect.width, inRect.height - headerRect.yMax - 30f);
            
            List<TabRecord> tabs = new List<TabRecord>();
            if (pawn.Dead)
            {
                tabs.Add(new TabRecord("Funeral Record", () => currentTab = PsychologyTab.Profile, currentTab == PsychologyTab.Profile));
            }
            else
            {
                tabs.Add(new TabRecord("Psychological Profile", () => currentTab = PsychologyTab.Profile, currentTab == PsychologyTab.Profile));
            }
            tabs.Add(new TabRecord("Memories", () => currentTab = PsychologyTab.Memories, currentTab == PsychologyTab.Memories));
            tabs.Add(new TabRecord("Social Network", () => currentTab = PsychologyTab.SocialNetwork, currentTab == PsychologyTab.SocialNetwork));
            
            TabDrawer.DrawTabs(tabRect, tabs, 200f);

            // Draw the inner content window
            Rect contentRect = tabRect.ContractedBy(18f);
            
            switch (currentTab)
            {
                case PsychologyTab.Profile:
                    if (pawn.Dead)
                        DrawFuneralTab(contentRect);
                    else
                        DrawProfileTab(contentRect);
                    break;
                case PsychologyTab.Memories:
                    DrawMemoriesTab(contentRect);
                    break;
                case PsychologyTab.SocialNetwork:
                    DrawSocialNetworkTab(contentRect);
                    break;
            }
        }
    }
}
