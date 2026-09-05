# Robotics long-term memory

Mem0Sharp can supply the persistent, queryable evidence layer between a robot's perception system and mission planner. It does not replace neural perception, SLAM, collision checking, motion generation, or a safety-rated controller. The implemented APIs are a research-informed prototype for object memory and action history, not a reproduction of Flexion Reflect or the papers below.

## Research basis

The following sources were inspected for their methods and limitations, not just their headlines:

| Source | Relevant finding | Applied here | Not implemented here |
| --- | --- | --- | --- |
| [Flexion Reflect v1.0](https://flexion.ai/news/flexion-reflect-v1.0), sections 2-5 | A semantic-map tool supports mission reasoning; live perception and controller feedback remain necessary during execution. | Structured object recall plus current camera input and measured action feedback. | Flexion's VLM training, RL policies, perception system, global planner and whole-body controller. |
| [VLMaps, Huang et al., 2022](https://arxiv.org/html/2210.05714v2), sections III and V | Visual-language features must be grounded in measured geometry. Reconstruction noise, odometry drift and similar objects remain limitations. | Capture-time metric points, explicit coordinate-frame revisions, uncertainty and conservative identity association. | Dense CLIP/LSeg feature maps, automatic camera-pose estimation and cross-embodiment obstacle maps. |
| [Hydra, Hughes et al., RSS 2022](https://arxiv.org/html/2201.13360v2), sections III-V | Objects, places and rooms support different reasoning scales; persistent maps must accommodate corrections and separate fast/slow processing. | A compact object-level recall view over retained evidence, exact frame isolation, and memory outside the physics controller. | Hierarchical room/place extraction, ESDFs, loop closure, deformation-graph optimization or constant-time updates. |
| [ConceptGraphs, Gu et al., 2023](https://arxiv.org/html/2309.16650v1), sections II, III-E/G/H and appendix A5 | Multi-view geometric/semantic association produces object-centric maps; object search must verify remembered locations. Caption errors and duplicates affect planning. | Stable externally supplied identities, conservative fallback association, measured relations, explicit absence/occlusion and fresh-sensing requests. | SAM/CLIP fusion, point-cloud reconstruction, probabilistic re-identification or LLM-inferred physical affordances. |
| [SayCan, Ahn et al., 2022, official technical project description](https://say-can.github.io/) | Language plausibility alone does not establish that an action is feasible; physical grounding is needed. | Persisted controller-reported attempts supplied to the next decision. | Learned value functions, calibrated skill-success probabilities or policy training. |

The temporal replay, confidence aging and event-ID handling below are engineering choices for this library. They are not claimed as algorithms reproduced from those papers, nor as empirically calibrated robotics estimators. No paper's performance numbers transfer to this implementation.

## Architecture

```text
RGB-D / LiDAR + localization + instance tracker
    -> map-frame observations + identity + capture time + error radius
    -> RememberObjectAsync -> existing persistent Mem0Sharp store
    -> RecallObjectsAsync -> object beliefs + history + observation requests
    -> mission planner + CURRENT sensors
    -> independent navigation / collision checks / controller
    -> measured attempt -> RememberRobotEpisodeAsync -> next decision
```

For a service robot, this can preserve where an item was last seen across missions, avoid returning obsolete coordinates after a known relocation, distinguish a blocked view from a confirmed empty location, and recall that a previous approach was blocked. These are memory capabilities; successful navigation or manipulation still depends on the rest of the robot stack.

## Implemented contracts

- **Object evidence:** `RememberObjectAsync` stores an immutable event with a stable entity ID, sensor source, map/frame revision, capture time, measured point, confidence, positional error radius and visibility. It disables LLM inference and text deduplication.
- **Temporal reconstruction:** `RecallObjectsAsync` resolves retained events by capture time, not arrival order. It selects the newest object state before radius filtering, so an old nearby location cannot override a newer distant location. `At` supports historical event-time queries, not transaction-time reconstruction of what a client knew then.
- **Provenance:** each result retains ordered evidence, last-seen time and relocation count. Identical retry payloads count once at recall; writes remain append-only and are not physically deduplicated.
- **Uncertainty:** `Observed`, `Stale`, `Occluded`, `Missing`, `Uncertain` and `Conflicted` distinguish remembered evidence from current certainty. Conflicting simultaneous observations request verification. Reusing a source/event ID with different payloads quarantines that entity as conflicted until the bad evidence is corrected or removed.
- **Aging:** confidence is the latest positive observation's confidence multiplied by `0.5^(age / halfLife)`. Repeated frames do not boost it. `MinimumConfidence` marks an object uncertain rather than dropping the new observation and resurrecting an older one. These values are heuristics, not calibrated probabilities.
- **Visibility:** occlusion and absence do not refresh last-seen time or invent a new location. Absence requires positive coverage evidence from the adapter, not merely a missed detection. Re-observation can restore an observed state.
- **Geometry:** unique same-label, nearby association is available as a fallback. Ambiguous matches return null. A tracked moving object needs an identity from a real tracker; matching a label alone cannot establish identity across a displacement. `Near` and Y-up `Above` relations include uncertainty margins and exclude stale/uncertain objects. They do not mean `inside`, `supported by` or `reachable`.
- **Action episodes:** measured start/end points, start heading, mission, skill, timestamps and feedback persist independently. Recall supports spatial, temporal, entity, action and wrapped-heading filters. `Completed` describes a primitive, not mission success. Historical failures are advisory context, not permanent prohibitions or clearance to retry.

The [API reference](api-reference.md#robotics-object-evidence) describes signatures. The [Godot sample](../samples/3DSpatialMemoryGodot/README.md) is the executable integration.

## Real-world adapter requirements

1. Supply synchronized RGB/depth and an estimated camera pose. Transform measured points into the declared metric map frame outside Mem0Sharp. `FrameId` must change when coordinates are no longer comparable; this library does not perform SLAM corrections or frame transforms.
2. Supply stable instance identities from tracking, fiducials, asset IDs or a validated association pipeline. Do not let an LLM invent stable identities or world coordinates. Similar objects and reappearance after long occlusion need explicit data-association handling.
3. Estimate uncertainty from depth, calibration, segmentation and localization error. A model's self-reported confidence is not a substitute. Validate timestamps, units, source identity and tenant permissions at the adapter/service boundary.
4. Decide absence only after checking field of view, range, occlusion, target extent and detector coverage. `NeedsObservation == false` still does not authorize movement, grasping or contact.
5. Use the existing persistent providers, such as pgvector, with bounded tenant/map scope. Keep raw images and dense geometry in their appropriate external stores; do not stream every sensor frame through an embedding API.
6. Keep memory and model calls out of real-time control. A timeout, unavailable database or missing memory must leave the controller able to stop safely. Pass measured success/failure feedback, not inferred success from an issued command.
7. Apply a consistent retention policy to each entity's evidence stream. Deleting/expiring its newest event while retaining older events can expose older state on replay. This implementation is not a tombstone/compaction or distributed-consensus system.

Queries currently replay the scoped records returned by `GetAllAsync`. They are not database-native spatial indexes: cost grows with retained history, and geometric relation generation is pairwise in the recalled object count. Building-scale or high-rate deployments need indexed materialized beliefs, bounded evidence retention, snapshot consistency, calibrated uncertainty and workload measurements before production use. An `AgentId` filter controls the recall set; it does not authenticate a robot.

## Godot validation

Validated locally on 2026-09-05 using Godot 4.8 dev 3 .NET on Windows ARM64:

| Check | Result |
| --- | --- |
| Robotics and existing spatial unit tests on .NET 8, 9 and 10 | 45 passed |
| Robotics tests against the .NET Standard 2.0 assembly | 11 passed |
| Godot camera rendering, translation and wall collision | Passed; 25 sampled camera colors |
| Repeated event, moved crate, late-arriving old observation | Passed |
| Old-location comparison on the same scenario | Raw observation recall: 1 old point; object-belief recall: 0 old objects |
| Physical foreground occlusion, explicit checked absence, reacquisition and aging | Passed |
| Controller wall feedback | Persisted as `Blocked` |
| JSON checkpoint loaded into a fresh store and a separate Godot process | Two objects and one blocked-action episode recovered |
| Live OpenAI/embedding/pgvector loop with a new database client | One current-run object belief and one action episode recovered |
| Rendered sample windows | Inspected at 1280x800 and 960x600 |

These are deterministic regression checks plus live integration smoke tests, not a statistically meaningful robotics benchmark. The offline scenario deliberately uses scripted labels and stable IDs with physics-raycast depth. Ground truth is used by the test fixture, not passed to the live VLM.

The live path uses a frozen 80x45 depth grid and model-selected normalized image points. A live run demonstrated a background point selected for the red crate; a later diagnostic run correctly measured `(-3.03, 0.54, -2.35)` on its surface. This illustrates model localization variability, not a solved perception problem. The sample's fixed uncertainty is illustrative, not calibrated. The renderer on this test machine also produces a bright, synthetic scene. Do not infer real-world perception accuracy or task-completion reliability from these tests.

Run artifacts are generated under the sample's ignored `artifacts/` directory: `robotics-report.json`, `robotics-checkpoint.json`, `robotics-memory.png`, `robot-camera.png` and `robot-lab-live.png`. The report records the baseline comparison and scenario assertions. Unit-test builds also reported the pre-existing `SSH.NET` dependency advisory `GHSA-q939-rpr3-3284`; this work did not change that dependency.