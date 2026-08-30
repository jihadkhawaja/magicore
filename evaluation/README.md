# Mem0Sharp evaluation harness

This console application evaluates Mem0Sharp in two complementary layers:

1. A deterministic capability suite exercises public memory features and records pass, fail, or skip evidence.
2. A memory-quality matrix follows an ingest -> search -> answer -> judge pipeline across extraction and retrieval configurations.

Each quality scenario gets an isolated in-memory `VectorDataMemoryStore` collection. The default corpus is self-contained and fictional, so no database or dataset download is required.

## What it measures

The default [`evaldataset.realworld.json`](Mem0Sharp.Evaluation/evaldataset.realworld.json) corpus models enterprise rollout, family care coordination, household and travel planning, and small-business operations. It includes corrections, preference drift, delayed plans, distractors, similar names, negation, sensitive-information boundaries, and facts spread across sessions.

| Conversations | Sessions | Turns | Questions | Categories |
| ---: | ---: | ---: | ---: | --- |
| 4 | 20 | 120 | 40 | single-hop, multi-hop, temporal, contradiction, adversarial |

| Category | Questions | What it tests |
| --- | ---: | --- |
| Single-hop | 12 | Recalling one stated fact among realistic distractors |
| Multi-hop | 8 | Combining evidence distributed across sessions |
| Temporal | 8 | Reconstructing changes to plans, ownership, and preferences |
| Contradiction | 4 | Preferring corrected/current facts over stale or similar facts |
| Adversarial | 8 | Declining requests for absent, sensitive, or invented details |

Metrics per quality scenario:

- **Accuracy (J-score)**: share of answers judged correct against the reference answer.
- **Mean F1 / BLEU-1**: token-overlap answer-quality metrics.
- **Retrieval hit rate**: share of answerable questions with expected evidence in retrieved memories.
- **Memories stored**, **mean search latency**, and **ingest time**.
- **Wilson 95% intervals** for accuracy and retrieval hit rate.

The capability suite covers CRUD, raw and conversation ingestion, batching, deduplication, identity scopes, nested metadata filters, paging, bulk deletion, hybrid and batch search, score explanations, expiration, retention, temporal reads, rollback, consolidation verification, behavior retrieval policy, conflict actions, procedural memory, entity and graph lifecycle, admission gates, deferred trajectory extraction, image memory, and reset semantics.

Provider-specific integrations that cannot be validated without credentials, local models, external processes, or databases are reported as **SKIP** with a reason. A skip is not treated as a pass.

## Scenarios

`--list` prints the current matrix.

| Scenario | What it varies |
| --- | --- |
| `baseline` | LLM extraction, hybrid search, and deduplication |
| `realistic-long-haul` | Recency bias and a 90-day freshness window |
| `stale-forget` | Retention pruning, recency bias, and a 60-day freshness window |
| `no-hybrid` | Semantic vector search only |
| `llm-rerank` | Adds `LlmReranker` |
| `conflict-resolution` | Adds structured ADD/UPDATE/DELETE/NONE decisions |
| `no-dedup` | Disables deduplication |
| `infer-off` | Stores raw messages without LLM extraction |
| `strict-threshold` | Raises the search threshold to 0.3 |
| `behavior-dreaming` | Dreaming behavior |
| `behavior-random-thoughts` | Random-thoughts behavior |
| `behavior-personal-memory` | Persona-shaped first-person memory |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An OpenAI-compatible API key for the live quality matrix

`--self-test` and `--validate-dataset` do not require credentials or Docker.

## Configure live mode

```powershell
Copy-Item .\evaluation\Mem0Sharp.Evaluation\evalconfig.example.yaml .\evaluation\Mem0Sharp.Evaluation\evalconfig.local.yaml
```

Add the API key to `evalconfig.local.yaml`, which is ignored by Git. The judge defaults to the chat model; configure a separate model for independent judging when appropriate. Embedding dimensions must match the selected embedding model.

## Run

```powershell
# Deterministic feature checks plus retrieval-only quality check
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --self-test

# Full capability and model-judged quality matrix
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj

# A subset of quality scenarios
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --scenario baseline,llm-rerank

# Validate the bundled default dataset
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --validate-dataset

# Validate or run a custom dataset using evaldataset.realworld.json as the schema
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --dataset .\path\to\dataset.json --validate-dataset
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --dataset .\path\to\dataset.json

# List scenarios
dotnet run --project .\evaluation\Mem0Sharp.Evaluation\Mem0Sharp.Evaluation.csproj -- --list
```

Reports are written as JSON and Markdown to the configured `resultsDirectory`. With the example configuration, this is `results/` relative to the working directory. Each scenario uses its own reset in-memory VectorData collection and scenario-scoped user IDs.

## Interactive graph memory visualizer

Open [`visualizer/index.html`](visualizer/index.html) in a browser or read the [visualizer guide](visualizer/README.md). It can load evaluation JSON reports, compare scenarios and question outcomes, inspect retrieved memories, and explore knowledge-graph views.

## Cost and interpretation

- A full default run can make more than 1,000 model calls. Use `--scenario` while iterating.
- LLM extraction and judging are not deterministic; rerun before interpreting small differences.
- The fixed fictional corpus supports regression and comparative evaluation, not claims of production performance.
- Wilson intervals describe question-sampling uncertainty only; they do not capture model, provider, prompt, or run-to-run variance.
