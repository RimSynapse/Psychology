using System.Linq;
using RimWorld;
using Verse;
using Newtonsoft.Json.Linq;
using RimSynapse.Comps;

namespace RimSynapse.Psychology.Comps
{
    /// <summary>
    /// Builds a pawn's Voice (Conversations#33): the prose speaking style Conversations consumes, plus
    /// the Kokoro TTS assignment (base voice by sex/gender + personality-driven speed/timbre) the
    /// 0.10+ TTS layer renders. Shared by the initial personality-profile path and the lazy backfill.
    /// Kokoro has no pitch knob, so timbre is expressed by blending toward a same-gender reference.
    /// </summary>
    public static class VoiceProfileBuilder
    {
        /// <summary>Assign/refresh a pawn's voice fields from parsed Voice descriptors. Idempotent;
        /// always leaves a valid base <c>kokoroVoice</c> and sets <c>voiceGenerated</c>.
        /// The optional <paramref name="sample"/> — one line the pawn might actually say — is appended to
        /// <c>voiceProfile</c>: the dialogue model imitates an example far more reliably than it interprets
        /// a description, so the sample is the strongest register anchor Conversations gets (#44).</summary>
        public static void ApplyVoiceProfile(Pawn pawn, SynapseCorePawnComp core, string style, string pace, string timbre, string sample = null)
        {
            if (core == null) return;
            if (!string.IsNullOrWhiteSpace(style))
            {
                string profile = style.Trim().TrimEnd('.');
                if (!string.IsNullOrWhiteSpace(sample))
                    profile = $"{profile}. Might say: «{sample.Trim().Trim('"')}»";
                core.voiceProfile = profile;
            }

            if (string.IsNullOrEmpty(core.kokoroVoice) || !KokoroVoices.IsKnown(core.kokoroVoice))
                core.kokoroVoice = KokoroVoices.RandomVoiceFor(pawn);

            core.kokoroSpeed = SpeedFromPace(pace);
            ApplyTimbreBlend(pawn, core, timbre);
            core.voiceGenerated = true;
        }

        public static float SpeedFromPace(string pace)
        {
            if (string.IsNullOrEmpty(pace)) return 1f;
            switch (pace.Trim().ToLowerInvariant())
            {
                case "slow": return 0.9f;
                case "fast": return 1.15f;
                default: return 1f; // measured / unknown
            }
        }

        // First-pass timbre → Kokoro blend (reserved; weights/targets get tuned when the 0.10+ render
        // lands). Blends the base voice toward a same-gender "firmer" or "softer" reference.
        private static void ApplyTimbreBlend(Pawn pawn, SynapseCorePawnComp core, string timbre)
        {
            core.kokoroBlendVoice = null;
            core.kokoroBlendWeight = 0f;
            if (string.IsNullOrEmpty(timbre)) return;

            bool male = pawn?.gender == Gender.Male;
            string firmer = male ? "am_onyx" : "af_kore";
            string softer = male ? "am_liam" : "af_nicole";

            switch (timbre.Trim().ToLowerInvariant())
            {
                case "gruff":
                case "clipped":
                    core.kokoroBlendVoice = firmer; core.kokoroBlendWeight = 0.25f; break;
                case "warm":
                case "breathy":
                case "bright":
                    core.kokoroBlendVoice = softer; core.kokoroBlendWeight = 0.25f; break;
                default:
                    break; // flat / unknown → no blend
            }

            // Never blend a voice with itself.
            if (core.kokoroBlendVoice == core.kokoroVoice) { core.kokoroBlendVoice = null; core.kokoroBlendWeight = 0f; }
        }

        /// <summary>Extract style/sample/pace/timbre from a parsed "Voice" JSON value (JObject).
        /// <c>sample</c> is absent from profiles generated before 0.9.2 — callers pass it through as null.</summary>
        public static bool TryReadVoice(object voiceObj, out string style, out string pace, out string timbre, out string sample)
        {
            style = pace = timbre = sample = null;
            if (voiceObj is JObject jo)
            {
                style = jo.Value<string>("style");
                pace = jo.Value<string>("pace");
                timbre = jo.Value<string>("timbre");
                sample = jo.Value<string>("sample");
                return true;
            }
            return false;
        }
    }
}
