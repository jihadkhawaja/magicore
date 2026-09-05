using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed partial class MemoryService : IMemoryService
{
    private readonly IMemoryStore store;
    private readonly IEmbeddingGenerator<string, Embedding<float>> embeddings;
    private readonly IEmbeddingGenerator<DataContent, Embedding<float>>? imageEmbeddings;
    private readonly IMemoryExtractor extractor;
    private readonly IMemoryReranker? reranker;
    private readonly IMemoryConflictResolver? conflictResolver;
    private readonly IProceduralMemoryGenerator? proceduralMemoryGenerator;
    private readonly IEntityExtractor entityExtractor;
    private readonly IEntityStore entityStore;
    private readonly IGraphMemoryExtractor? graphExtractor;
    private readonly IGraphMemoryStore? graphStore;
    private readonly IAdmissionGate? admissionGate;
    private readonly IConsolidationVerifier? consolidationVerifier;
    private readonly ITrajectoryStore trajectoryStore;
    private readonly ITemporalQueryInterpreter temporalQueryInterpreter;
    private readonly MemoryOptions options;
    private readonly Dictionary<string, float[]> vectors = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim indexLock = new(1, 1);

    public MemoryService(
        IMemoryStore? store = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddings = null,
        IMemoryExtractor? extractor = null,
        MemoryOptions? options = null,
        IMemoryReranker? reranker = null,
        IMemoryConflictResolver? conflictResolver = null,
        IProceduralMemoryGenerator? proceduralMemoryGenerator = null,
        IEntityExtractor? entityExtractor = null,
        IEntityStore? entityStore = null,
        IGraphMemoryExtractor? graphExtractor = null,
        IGraphMemoryStore? graphStore = null,
        IAdmissionGate? admissionGate = null,
        IConsolidationVerifier? consolidationVerifier = null,
        ITrajectoryStore? trajectoryStore = null,
        IEmbeddingGenerator<DataContent, Embedding<float>>? imageEmbeddings = null)
        : this(
            new DeterministicTemporalQueryInterpreter(),
            store,
            embeddings,
            extractor,
            options,
            reranker,
            conflictResolver,
            proceduralMemoryGenerator,
            entityExtractor,
            entityStore,
            graphExtractor,
            graphStore,
            admissionGate,
            consolidationVerifier,
            trajectoryStore,
            imageEmbeddings)
    {
    }

    public MemoryService(
        ITemporalQueryInterpreter temporalQueryInterpreter,
        IMemoryStore? store = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddings = null,
        IMemoryExtractor? extractor = null,
        MemoryOptions? options = null,
        IMemoryReranker? reranker = null,
        IMemoryConflictResolver? conflictResolver = null,
        IProceduralMemoryGenerator? proceduralMemoryGenerator = null,
        IEntityExtractor? entityExtractor = null,
        IEntityStore? entityStore = null,
        IGraphMemoryExtractor? graphExtractor = null,
        IGraphMemoryStore? graphStore = null,
        IAdmissionGate? admissionGate = null,
        IConsolidationVerifier? consolidationVerifier = null,
        ITrajectoryStore? trajectoryStore = null,
        IEmbeddingGenerator<DataContent, Embedding<float>>? imageEmbeddings = null)
    {
        Guard.NotNull(temporalQueryInterpreter);
        this.store = store ?? new InMemoryStore();
        this.embeddings = embeddings ?? new LocalEmbeddingGenerator();
        this.extractor = extractor ?? new BasicMemoryExtractor();
        this.options = options ?? new MemoryOptions();
        this.reranker = reranker;
        this.conflictResolver = conflictResolver;
        this.proceduralMemoryGenerator = proceduralMemoryGenerator;
        this.entityExtractor = entityExtractor ?? new RuleBasedEntityExtractor();
        this.entityStore = entityStore ?? new InMemoryEntityStore();
        this.graphExtractor = graphExtractor;
        this.graphStore = graphStore;
        this.admissionGate = admissionGate;
        this.consolidationVerifier = consolidationVerifier;
        this.trajectoryStore = trajectoryStore ?? new InMemoryTrajectoryStore();
        this.imageEmbeddings = imageEmbeddings;
        this.temporalQueryInterpreter = temporalQueryInterpreter;
    }

}