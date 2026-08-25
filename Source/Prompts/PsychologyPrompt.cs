namespace RimSynapse.Psychology.Prompts
{
    /// <summary>A system+user message pair for a Psychology LLM call. Splitting them matters: a chat
    /// completion with only a system message makes a local model return an empty acknowledgement instead of
    /// generating — the concrete subject has to arrive as the USER message.</summary>
    public struct PsychologyPrompt
    {
        public string system;
        public string user;
    }
}
