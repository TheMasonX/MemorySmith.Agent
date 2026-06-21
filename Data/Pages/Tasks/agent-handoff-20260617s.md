# MemorySmith.Agent Handoff — Sprint 18
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` → PR #1 (merge still deferred)  
**Head commit:** `05f9a6d` (AGENTS.md curl examples — final Sprint 17 push)  
**CI status:** ✅ Green (build-and-test: success on run 27721607397)  

---

## Current state in one paragraph

Leo is a Minecraft bot (C# + Node.js) with a deterministic HTN planner, LLM fallback chat, and a MemorySmith wiki as long-term memory. Sprints 1–17 are complete. Sprint 17 finished Phase 7-B: the `ClassifySpec` heuristic now correctly classifies ore drops (diamond, coal, emerald, redstone, lapis_lazuli) as `DirectMineable` by consulting `CommonMinecraftBlocks.DirectMineBlocks`; `LocalKnowledgeResolver` gained a third knowledge source (`CandidateType.WorldFact`) that scans `WorldState.StructuredFacts` at resolve time via a lazy factory delegate; 4 new tests were added; AGENTS.md now documents the `/api/agent/resolve` endpoint. Sprint 18 begins Phase 7-C: observation pipeline normalization — centralizing the scattered item/block name normalization logic that currently lives ad-hoc in `WorldStateProjector`.

---

## What changed in Sprint 17 (this session)

**P0 — ClassifySpec fix (council D1)**
- `Agent.Core/CommonMinecraftBlocks.cs`: expanded `DirectMineBlocks` with raw ore drops (`diamond`, `coal`, `emerald`, `redstone`, `lapis_lazuli`) and the missing `emerald_ore`/`deepslate_emerald_ore` block names; updated class comment
- `Agent.Memory/LocalKnowledgeResolver.cs`: `ClassifySpec` now checks `DirectMineBlocks.Contains(spec.ItemId)` OR `SourceBlocks.Contains(spec.ItemId)` — the OR ensures both paths work (self-sourced blocks like oak_log pass via SourceBlocks; ore drops like diamond pass via DirectMineBlocks)

**P1 — WorldFact third source (council D4)**
- `Agent.Memory/IKnowledgeResolver.cs`: `CandidateType.WorldFact` added as sixth enum value
- `Agent.Memory/LocalKnowledgeResolver.cs`: optional `Func<WorldState?>? worldStateAccessor = null` constructor parameter; step 4 in the pipeline scans `StructuredFacts` for keys containing the normalized query; confidence decay: `0.70f` (< 60s) / `0.50f` (≥ 60s)
- `WebUI.Blazor/Program.cs`: DI registration updated to pass `() => sp.GetService<AgentBackgroundService>()?.WorldState` — lazy evaluation avoids the forward-reference issue

**D2 — SearchAsync raw-query comment**
- `LocalKnowledgeResolver.cs`: added comment explaining `SearchAsync` receives `query.Query` (not `normalizedId`) — intentional for semantic search quality

**D3 — AGENTS.md curl examples**
- `AGENTS.md`: new "Testing the /api/agent/resolve endpoint" subsection with 5 curl examples + 3 behavior notes

**Tests:** 4 new tests in `KnowledgeResolverTests.cs` — `ClassifySpec_Diamond_ReturnsDirectMineable`, `ClassifySpec_OakLog_ReturnsDirectMineable`, `WorldFact_ReturnsOnQueryMatch`, `WorldFact_LowConfidenceForOldFact`

**Council review:** `Data/Pages/council/sprint17-council-20260617.md` — no blockers, approved

---

## Suggested skills

- **GitHub MCP** — all code at `TheMasonX/MemorySmith.Agent`, branch `sprint-5-tool-safety`. Always fetch blob SHA before updating existing files. Use `paramsFile`, never inline.
- **CI check** — `curl -s "https://api.github.com/repos/TheMasonX/MemorySmith.Agent/commits/<sha>/check-runs"` + annotations for failures.
- **Council review pattern** — 6-seat + anonymous peer review to `Data/Pages/council/` after each sprint.

---

## Sprint 18 starting point (Phase 7-C: Observation Pipeline Normalization)

### Background

The audit synthesis (C2) and architecture inventory both identify the observation pipeline as the critical missing layer between adapter events and planning. Currently:
- `WorldStateProjector.ApplyStatus` normalizes inventory keys (strips `"minecraft:"` prefix) — done in Sprint 14
- `WorldStateProjector.ApplyBlockMined` uses raw `e.BlockId` — NOT normalized
- Other Apply* methods use raw event data without normalization
- Normalization logic is scattered ad-hoc across WorldStateProjector methods

Phase 7-C goal: centralize all normalization into a single `ObservationNormalizer` service so every Apply* path goes through one canonical normalization step.

### P0 — ObservationNormalizer service

**Why:** Scattered normalization means new Apply* methods added in Phase 7-D+ will silently forget to normalize. A single normalizer prevents future drift.

**Tasks:**
1. Create `Agent.Core/ObservationNormalizer.cs`:
   ```csharp
   public static class ObservationNormalizer
   {
       // Strips "minecraft:" namespace prefix from item/block IDs.
       // "minecraft:oak_log" → "oak_log", "oak_log" → "oak_log"
       public static string NormalizeId(string id) =>
           id.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)
               ? id["minecraft:".Length..]
               : id;

       // Normalizes an inventory snapshot dictionary in-place-equivalent.
       public static Dictionary<string, int> NormalizeInventory(IReadOnlyDictionary<string, int> raw) =>
           raw.GroupBy(kv => NormalizeId(kv.Key))
              .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));

       // Normalizes a block ID for use in facts and planner state.
       public static string NormalizeBlockId(string blockId) => NormalizeId(blockId);
   }
   ```
2. **Refactor `WorldStateProjector.ApplyStatus`**: replace the current inline `key.StartsWith("minecraft:", ...)` normalization with `ObservationNormalizer.NormalizeInventory`
3. **Patch `WorldStateProjector.ApplyBlockMined`**: wrap `e.BlockId` with `ObservationNormalizer.NormalizeBlockId(e.BlockId)` before storing in facts and updating inventory
4. Add tests: `NormalizeId_StripsMincraftPrefix`, `NormalizeId_LeavesBareIdUnchanged`, `NormalizeInventory_MergesDuplicateAfterNormalization`, `ApplyBlockMined_UsesNormalizedBlockId`

**Files:** `Agent.Core/ObservationNormalizer.cs` (NEW) · `Agent.Core/WorldStateProjector.cs` · `MemorySmith.Agent.Tests/WorldStateProjectorTests.cs`

**Note:** `Agent.Core` has no dependencies on other projects. `ObservationNormalizer` goes there. No project reference changes needed.

### P1 — WorldModel.Observe integration check

**Why:** `IWorldModel.Observe(ObservationState)` was introduced in Sprint 6 but is never called from `AgentBackgroundService.ProcessEventsAsync` — raw events bypass the WorldModel entirely.

**Tasks:**
1. In `AgentBackgroundService.ProcessEventsAsync`, after `WorldStateProjector.Apply(event)`, call `_worldModel.Observe(new ObservationState { ... })` with the relevant fields from the event
2. Do this for at minimum: `StatusEvent` (position + inventory) and `BlockMinedEvent` (block type)
3. Add 1–2 tests verifying Observe is called when a StatusEvent is processed

**Files:** `WebUI.Blazor/AgentBackgroundService.cs` · `MemorySmith.Agent.Tests/`

**Constraint:** Do not call `_worldModel.Observe` on every event type — start with high-value, low-frequency events to avoid performance overhead. `StatusEvent` and `BlockMinedEvent` are the right starting set.

### P2 — Deferred carry-forwards

| ID | Task | File |
|----|------|------|
| B3 | Orientation-aware PlaceBlock (facing direction in action args) | `HtnTaskLibrary.cs`, `index.js` |
| D2 (S2) | MemorySmithItemRegistry parallel miss race | `Agent.Memory/` |
| D2 (S17) | Add AGENTS.md note: suggest `confidenceThreshold ≥ 0.3` for WorldFact queries | `AGENTS.md` |

---

## Phase 7 roadmap (current state)

| Sub-phase | Focus | Sprint estimate |
|-----------|-------|----------------|
| **7-A (done)** | Architecture inventory; planner routing cleanup | Sprint 16 ✅ |
| **7-B (done)** | Resolver growth: ClassifySpec fix + WorldFact source | Sprint 17 ✅ |
| **7-C (now)** | Observation pipeline normalization | Sprint 18 |
| 7-D | Belief layer + IBeliefState | Sprint 19 |
| 7-E | Episodic memory + IEpisode | Sprint 20 |
| 7-F | Planner input migration to world model + beliefs | Sprint 21 |
| 7-G | Reflection service | Sprint 22 |
| 7-H | Page synthesis from memory clusters | Sprint 23 |
| 7-I | Adapter generalization audit | Sprint 24 |

---

## Key rules (non-negotiable)

All in `AGENTS.md` at repo root. Critical ones:
1. **Warnings = errors** (`Directory.Build.props`). Fix before pushing.
2. **paramsFile, never inline content** when pushing to GitHub MCP.
3. **CI must be green before council review.**
4. **Enqueue chat response AFTER the switch** in `HandleChatEventAsync`.
5. **ActionQueue is ConcurrentQueue** — don't revert to `Queue<T>`.
6. **GoalNamesMatch compares by suffix** — "GatherItem:X" matches "Gather:X".
7. **Council workflow per phase:** implement → local build/test → push → CI green → council review → fix blockers → confirm CI → next sprint.

---

## Files to read on arrival

- `AGENTS.md` — all rules, patterns, anti-patterns (5 min read); includes /api/agent/resolve curl examples added Sprint 17
- `Data/Pages/Tasks/phase6-tasks.md` — sprint tracker (Sprints 1–17)
- `Data/Pages/council/sprint17-council-20260617.md` — latest council; see deferred items D1–D6
- `Agent.Core/WorldStateProjector.cs` — start here for P0; understand current normalization pattern in `ApplyStatus`
- `Agent.Core/CommonMinecraftBlocks.cs` — see the DirectMineBlocks set (expanded in Sprint 17)
- `Agent.Memory/LocalKnowledgeResolver.cs` — fully updated resolver with all three sources
- `WebUI.Blazor/AgentBackgroundService.cs` — start here for P1 WorldModel.Observe wiring

---

## Guidance for future agent sessions

Per user instructions, every sprint must follow this workflow:
1. Review the handoff and understand the next sprint's tasks
2. Implement the sprint (P0 first, then P1, then P2)
3. Run 6-seat council review (`Data/Pages/council/sprint<N>-council-<date>.md`) with:
   - Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer
   - Plus anonymous peer review
   - Explicit dissent, per-seat confidence, blocking vs. deferred triage, testable acceptance criteria
4. Fix any blocking council findings; confirm CI green
5. Update `Data/Pages/Tasks/phase6-tasks.md` with sprint tracking row
6. Write the next sprint handoff (`Data/Pages/Tasks/agent-handoff-20260617<letter>.md`) — use the next letter in sequence after `s`
7. Push all docs to the branch

**This guidance must be included in every future handoff.**
