# Mem0Sharp Graph Memory Visualizer

An interactive, zero-dependency **Vanilla HTML/CSS/JS Graph Memory Nodes Visualizer** for Mem0Sharp evaluation results and long-term memory benchmarks.

## Overview

The visualizer dynamically loads benchmark evaluation reports directly from [`evaluation/results/`](../results/), connecting:
1. **Questions** evaluated in LOCOMO-style benchmarks (Single-hop, Multi-hop, Temporal, Contradiction, Adversarial) with their judge verdicts, F1/BLEU metrics, and search latencies.
2. **Retrieved Memory Nodes** stored in vector collections across different extraction, conflict-resolution, and behavior pipelines.
3. **Knowledge Entities & Triples** extracted from multi-session conversation histories across 4 real-world domains (Enterprise Rollout, Family Care, Household & Travel, Cafe Operations).
4. **Reasoning Chains** showing multi-hop synthesis paths and temporal state updates.

## Getting Started

### Serve via Local Static Server

```powershell
# From repo root
python -m http.server 8080

# Or using Node
npx serve .
```

Then open `http://localhost:8080/evaluation/visualizer/index.html` in your browser. The visualizer will asynchronously fetch the latest evaluation report and render the graph instantly.

### Direct File Open & Drag-and-Drop

When opening `index.html` directly from disk (`file://`), you can:
- Select the JSON file directly with the built-in file picker from `evaluation/results/`.
- Drag and drop any `evaluation-*.json` file onto the browser window.

## Features

- **Dynamic File Referencing**: Directly references raw benchmark JSON files in `evaluation/results/` without data duplication.
- **4-Domain Persona Separation**: Displays distinct, uncluttered clusters for **Northstar Enterprise Rollout** (NW), **Family Care Coordination** (NE), **Household & Travel Planning** (SW), and **Neighborhood Cafe Operations** (SE) with dedicated centering physics and cluster hulls.
- **Multi-Perspective Views**:
  - **Retrieval & Memory Graph:** Interactive network linking evaluation questions $\leftrightarrow$ retrieved memory nodes $\leftrightarrow$ extracted entity hubs.
  - **Knowledge Graph:** Semantic Subject $\to$ Predicate $\to$ Object triples network (`Northstar Health` $\to$ `has_rollout_owner` $\to$ `Dana Cho`, `Roberto Alvarez` $\to$ `has_allergy` $\to$ `Penicillin Allergy`, etc.).
  - **Reasoning Chains:** Highlighted multi-hop reasoning synthesis pathways and temporal transition edges.
- **Interactive Physics Engine**: Smooth 60fps force-directed canvas simulation with pause/resume, reheat on toggle, node dragging, vertical zoom slider, and customizable repulsion/distance/gravity sliders.
- **Scenario Benchmark HUD**: Floating live metrics card displaying J-Score Accuracy (with Wilson 95% CI), Retrieval Hit Rate, Mean F1, Mean BLEU-1, Search Latency, and Memory Footprint for all 12 evaluation scenarios.
- **Rich Node Inspector**: Slide-over drawer with side-by-side Gold vs. Generated answer comparisons, LLM Judge reasoning, ranked memory list with hit badges, and connected entity links.
- **Dynamic Search & Filtering**: Instant search highlighting 1-hop and 2-hop neighborhoods; filter chips for Categories and Verdicts.
- **Export Capabilities**: One-click PNG snapshot export and Graph JSON export.
