# Agent Runtime Follow-ups (2026-06-16)

## Context
Live LAN run (Minecraft 1.21.6, local server:4242) showed that deterministic gather goal creation now works (`gather wood` -> `Gather:oak_log`), but several runtime behaviors still need follow-up.

## Confirmed Issues

1. Out-of-reach gathering strategy is missing.
- Symptom: bot can identify/gather logs but does not plan to reach floating/high logs (for example by pillar-up with dirt or by pathing to better tree positions).
- Impact: stalls or under-performs when easy logs are exhausted and reachable canopy logs remain.
- Next fix direction: add a fallback decomposition phase for `IItemSpecGoal` that attempts one of:
  - `MoveTo` alternate nearby source
  - `PlaceBlock` scaffold step(s) using cheap inventory blocks (dirt/cobble)
  - retry `MineBlock` with updated position

2. Goal interruption behavior was weak during gather loops.
- Symptom: user chat like `@AgentBot come back` could be logged while gather behavior continued.
- Root cause: chat navigation handler only executed when explicit XYZ coordinates existed; `target=player` commands were ignored.
- Mitigation implemented this session:
  - navigation intents now call `CancelGoal()` first (interrupt current gather loop)
  - `target=player` now resolves to `chat.PlayerPos` and enqueues `MoveTo`

3. Planner/runtime tool mismatch (`GetStatus` not registered).
- Symptom: repeated warning loop: `Tool GetStatus failed: Tool 'GetStatus' is not registered.`
- Impact: repeated replan cycles and excess queued actions.
- Mitigation implemented this session:
  - added `GetStatusTool` compatibility alias that maps to ActionProtocol `status`
  - registered both `StatusTool` and `GetStatusTool` in DI
  - added tests for alias dispatch path

4. LLM config compatibility mismatch.
- Symptom: Ollama operational in terminal but app path appeared not to use expected model.
- Root cause: config often uses `Agent:Chat:Model`, while runtime options bind `LlmModel`.
- Mitigation implemented this session:
  - added backward-compatible binding in `Program.cs`: when `LlmModel` key is absent, use legacy `Model` key.

## Remaining Follow-up Work

1. Add explicit runtime telemetry around LLM decision path.
- Log provider availability, selected model, and whether each message used:
  - deterministic fast-path
  - LLM decision
  - fallback path after LLM failure

2. Add integration tests tagged local-only for live services.
- Suggested NUnit category: `[Category("LocalOnly")]`
- Candidate tests:
  - LAN chat interruption (`gather wood` then `come back`)
  - Ollama roundtrip parse (`intent` JSON)
  - MemorySmith registry live read + gather planning

3. Implement scaffold/vertical reach behavior for gather goals.
- Track as a dedicated task (recommended):
  - `GatherItem` decomposition enhancement for unreachable source blocks.
