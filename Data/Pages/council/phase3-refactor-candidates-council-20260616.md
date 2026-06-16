# Council Review: Phase 3 Refactor Candidates 3–5

Date: 2026-06-16  
Scope: Candidate 3 (WorldStateProjector), Candidate 4 (HtnTask deletion), Candidate 5 (ActionProtocol constants)  
Last green commit before this session: `a2f6895b91`

---

## Decision

**Accept all three candidates.** No blocking findings. Two deferred improvements noted.

---

## Evidence Reviewed

- `Agent.Core/WorldStateProjector.cs` — new pure projector (15 event-handling branches)
- `WebUI.Blazor/AgentBackgroundService.cs` — ProcessEventsAsync slimmed to projector call + channel writes; 3 private helpers removed
- `MemorySmith.Agent.Tests/WorldStateProjectorTests.cs` — 15 unit tests
- `Agent.Planning/HtnTaskLibrary.cs` — TaskDecomposer delegate moved in; no functional change to decomposers
- `Agent.Planning/HtnTask.cs` — tombstoned (namespace + comment only)
- `Agent.Tools/ActionProtocol.cs` — 6 constants covering all live wire names
- `Agent.Tools/Tools/*.cs` — MineBlockTool, MoveToTool, WanderTool, StatusTool, PlaceBlockTool each updated to use ActionProtocol constant
- `Agent.World.Minecraft/WebSocketBridge.cs` — `action.Tool.ToLowerInvariant()` removed; value forwarded as-is
- `Data/Pages/decisions.md` — ADR-009 (HtnTask), ADR-010 (ActionProtocol) added

---

## Findings

| Seat | Recommendation | Confidence | Blocking Concern |
|---|---|---:|---|
| Source-Grounded Archivist | WorldStateProjector.Apply matches the switch logic that was previously in ProcessEventsAsync. Spawn event applies position then health+food (same order as before). blockMined namespaced-ID stripping is identical. Error/blockNotFound produce no state change as intended. HtnTaskLibrary.cs diff is additive only — the TaskDecomposer delegate added at the top of the file; all decomposers unchanged. Tombstone file compiles cleanly (empty namespace). ActionProtocol values (`move`, `mine`, `place`, `status`, `wander`, `chat`) match the Node.js command strings as documented in WebSocketBridge header comments. | 94% | None. |
| Data Model Architect | Channel<string> for error signaling is a strict improvement over the stringly-typed `game.lastError` WorldState fact. SingleWriter = true on the channel is correct (only one ProcessEventsAsync task writes). TryRead in the settle block correctly reads one error per settle cycle — semantically equivalent to the old check-and-clear pattern. Candidate 4 deletion test passes: removing HtnTask changes 0 runtime paths (no instantiation site existed). TaskDecomposer delegate is now co-located with its only consumer. ActionProtocol.Chat is reserved correctly — no tool sets it, so no test regression. | 92% | None. |
| Retrieval Specialist | WorldStateProjector.ApplyInventorySnapshot is a pure function now — previously it mutated `_worldState` as a side effect via the service's private method. The pure version returns the state, making it testable in isolation. The 15 tests in WorldStateProjectorTests cover the key paths: purity (no mutation), error events don't write game.lastError, unknown event types store raw facts only. StatusTool now uses ActionProtocol.Status — but StatusTool sends `Tool = "status"` to worldAdapter, which was already the correct wire name before. Behaviour unchanged, just more explicit. | 90% | None. |
| Human Learning Advocate | The refactoring reduces AgentBackgroundService from ~340 lines to ~230 lines. ProcessEventsAsync is now 30 lines (from ~70). The intent is immediately readable: apply projector, log notable transitions, route errors. The error-channel approach is clearer to a future developer than the `game.lastError` fact — it's a typed signal rather than a magic string key. ActionProtocol.cs is a single file to grep for all wire names. ADR-009 and ADR-010 are clear and complete. | 93% | None. |
| Skeptical Reviewer | Candidate 3: If `WorldStateProjector` is ever injected via DI (to swap implementations in tests), the current design (`private readonly WorldStateProjector _projector = new()`) would need to change. However, since the projector is stateless and has no dependencies, this is not a real problem now. The test `Apply_ReturnsNewInstance_NotSameReference` verifies the projector always returns a new `WorldState` — important since `WorldState` is a record and `with {}` semantics must be preserved. Candidate 4: the tombstone HtnTask.cs still compiles (namespace + comments). The `TaskDecomposer` delegate is now defined twice if the tombstone has a leftover reference — but it doesn't (the tombstone has no type declarations). Candidate 5: WebSocketBridge previously lowercased `action.Tool`. Removing this is safe ONLY because every tool using `worldAdapter.SendActionAsync` now passes an ActionProtocol constant that is already lowercase. Confirmed: all 5 wire names are lowercase strings. No regression. | 89% | **Deferred** (not blocking): the tombstone file will confuse future agents ("why is this file here?"). Recommend a follow-up to fully delete it via a git tree operation or document it clearly as intentional. |
| Synthesizer | All three candidates improve the codebase measurably. Candidate 3 extracts a deep module that is independently testable. Candidate 4 eliminates dead code and co-locates related types. Candidate 5 centralises a previously implicit mapping that was a runtime hazard (any new tool that forgot to set Tool = "correct-wire-name" would silently send the wrong command). The 15 new tests for WorldStateProjector, combined with the existing test suite, give good coverage of the new boundaries. No architectural regressions introduced. | 93% | None. |

---

## Synthesis

**All three candidates accepted.** The refactoring is correct, well-tested, and improves the architecture in the three dimensions the handoff identified:
- **WorldStateProjector**: god class shrinks; state mutation is now a pure function.
- **HtnTask deletion**: dead code removed; `TaskDecomposer` co-located with its sole consumer.
- **ActionProtocol**: wire-name mapping explicit and centralised; `WebSocketBridge` is now protocol-agnostic (it doesn't know about valid wire names).

**Fix before next major feature (deferred, not blocking):**
1. **Tombstone cleanup** — When possible (a CI/CD pipeline or a future git-tree write that restores token scope), fully delete `Agent.Planning/HtnTask.cs`. Until then, the tombstone comment is sufficient.

**Open for Phase 4:**
2. **WorldStateProjector injection** — If tests need to swap the projector for a mock (e.g. to inject a failing state), expose it as a constructor parameter with a default. Not needed now since the projector is pure and the existing tests cover it directly.

---

## Dissent

- Skeptical Reviewer is more concerned about the tombstone file than the Synthesizer ranked it. Recommends that the next handoff explicitly note "HtnTask.cs is a tombstone — delete it" so the next agent doesn't spend time understanding it.
- Retrieval Specialist notes that `WorldStateProjectorTests` does not include an integration test verifying that the Channel<string> path in `AgentBackgroundService.ProcessEventsAsync` correctly signals errors. The 15 tests are pure-function only. The existing `AgentBackgroundServiceTests` should be extended to cover the error-channel path (deferred to next session).

---

## Acceptance Criteria for Phase 4 Entry (Post-Refactor)

- [x] WorldStateProjector.cs created in Agent.Core with pure Apply method
- [x] AgentBackgroundService uses WorldStateProjector and Channel<string> error signaling
- [x] 15 WorldStateProjectorTests added and passing
- [x] HtnTask record tombstoned; TaskDecomposer in HtnTaskLibrary.cs
- [x] ActionProtocol constants in Agent.Tools; all 5 tools updated; WebSocketBridge no longer lowercases
- [x] ADR-009 and ADR-010 added to decisions.md
- [ ] CI green on final commit (pending — check after push)

---

## Addendum — 2026-06-16 (deferred item resolved, same session)

**Deferred item 2 from Retrieval Specialist now closed:**

Add integration test verifying that `blockNotFound` / `error` events are written to `_gameErrors` Channel and increment `_consecutiveFailures`.

**Root cause found during implementation**: `DispatchActionsAsync` Block 1 (re-planning guard) was missing `!_actionDispatchedThisCycle`. Without this guard, re-planning fires in the same loop iteration that should have entered the settle window, so the 300ms `Task.Delay` and the channel `TryRead` never execute. This made `_consecutiveFailures` via the error-channel path effectively dead code.

**Fix** (commit `5d59d1c9`): Added `!_actionDispatchedThisCycle` to Block 1's guard:

```csharp
if (_queue.IsEmpty && _currentGoal is not null && !_actionDispatchedThisCycle)
```

When a plan cycle completes (all actions dispatched → `_actionDispatchedThisCycle = true`), re-planning is deferred one iteration. That iteration's Block 2 dequeues null, the `else` branch fires, and the settle runs. After the settle, `_actionDispatchedThisCycle = false` and Block 1 runs on the following iteration.

**New constructor parameter** (commit `0880af81`): `maxConsecutiveFailures = 3` (optional, default unchanged) lets tests observe failure behaviour with a 1-failure threshold.

**3 tests added** (commit `75d76dcb`, passing on `5d59d1c9`):
- `BlockNotFoundEvent_MinedZero_WritesToErrorChannel_CausesGoalAbandonment`
- `ErrorEvent_WritesToErrorChannel_CausesGoalAbandonment`
- `BlockNotFoundEvent_MinedGreaterThanZero_DoesNotSignalError`

CI green on `5d59d1c9` — all tests pass.

**All Phase 3 council acceptance criteria now fully met.**
