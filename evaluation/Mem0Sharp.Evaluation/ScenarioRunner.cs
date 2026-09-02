using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace Mem0Sharp.Evaluation;

/// <summary>
/// Runs one scenario: creates an in-memory VectorData collection, ingests all conversations with the
/// scenario's add options, then searches, answers, and judges every question.
/// </summary>
internal sealed class ScenarioRunner(
    EvaluationConfiguration configuration,
    ScenarioDefinition scenario,
    IChatClient? chatClient,
    IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator,
    LlmEvalHelper? llmHelper,
    bool retrievalOnly,
    EvaluationDatasetSnapshot dataset)
{
    private readonly int topK = Math.Max(1, configuration.Evaluation.TopK);

    public async Task<ScenarioReport> RunAsync(CancellationToken cancellationToken)
    {
        var ingestWatch = Stopwatch.StartNew();
        var memoriesStored = 0;

        var store = VectorDataMemoryStore.CreateInMemory(new VectorDataMemoryStoreOptions
        {
            CollectionName = scenario.TableName
        });
        await store.InitializeAsync(cancellationToken);
        await store.ResetAsync(cancellationToken);

        var memory = new MemoryService(
            store: store,
            embeddings: retrievalOnly ? new LocalEmbeddingGenerator(1536) : embeddingGenerator!,
            extractor: retrievalOnly ? new BasicMemoryExtractor() : new LlmMemoryExtractor(chatClient!),
            reranker: scenario.Rerank && !retrievalOnly ? new LlmReranker(chatClient!) : null,
            conflictResolver: scenario.UseConflictResolver && !retrievalOnly ? new LlmMemoryConflictResolver(chatClient!) : null);

        foreach (var conversation in dataset.Conversations)
        {
            var userId = $"{scenario.Name}_{conversation.Id}";

            foreach (var session in conversation.Sessions)
            {
                var sessionMessages = session.Turns
                    .Select(turn => new Message(turn.Speaker, turn.Text))
                    .ToArray();

                var addOptions = new MemoryAddOptions
                {
                    UserId = userId,
                    Infer = scenario.Infer,
                    Deduplicate = scenario.Deduplicate,
                    Behavior = scenario.Behavior,
                    Prompt = scenario.BehaviorPersona,
                    ReferenceTime = scenario.UseSessionReferenceTime ? ParseSessionDate(session.Date) : null,
                    Metadata = new Dictionary<string, string>
                    {
                        ["session_date"] = session.Date
                    }
                };

                var addResult = await memory.AddAsync(sessionMessages, addOptions, cancellationToken);
                memoriesStored += addResult.Memories.Count;
            }

            if (scenario.ForgetStaleAfterDays is > 0)
            {
                await memory.ForgetStaleAsync(TimeSpan.FromDays(scenario.ForgetStaleAfterDays.Value), new MemoryFilter(UserId: userId), cancellationToken);
            }
        }

        ingestWatch.Stop();

        var questions = dataset.Questions;
        var results = new QuestionResult[questions.Count];
        var gate = new SemaphoreSlim(Math.Max(1, configuration.Evaluation.Concurrency));
        var workers = questions.Select(async (question, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var userId = $"{scenario.Name}_{question.ConversationId}";
                var searchWatch = Stopwatch.StartNew();
                var searchResults = await memory.SearchAsync(
                    question.Question,
                    new MemorySearchOptions
                    {
                        Filter = new MemoryFilter(UserId: userId),
                        TopK = topK,
                        Threshold = scenario.Threshold,
                        Hybrid = scenario.Hybrid,
                        Rerank = scenario.Rerank && !retrievalOnly,
                        RecencyBias = scenario.RecencyBias,
                        FreshnessWindow = scenario.FreshnessWindowDays is null ? null : TimeSpan.FromDays(scenario.FreshnessWindowDays.Value),
                        Behavior = scenario.Behavior,
                        EnableTemporalSearch = scenario.EnableTemporalSearch,
                        IncludeUndatedMemories = scenario.IncludeUndatedMemories
                    },
                    cancellationToken);
                searchWatch.Stop();

                var result = await EvaluateQuestionAsync(question, searchResults, searchWatch, cancellationToken);
                results[index] = result;
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(workers);

        return BuildReport(memoriesStored, ingestWatch.Elapsed, results);
    }

    private static DateTimeOffset ParseSessionDate(string value)
    {
        if (DateTimeOffset.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var referenceTime))
        {
            return referenceTime;
        }
        throw new InvalidDataException($"Session date '{value}' must use yyyy-MM-dd format for event-time evaluation.");
    }

    private async Task<QuestionResult> EvaluateQuestionAsync(
        EvalQuestion question,
        IReadOnlyList<SearchResult> searchResults,
        Stopwatch searchWatch,
        CancellationToken cancellationToken)
    {
        var retrievedTexts = searchResults.Select(r => r.Memory.Text).ToArray();
        var retrievalHit = question.IsAdversarial
            ? searchResults.Count == 0
            : question.Evidence.Count == 0 || question.Evidence.Any(evidence => retrievedTexts.Any(text => text.Contains(evidence, StringComparison.OrdinalIgnoreCase)));

        if (retrievalOnly || llmHelper is null)
        {
            return new QuestionResult
            {
                QuestionId = question.Id,
                Category = question.Category,
                Question = question.Question,
                ExpectedAnswer = question.ExpectedAnswer,
                RetrievalHit = retrievalHit,
                RetrievedCount = searchResults.Count,
                SearchLatencyMs = searchWatch.Elapsed.TotalMilliseconds,
                RetrievedMemories = retrievedTexts
            };
        }

        var generatedAnswer = await llmHelper.GenerateAnswerAsync(question.Question, searchResults, cancellationToken);
        var outcome = await llmHelper.JudgeAsync(question.Question, question.ExpectedAnswer, generatedAnswer, cancellationToken);

        return new QuestionResult
        {
            QuestionId = question.Id,
            Category = question.Category,
            Question = question.Question,
            ExpectedAnswer = question.ExpectedAnswer,
            GeneratedAnswer = generatedAnswer,
            JudgeVerdict = outcome.Correct ? "CORRECT" : "WRONG",
            JudgeReasoning = outcome.Reasoning,
            Correct = outcome.Correct,
            F1 = outcome.F1,
            Bleu1 = outcome.Bleu1,
            RetrievalHit = retrievalHit,
            RetrievedCount = searchResults.Count,
            SearchLatencyMs = searchWatch.Elapsed.TotalMilliseconds,
            RetrievedMemories = retrievedTexts
        };
    }

    private ScenarioReport BuildReport(int memoriesStored, TimeSpan ingestElapsed, IReadOnlyList<QuestionResult> results)
    {
        var judged = results.Where(result => result.Correct.HasValue).ToArray();
        var answerable = results.Where(result => result.Category != EvaluationDataset.CategoryAdversarial).ToArray();

        var categories = dataset.Categories.Select(category =>
        {
            var inCategory = results.Where(result => result.Category == category).ToArray();
            var judgedInCategory = inCategory.Where(result => result.Correct.HasValue).ToArray();
            var answerableInCategory = category == EvaluationDataset.CategoryAdversarial ? [] : inCategory;
            return new CategoryMetrics
            {
                Category = category,
                Questions = judgedInCategory.Length,
                Correct = judgedInCategory.Count(result => result.Correct == true),
                AccuracyLower95 = judgedInCategory.Length == 0 ? null : WilsonInterval(judgedInCategory.Count(result => result.Correct == true), judgedInCategory.Length).Lower,
                AccuracyUpper95 = judgedInCategory.Length == 0 ? null : WilsonInterval(judgedInCategory.Count(result => result.Correct == true), judgedInCategory.Length).Upper,
                RetrievalQuestions = answerableInCategory.Length,
                RetrievalHits = answerableInCategory.Count(result => result.RetrievalHit),
                RetrievalHitRate = answerableInCategory.Length == 0
                    ? 0
                    : (double)answerableInCategory.Count(result => result.RetrievalHit) / answerableInCategory.Length
            };
        }).ToArray();

        return new ScenarioReport
        {
            Name = scenario.Name,
            Description = scenario.Description,
            MemoriesStored = memoriesStored,
            IngestSeconds = ingestElapsed.TotalSeconds,
            Questions = results.Count,
            Judged = judged.Length,
            Correct = judged.Count(result => result.Correct == true),
            AccuracyLower95 = judged.Length == 0 ? null : WilsonInterval(judged.Count(result => result.Correct == true), judged.Length).Lower,
            AccuracyUpper95 = judged.Length == 0 ? null : WilsonInterval(judged.Count(result => result.Correct == true), judged.Length).Upper,
            MeanF1 = judged.Length == 0 ? null : judged.Average(result => result.F1 ?? 0),
            MeanBleu1 = judged.Length == 0 ? null : judged.Average(result => result.Bleu1 ?? 0),
            RetrievalQuestions = answerable.Length,
            RetrievalHits = answerable.Count(result => result.RetrievalHit),
            RetrievalHitRate = answerable.Length == 0
                ? 0
                : (double)answerable.Count(result => result.RetrievalHit) / answerable.Length,
            RetrievalHitRateLower95 = answerable.Length == 0 ? null : WilsonInterval(answerable.Count(result => result.RetrievalHit), answerable.Length).Lower,
            RetrievalHitRateUpper95 = answerable.Length == 0 ? null : WilsonInterval(answerable.Count(result => result.RetrievalHit), answerable.Length).Upper,
            MeanRetrievedCount = results.Count == 0 ? 0 : results.Average(result => result.RetrievedCount),
            MeanSearchLatencyMs = results.Count == 0 ? 0 : results.Average(result => result.SearchLatencyMs),
            Categories = categories,
            Results = results
        };
    }

    private static (double Lower, double Upper) WilsonInterval(int successes, int trials)
    {
        const double z = 1.96;
        var sampleSize = (double)trials;
        var proportion = (double)successes / trials;
        var denominator = 1 + z * z / sampleSize;
        var center = (proportion + z * z / (2 * sampleSize)) / denominator;
        var margin = z / denominator * Math.Sqrt(proportion * (1 - proportion) / sampleSize + z * z / (4 * sampleSize * sampleSize));
        return (Math.Max(0, center - margin), Math.Min(1, center + margin));
    }
}
