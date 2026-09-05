namespace MagiCore;

internal static class MemoryBehaviorPrompts
{
    internal const string NormalExtraction = "Extract durable user facts from the conversation. Return only a JSON array of strings. Ignore greetings, questions, and temporary requests.";

    internal static string ForExtraction(MemoryAddOptions options)
    {
        var instruction = options.Behavior switch
        {
            MemoryBehavior.Normal => NormalExtraction,
            MemoryBehavior.Dreaming => "Process the conversation as dream-like memory consolidation. Extract durable themes, emotional patterns, and meaningful associations, including subtle connections that may not be explicit. Phrase uncertain or imaginative associations as possibilities rather than facts. Return only a JSON array of concise strings. Ignore greetings and temporary requests.",
            MemoryBehavior.RandomThoughts => "Generate concise, spontaneous thoughts associated with the conversation, favoring useful or surprising connections while keeping uncertainty explicit. Return only a JSON array of strings. Do not claim invented details as facts.",
            MemoryBehavior.PersonalMemory => "Shape memories from the agent's first-person perspective and personality. Capture what the agent noticed, concluded, or wants to remember about the interaction. Use first-person language. Return only a JSON array of concise strings. Do not invent events or user facts.",
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Behavior, "Unknown memory behavior.")
        };

        return string.IsNullOrWhiteSpace(options.Prompt)
            ? instruction
            : $"{instruction} Additional agent perspective or personality: {options.Prompt!.Trim()}";
    }

    internal static string ForConflictResolution(string normalPrompt, MemoryAddOptions options) =>
        options.Behavior == MemoryBehavior.Normal
            ? normalPrompt
            : $"{normalPrompt} Apply this memory behavior while forming each memory: {ForExtraction(options)}";
}