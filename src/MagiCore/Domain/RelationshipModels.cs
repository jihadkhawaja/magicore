namespace MagiCore;

public enum EntityType
{
    Person,
    Organization,
    Location,
    Concept,
    Other
}

public sealed record ExtractedEntity(string Text, EntityType Type = EntityType.Other);

public sealed record MemoryEntity(string Id, string Text, EntityType Type, IReadOnlyCollection<string> LinkedMemoryIds);

public sealed record ExtractedRelation(string Source, string Relationship, string Target);

public sealed record MemoryRelation(string Id, string Source, string Relationship, string Target, string MemoryId);