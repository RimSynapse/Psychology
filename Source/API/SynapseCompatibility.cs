using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Comps;

namespace RimSynapse.Psychology.API
{
    /// <summary>
    /// Personality compatibility (#72): how two pawns' CHARACTERS dispose them toward each other, before any
    /// shared history. Clashing values grate, shared ones click. It is recomputed LIVE from current traits, so
    /// as the trait engine shifts a pawn over time their compatibility shifts too — nothing is frozen at first
    /// meeting. Familiarity then modulates how much a clash actually bites: a bond you value desensitises you to
    /// the other's flaws, a bond you resent hypersensitises you to them. The LLM relationship eval reconsiders
    /// this on top and supplies the "why".
    /// </summary>
    public static class SynapseCompatibility
    {
        /// <summary>Warmth points drifted per evaluation at full (+1/-1) compatibility.</summary>
        public const float DriftRate = 3f;
        /// <summary>At max familiarity, a valued bond dampens a clash by up to this fraction (you overlook it).</summary>
        public const float DesensitiseMax = 0.6f;
        /// <summary>At max familiarity, a resented bond amplifies a clash by up to this fraction (it grates more).</summary>
        public const float HypersensitiseMax = 1.0f;

        // Symmetric CLASHES: if one pawn has the first trait and the other has the second (either way round),
        // their characters grate. Each contributes a negative to the compatibility sum.
        private static readonly (string, string)[] ClashPairs =
        {
            ("Kind", "Abrasive"), ("Kind", "Bloodlust"), ("Kind", "Psychopath"), ("Kind", "Cannibal"),
            ("Ascetic", "Greedy"), ("Greedy", "Jealous"),
        };
        // Sharing one of these "character" traits is a bond of shared worldview (+).
        private static readonly string[] KinshipTraits = { "Kind", "Ascetic", "Bloodlust", "Nudist", "Cannibal" };
        // Sharing one of these makes two people rub each other the wrong way even though it's the same trait (-).
        private static readonly string[] SameTraitClash = { "Abrasive", "Greedy", "Jealous" };

        private const float ClashWeight = 0.35f;
        private const float KinWeight = 0.30f;
        private const float MoodKinWeight = 0.20f;
        private const float PassionKinWeight = 0.25f;

        /// <summary>
        /// Compatibility of A and B from current traits, in [-1, 1]: negative = clash, positive = kinship.
        /// Symmetric; recomputed live so trait shifts move it. <paramref name="reasons"/>, if given, is filled
        /// with short human-readable contributors for the debug/UI breakdown.
        /// </summary>
        public static float Score(Pawn a, Pawn b, List<string> reasons = null)
        {
            var ta = a?.story?.traits;
            var tb = b?.story?.traits;
            if (ta == null || tb == null) return 0f;
            float score = 0f;

            foreach (var (x, y) in ClashPairs)
            {
                var dx = Def(x); var dy = Def(y);
                if (dx == null || dy == null) continue;
                if ((ta.HasTrait(dx) && tb.HasTrait(dy)) || (ta.HasTrait(dy) && tb.HasTrait(dx)))
                { score -= ClashWeight; reasons?.Add($"-{x}/{y} clash"); }
            }
            foreach (var t in KinshipTraits)
            {
                var d = Def(t);
                if (d != null && ta.HasTrait(d) && tb.HasTrait(d)) { score += KinWeight; reasons?.Add($"+both {t}"); }
            }
            foreach (var t in SameTraitClash)
            {
                var d = Def(t);
                if (d != null && ta.HasTrait(d) && tb.HasTrait(d)) { score -= ClashWeight; reasons?.Add($"-two {t}s rub"); }
            }

            // Shared outlook: both lean the same way on the mood axis.
            int moodA = ta.DegreeOfTrait(Def("NaturalMood"));
            int moodB = tb.DegreeOfTrait(Def("NaturalMood"));
            if (moodA != 0 && System.Math.Sign(moodA) == System.Math.Sign(moodB))
            { score += MoodKinWeight; reasons?.Add("+shared outlook"); }

            // A shared strong passion — they love the same craft.
            if (SharesStrongPassion(a, b)) { score += PassionKinWeight; reasons?.Add("+shared passion"); }

            return Clamp(score, -1f, 1f);
        }

        /// <summary>
        /// The warmth pull for A→B this evaluation: compatibility × <see cref="DriftRate"/>, with a clash
        /// DESENSITISED when A currently likes B (high familiarity dampens it) or HYPERSENSITISED when A dislikes
        /// B (high familiarity amplifies it). Kinship pulls aren't modulated — liking someone compatible is steady.
        /// Pure and unit-testable.
        /// </summary>
        public static float EffectivePull(float compatScore, float familiarity, float currentWarmth)
        {
            float pull = Clamp(compatScore, -1f, 1f) * DriftRate;
            if (compatScore < 0f)
            {
                float fam = Clamp(familiarity / 100f, 0f, 1f);
                pull *= currentWarmth >= 0f ? (1f - fam * DesensitiseMax) : (1f + fam * HypersensitiseMax);
            }
            return pull;
        }

        /// <summary>
        /// Once per daily pass, drift a pawn's warmth toward every colonist they know by whatever their
        /// personalities currently imply — recomputed live (so it tracks trait shifts) and modulated by
        /// familiarity/valence. This is the continuous "do these two still fit?" pull; the LLM eval reasons on
        /// top of it. Directed: only <paramref name="pawn"/>'s side moves, from <paramref name="pawn"/>'s view.
        /// </summary>
        public static void ApplyDailyDrift(Pawn pawn, SynapsePawnComp comp)
        {
            if (pawn?.Map == null || comp?.socialNetwork == null || comp.socialNetwork.Count == 0) return;
            var map = pawn.Map;
            foreach (var kv in comp.socialNetwork)
            {
                var rec = kv.Value;
                if (rec == null) continue;
                var other = map.mapPawns.AllPawns.FirstOrDefault(x => x.GetUniqueLoadID() == kv.Key);
                if (other == null || other == pawn) continue;
                float compat = Score(pawn, other);
                if (compat == 0f) continue;
                rec.AddWarmth(EffectivePull(compat, rec.familiarity, rec.warmth));
            }
        }

        private static bool SharesStrongPassion(Pawn a, Pawn b)
        {
            var sa = a?.skills?.skills; var sb = b?.skills?.skills;
            if (sa == null || sb == null) return false;
            foreach (var ra in sa)
            {
                if (ra == null || (int)ra.passion < (int)Passion.Major) continue;
                foreach (var rb in sb)
                    if (rb?.def == ra.def && (int)rb.passion >= (int)Passion.Major) return true;
            }
            return false;
        }

        private static readonly Dictionary<string, TraitDef> _cache = new Dictionary<string, TraitDef>();
        private static TraitDef Def(string name)
        {
            if (name == null) return null;
            if (!_cache.TryGetValue(name, out var d)) { d = DefDatabase<TraitDef>.GetNamedSilentFail(name); _cache[name] = d; }
            return d;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
