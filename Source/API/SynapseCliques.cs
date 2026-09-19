using System.Collections.Generic;
using System.Linq;
using Verse;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Colony cliques (#20, re-scoped): emergent friend-groups derived from the #72 relationship compass. Two
    /// colonists are linked when they are MUTUALLY warm + familiar (both directions), and a clique is a connected
    /// cluster of ≥2 such links. Purely DERIVED from the already-scribed social network, so it needs no state of
    /// its own and is save/load-safe by construction. Detected cliques are woven into social-flavoured LLM prompts
    /// via the Core context hook, so dialogue and evaluations become socially aware. Feeds the #24 social graph.
    ///
    /// The rumor mill is deliberately NOT here — Conversations #60 owns local gossip and Factions #17 owns global
    /// spread; this consumes/coordinates rather than duplicating them.
    /// </summary>
    public static class SynapseCliques
    {
        public const float CliqueWarmth = 25f;       // liking floor for a clique link
        public const float CliqueFamiliarity = 40f;  // must actually know each other

        private const int CacheTtlTicks = 2500;       // recompute at most ~hourly
        private static Map cachedMap;
        private static int cachedTick = int.MinValue;
        private static List<List<Pawn>> cachedGroups = new List<List<Pawn>>();

        /// <summary>Whether <paramref name="from"/> feels close enough TO <paramref name="to"/> for a clique link
        /// (one direction).</summary>
        public static bool CloseToward(Pawn from, Pawn to)
        {
            var comp = from?.GetComp<SynapsePawnComp>();
            if (comp?.socialNetwork == null || to == null) return false;
            return comp.socialNetwork.TryGetValue(to.GetUniqueLoadID(), out var rec)
                   && rec.warmth >= CliqueWarmth && rec.familiarity >= CliqueFamiliarity;
        }

        /// <summary>A clique link requires the closeness to be MUTUAL — a one-sided crush is not a friend-group.</summary>
        public static bool MutuallyClose(Pawn a, Pawn b) => a != b && CloseToward(a, b) && CloseToward(b, a);

        /// <summary>Detect the friend-groups among the given pawns: connected clusters of mutual-close links,
        /// size ≥ 2. Pure — no map/tick dependency — so it is directly unit-testable.</summary>
        public static List<List<Pawn>> Detect(IList<Pawn> pawns)
        {
            var groups = new List<List<Pawn>>();
            if (pawns == null || pawns.Count < 2) return groups;

            // Union-find over the pawn indices.
            int n = pawns.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (MutuallyClose(pawns[i], pawns[j])) Union(i, j);

            var byRoot = new Dictionary<int, List<Pawn>>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(i);
                if (!byRoot.TryGetValue(r, out var list)) { list = new List<Pawn>(); byRoot[r] = list; }
                list.Add(pawns[i]);
            }
            foreach (var g in byRoot.Values)
                if (g.Count >= 2) groups.Add(g);
            return groups;
        }

        /// <summary>The map's current cliques, recomputed at most ~hourly (or when forced). Derived on demand from
        /// the live social network, so nothing is persisted.</summary>
        public static List<List<Pawn>> ForMap(Map map, bool force = false)
        {
            if (map == null) return new List<List<Pawn>>();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!force && map == cachedMap && now - cachedTick < CacheTtlTicks) return cachedGroups;

            cachedGroups = Detect(map.mapPawns?.FreeColonists?.ToList());
            cachedMap = map;
            cachedTick = now;
            return cachedGroups;
        }

        /// <summary>The clique containing <paramref name="pawn"/> (including the pawn), or empty if it is in none.</summary>
        public static List<Pawn> CliqueOf(Pawn pawn)
        {
            if (pawn?.Map == null) return new List<Pawn>();
            foreach (var g in ForMap(pawn.Map))
                if (g.Contains(pawn)) return g;
            return new List<Pawn>();
        }

        /// <summary>A one-line clique note for a pawn's prompt context ("Close social circle: B, C"), or "" if the
        /// pawn is not in a clique.</summary>
        public static string CliqueSummaryFor(Pawn pawn)
        {
            var g = CliqueOf(pawn);
            if (g.Count < 2) return "";
            var others = g.Where(p => p != pawn).Select(p => p.LabelShort);
            return "Close social circle: " + string.Join(", ", others);
        }

        /// <summary>Core context-injection handler (registered at startup): weave the pawn's clique into the
        /// social-flavoured generation stages so dialogue and evaluations are socially aware.</summary>
        public static void InjectCliqueContext(Pawn pawn, string contextType, List<string> extra)
        {
            if (pawn == null || extra == null) return;
            if (contextType != RimSynapse.SynapseContextTypes.RelationshipEvaluation
                && contextType != RimSynapse.SynapseContextTypes.DailyReview
                && contextType != RimSynapse.SynapseContextTypes.PersonalityProfile)
                return;

            string s = CliqueSummaryFor(pawn);
            if (!string.IsNullOrEmpty(s)) extra.Add(s);
        }
    }

    /// <summary>Registers clique context injection into the Core hook once, at game load.</summary>
    [StaticConstructorOnStartup]
    public static class SynapseCliquesStartup
    {
        static SynapseCliquesStartup()
        {
            RimSynapse.SynapseCoreContext.OnInjectGenericContext += SynapseCliques.InjectCliqueContext;
        }
    }
}
