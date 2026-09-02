# Raw evaluation reports

This folder holds committed raw reports from evaluation runs (Markdown + JSON), copied from the build-output `results/` folder after a run. They back the summary tables in [docs/evaluation.md](../../docs/evaluation.md) and let reviewers inspect per-question judgments, retrieved memories, and latencies without rerunning the matrix.

Naming: `evaluation-<yyyyMMdd>-<HHmmss>.{md,json}` (UTC, from the run start).

Current published run:
- [evaluation-20260902-150733.md](evaluation-20260902-150733.md)
- [evaluation-20260902-150733.json](evaluation-20260902-150733.json)

When you publish a new run, copy both files here and update the "Raw report" link and run date in [docs/evaluation.md](../../docs/evaluation.md). Fresh runs always write to `evaluation/Mem0Sharp.Evaluation/bin/<configuration>/net10.0/results/`, which stays gitignored.
