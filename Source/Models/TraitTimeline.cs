using System.Collections.Generic;
using System.Linq;
using RimSynapse.Comps;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.Models
{
    /// <summary>One row of the Patient History timeline: an absolute-tick-stamped trait event.</summary>
    public struct TraitTimelineEntry
    {
        public long absTick;
        public string text;
    }

    /// <summary>
    /// Builds the merged Patient History timeline (#25): every AI-engine trait gain and loss — each with the
    /// reasoning captured at that end of the trait's life — merged with the therapy/desensitization
    /// "TraitShift" memories, oldest-first, exact duplicates dropped. Trait-engine records store game ticks,
    /// so they are lifted to absolute ticks to share one clock with the memories. Pulled out of the dialog so
    /// the merge/order/dedup contract is unit-testable without a live window (#25 test plan).
    /// </summary>
    public static class TraitTimeline
    {
        public static List<TraitTimelineEntry> Build(SynapseCorePawnComp coreComp, SynapsePawnComp pawnComp)
        {
            var rows = new List<TraitTimelineEntry>();

            if (pawnComp?.dynamicTraits != null)
            {
                foreach (var t in pawnComp.dynamicTraits)
                {
                    if (t.traitDef == null) continue;
                    string label = t.traitDef.LabelCap.ToString();
                    if (string.IsNullOrEmpty(label)) label = t.traitDef.label;
                    rows.Add(new TraitTimelineEntry
                    {
                        absTick = RimSynapse.Utils.SynapseDateHelper.GameTickToAbsTick(t.tickAdded),
                        text = $"Gained '{label}'" + (string.IsNullOrEmpty(t.reason) ? "" : $" — {t.reason}")
                    });
                    if (t.tickRemoved > 0)
                    {
                        rows.Add(new TraitTimelineEntry
                        {
                            absTick = RimSynapse.Utils.SynapseDateHelper.GameTickToAbsTick(t.tickRemoved),
                            text = $"Lost '{label}'" + (string.IsNullOrEmpty(t.removalReason) ? "" : $" — {t.removalReason}")
                        });
                    }
                }
            }

            if (coreComp?.memories != null)
            {
                foreach (var m in coreComp.memories)
                    if (m.tags != null && m.tags.Contains("TraitShift"))
                        rows.Add(new TraitTimelineEntry { absTick = m.absTick, text = m.summary });
            }

            var seen = new HashSet<string>();
            return rows.OrderBy(r => r.absTick)
                       .Where(r => seen.Add(r.absTick + "|" + r.text))
                       .ToList();
        }
    }
}
