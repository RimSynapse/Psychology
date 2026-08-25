using System.Collections.Generic;
using System.Linq;

namespace RimSynapse.Psychology.Prompts
{
    /// <summary>
    /// PURE, Verse-free composer for the extreme mental-break prediction prompt: given a colonist's mood,
    /// traits and psychological burdens (and the candidate MentalStateDef defNames), it asks the model to pick
    /// the single break their psychology best justifies. Authored once here; the game-free Prompt Lab links
    /// this file, and <see cref="DefaultCandidates"/> is the single source of the candidate list shared with
    /// the game (SynapsePsychologyBreaks references it).
    /// </summary>
    public static class BreakPromptComposer
    {
        // The MentalStateDef defNames the model must choose from (Berserk etc.). Single-sourced here so the
        // game call site and the lab use the exact same list. (An invalid name silently no-ops the break.)
        public static readonly string[] DefaultCandidates =
            { "Berserk", "Slaughterer", "FireStartingSpree", "InsultingSpree", "Binging_Food", "Wander_Sad", "GiveUpExit" };

        /// <param name="moodFraction">Current mood as a 0..1 fraction (formatted P0, matching the game).</param>
        public static PsychologyPrompt Compose(string name, float moodFraction, string traits, string burdens,
            IReadOnlyList<string> candidates = null)
        {
            var list = (candidates != null && candidates.Count > 0) ? candidates : (IReadOnlyList<string>)DefaultCandidates;
            string system = $@"You are modelling a RimWorld colonist who has crossed their EXTREME mental-break threshold and is about to snap.
Choose the single mental break their psychology, traits and burdens best justify, from this exact list of RimWorld MentalStateDef defNames:
{string.Join(", ", list)}.
Respond ONLY as valid JSON, no markdown:
{{ ""BreakDefName"": ""<one defName from the list>"", ""Warning"": ""<1-2 sentence in-character warning of what is coming>"" }}";

            string user = $"Name: {name}\nMood: {moodFraction:P0} (below the extreme break threshold)\nTraits: {traits}\nPsychological burdens: {burdens}";
            return new PsychologyPrompt { system = system, user = user };
        }
    }
}
