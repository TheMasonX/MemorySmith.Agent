# Architecture Review — MemorySmith.Agent

**Date:** 2026-06-16  
**Reviewer:** Agent (automated session, 6th session of the day)  
**Scope:** Full codebase as of Phase 5b + refactor  
**File:** Data/Pages/architecture-review-20260616.md

---

## 1. Overview

MemorySmith.Agent is a modular autonomous Minecraft agent framework that treats the
game world as a swappable "world adapter" and uses MemorySmith (a separate wiki-backed
knowledge base) as long-term memory. The agent is designed for autonomous resource
gathering, construction, and in-game interaction via natural language chat.

**Technology stack:**
- Runtime: .NET 10 (C#, SDK-style projects)
- World adapter: Node.js / Mineflayer subprocess over WebSocket
- Memory: MemorySmith REST API (wiki pages, front-matter parsing)
- LLM: Ollama (local) with multi-provider extensibility
- Web UI: ASP.NET Core Minimal API + static HTML dashboard
- Tests: NUnit 4 + GitHubActionsTestLogger

---

## 2. Project Structure & Dependency Graph

```
MemorySmith.Agent.slnx  (.slnx — ADR-007)
│
├── Agent.Core                    (no dependencies)
│   Models: WorldState, ActionData, ActionPlan, ActionQueue,
│            WorldEvent, SearchResult, Position
│   Interfaces: IGoal, IItemSpecGoal, IWorldAdapter, ITool,
│               IToolCaller, IPlanner, IMemoryGateway, IPlan,
│               IItemRegistry
│
├── Agent.Construction            → Agent.Core
│   Blueprint, PlacementBlock, BlueprintParser, BlueprintExecutor
│   Interfaces: IBlueprintRepository, IBlueprintExecutor, IArchitect
│
├── Agent.Memory                  → Agent.Core, Agent.Construction
│   RestMemoryGateway (IMemoryGateway)
│   MemorySmithItemRegistry (IItemRegistry)
│   MemorySmithBlueprintRepository (IBlueprintRepository)
│
├── Agent.Planning                → Agent.Core, Agent.Construction
│   Goals: GatherWoodGoal, SurviveNightGoal, GenericGatherGoal, BuildGoal
│   GoalFactory (IGoalFactory), HtnPlanner (IPlanner), HtnTaskLibrary
│   Chat: IChatInterpreter, ChatInterpreter, LlmChatInterpreter,
│          ChatRateLimiter, ChatModels (ChatInterpretation, ChatIntentType)
│   Llm/: ILlmProvider, ChatOptions, LlmProviderFactory
│          OllamaProvider, OpenAICompatibleProvider,
│          AnthropicProvider, GeminiProvider
│
├── Agent.Tools                   → Agent.Core
│   ActionProtocol, ToolDispatcher
│   Tools: MoveToTool, MineBlockTool, WanderTool, StatusTool,
│           PlaceBlockTool, SearchMemoryTool, GetPageTool, CreatePageTool,
│           ChatTool, CraftItemTool, FurnaceTool
│
├── Agent.World.Minecraft         → Agent.Core
│   MinecraftAdapter, WebSocketBridge, MinecraftAdapterConfig
│
├── WebUI.Blazor                  → all Agent.* projects
│   Program.cs (Minimal API host, DI wiring)
│   AgentBackgroundService (hosted agent loop)
│   wwwroot/: index.html (dashboard), about.html (static info)
│
├── Agent.Vision                  (stub — future GOAP/vision subsystem)
│
└── MemorySmith.Agent.Tests       → Agent.Core, Agent.Construction,
                                    Agent.Planning, Agent.Tools, WebUI.Blazor
```

**Dependency flow:** acyclic, layered. Core → domain → adapters → host. Tests reach all layers.

---

## 3. Core Domain (Agent.Core)

**Strengths:**
- Immutable record types (WorldState, ActionData, ActionPlan) prevent accidental mutation.
- `WorldStateProjector` is a pure function (event → state) — easily unit-testable.
- Interfaces are narrowly scoped (IGoal has 5 members, IWorldAdapter has 4).
- `ActionQueue` wraps thread-unsafe `Queue<T>` in a thin mutex-free abstraction.

**Gaps:**
- `ActionQueue` is not thread-safe. `AgentBackgroundService` accesses it from a single loop thread, which is correct today. If parallelism is added (e.g., a GOAP replanner on a background thread), a `Channel<ActionData>` would be safer.
- `WorldEvent.Payload` is `Dictionary<string, object?>`. Strongly-typed event subtypes (e.g., `ChatWorldEvent`, `BlockMinedEvent`) would improve discoverability and eliminate string key constants scattered across `AgentBackgroundService`.
- `SimpleGoal` is defined in `Agent.Core/Models/` rather than `Agent.Planning/Goals/`. It doesn't belong in Core.

---

## 4. Memory Layer (Agent.Memory)

**Strengths:**
- `MemorySmithItemRegistry` and `MemorySmithBlueprintRepository` follow an identical lookup pattern: direct slug lookup → search fallback → parse front-matter. Consistent and predictable.
- `ParseItemSpec` is `public static` — directly unit-testable without HTTP.
- `RestMemoryGateway` uses `IHttpClientFactory` (HttpClient pooling). No connection exhaustion.

**Gaps:**
- No caching. Every `IItemRegistry.GetAsync("oak_log")` issues a fresh HTTP request. A simple `ConcurrentDictionary<string, ItemSpec?>` TTL cache would dramatically reduce load on MemorySmith during active builds (330+ PlaceBlock actions each potentially re-checking inventory).
- `MemorySmithBlueprintRepository.SaveAsync` throws `NotImplementedException`. The interface contract is broken — callers who hold `IBlueprintRepository` cannot reliably call `SaveAsync`.
- `SearchResult.Kind` string comparison is scattered: `r.Kind == "page"` vs `string.Equals(r.Kind, "page", OrdinalIgnoreCase)`. Should be an enum or constant.

---

## 5. Planning System (Agent.Planning)

**Strengths:**
- HTN decomposition is clean: `GoalFactory.CreateAsync` resolves goals, `HtnPlanner.PlanAsync` decomposes them, `HtnTaskLibrary` holds the decomposers.
- `IItemSpecGoal` marker interface (D2) prevents `goal is ConcreteType` from proliferating.
- `GoalFactory.RegisteredGoals` is now dynamic — REST clients and the dashboard can discover all registered goal types.
- `BuildGoal` correctly holds both metadata (Blueprint) and execution data (IReadOnlyList\<PlacementBlock\>), enabling the planner to work with full context.

**Gaps:**
- **HtnPlanner still uses `goal is BuildGoal`** type-check after the IItemSpecGoal refactor. The same marker-interface pattern should be extended to `BuildGoal` (e.g., `IBuildGoal` or a generalized `IStructuredGoal`).
- **`HtnTaskLibrary.DirectMineBlocks`** is a hardcoded HashSet. This should be wiki-driven (item registry pages already have `min_harvest_level`) or at minimum configurable.
- **`GoalFactory` holds a concrete `IBlueprintRepository`** dependency. Injecting via constructor is clean, but the registration in `Program.cs` uses `sp.GetRequiredService<GoalFactory>()` which bypasses the `IGoalFactory` interface for the async path. Any code holding only `IGoalFactory` cannot create async goals — `CreateAsync` is not on the interface everywhere it matters.
- **No goal priority or preemption.** If the agent is mid-build and receives a "stop" command, `CancelGoal()` works, but there's no ability to pause and resume. Resuming a partially-built house requires re-running the full plan (would re-place already-placed blocks).
- **`HtnPlanner.ReplanAsync`** creates a `SimpleGoal` by copying the current plan's goal name and phases. It should instead reuse the original `IGoal` instance (already stored in `AgentBackgroundService._currentGoal`) to preserve typed dispatch.

---

## 6. Construction System (Agent.Construction)

**Strengths:**
- `BlueprintParser` is a clean static parser with graceful null returns.
- `IsValidGridRow` validation prevents prose from being misinterpreted as grid data.
- Layer-by-layer grid format is human-readable and machine-parseable.

**Gaps:**
- **No resume/checkpoint.** A 330-block build plan has no way to restart from the last successfully placed block after a crash.
- **No area clearance.** `DecomposeBuild` places blocks at absolute coordinates but doesn't check if the target cells are already occupied by terrain or other structures.
- **`IBlueprintExecutor` is not injected** — `HtnTaskLibrary.DecomposeBuild` instantiates `new BlueprintExecutor()` directly. DI injection would improve testability.
- **Door/bed facing** is not encoded in `PlacementBlock`. The Mineflayer adapter uses bot yaw at placement time. A `FacingDirection` property on `PlacementBlock` would make orientation deterministic.
- **`IArchitect`** interface is defined but not implemented. Procedural blueprint generation (e.g., LLM-driven room layout) would make this useful.

---

## 7. Chat System (Agent.Planning, v2)

**Strengths:**
- "Split-brain" architecture: deterministic pattern-matching + LLM as an optional enhancement. D-003 (deterministic-first) is fully respected.
- `LlmChatInterpreter` has a clear pipeline: truncate → distance gate → pattern fast-path → rate limit → LLM → fallback.
- `ChatRateLimiter` uses sliding-window for global (correct) and per-player cooldown (simpler, correct for the use case).
- `ChatOptions` is a single, flat config class — easy to bind from JSON.
- Provider abstraction (`ILlmProvider`) returns raw `string?`, keeping the parsing logic in one place (`LlmChatInterpreter.ParseDecision`).
- `LlmProviderFactory` covers 7 providers: Ollama, OpenAI, OpenRouter, DeepSeek, GitHub Copilot, Anthropic, Gemini.

**Gaps:**
- **Ollama call is in the event-processing loop.** `HandleChatEventAsync` awaits `LlmChatInterpreter.InterpretAsync` which can block for up to `LlmTimeoutSeconds`. During this time, world events (health changes, death, kicked) queue in the WebSocket channel but are not applied to `_worldState`. A background `Channel<ChatEvent>` with a separate consumer would fix this.
- **No chat history context.** The LLM sees only the single current message. Sending the last 3-5 conversation turns would dramatically improve multi-turn interactions (e.g., "get 32 of those" wouldn't require specifying "those" explicitly).
- **`ChatRateLimiter.Prune()` is never called.** The per-player dictionary grows forever. A simple `PeriodicTimer` call in `AgentBackgroundService` would fix this.
- **LLM JSON parsing (`ParseDecision`) is fragile** with greedy `\{[\s\S]*\}` brace matching. If the model produces a response like `{"a":{"b":...}}`, the inner object start would be handled correctly by `JsonDocument.Parse`, but a model that outputs two root objects would fail silently.
- **`ConversationWindowSeconds` state** (`_lastBotSpoke`) is held in `ChatInterpreter`, but `LlmChatInterpreter.RecordBotSpoke()` delegates to `patternFallback.RecordBotSpoke()`. This is correct but subtle — if `ChatInterpreter` is replaced, the forwarding must be maintained.

---

## 8. Tool Layer (Agent.Tools)

**Strengths:**
- `ActionProtocol` constants are the single source of truth for wire names (ADR-010).
- `ToolDispatcher` is a simple name→ITool dictionary. Registration in `Program.cs` is explicit and readable.
- `PlaceBlockTool` correctly uses `ActionProtocol.Place` ("place") not "PlaceBlock" — the tool name and wire name are cleanly separated.

**Gaps:**
- **`FurnaceTool.ExecuteAsync`** just dispatches the `smelt` action — the actual 40-second wait is in `index.js`. The C# side has no way to know when smelting completes short of polling `GetStatus`. A `smeltComplete` event acknowledgment tracked in `AgentBackgroundService` would close this gap.
- **`CraftItemTool`** doesn't pathfind to a crafting table. If the bot is >4 blocks away, craft fails silently. The tool should either pathfind first or the Mineflayer handler should expand the search radius.
- **Tool registration is hardcoded** in `Program.cs`. A plugin-style `ITool[]` discovered via DI scanning would be cleaner for large tool sets.
- **No tool timeout.** Any tool can hang indefinitely if the world adapter disconnects mid-action.

---

## 9. World Adapter (Agent.World.Minecraft + MineflayerAdapter)

**Strengths:**
- Sequential command queue in `index.js` eliminates "Digging aborted" race conditions.
- Auto-navigation in the `mine` case before digging is a significant UX improvement.
- `sendBotStatus()` includes a full inventory map — the dashboard and planner use this.
- `wander` correctly clamps to `maxDistanceFromSpawn` using vector scaling.

**Gaps:**
- **`place` action** computes the reference block offset using floating-point subtraction from bot position — this can be imprecise when the bot is mid-movement. A brief `goto` before equipping and placing would be more reliable.
- **No retry on Mineflayer errors.** If the bot disconnects, `ReceiveEventsAsync` terminates and the hosted service shuts down. `MinecraftAdapter.ConnectAsync` should support reconnection with exponential backoff.
- **`bot.on('move')` fires too often** (every tick the bot moves), filling the WebSocket with `move` events. The C# projector updates position on every event but the dashboard only polls every 2s. Consider throttling to once per 500ms.
- **No entity tracking.** The agent can't see players, mobs, or other entities — only blocks and inventory. An `entityUpdate` event would enable basic awareness.

---

## 10. Web UI & Host (WebUI.Blazor)

**Strengths:**
- Minimal API pattern is appropriate for this use case — no Razor Pages overhead.
- `AgentBackgroundService` correctly uses `Task.WhenAll(ProcessEventsAsync, DispatchActionsAsync)` — both loops run concurrently.
- `_pendingActions` snapshot (with `_pendingLock`) gives the REST API a consistent view without exposing the action queue's internals.
- `index.html` dashboard is self-contained, polls live APIs, and works without build tooling.

**Gaps:**
- **No real-time push.** Dashboard polls every 2s. World events (chat, blockMined, death) are not surfaced unless the poll happens to show them. SignalR or SSE would solve this.
- **`AgentBackgroundService` constructor has 7 parameters.** This is a code smell for growing complexity. A dedicated `AgentLoopOptions` record could carry `botName` and `maxConsecutiveFailures`.
- **`_pendingActions.RemoveAt(0)` is O(n).** For a 330-block build plan, this is ~54k operations total. A `Queue<ActionData>` or index pointer would be O(1).
- **Version number is hardcoded** in `Program.cs` (`Version = "0.7.0"`). Should come from assembly metadata.
- **`/api/blueprints`** returns a hardcoded list. Should query `IBlueprintRepository.SearchAsync`.

---

## 11. Testing

**Strengths:**
- 188+ NUnit tests covering all major subsystems.
- `MockMemoryGateway`, `MockWorldAdapter`, `MockPlanner` isolate unit tests from real dependencies.
- `GitHubActionsTestLogger` surfaces failing test names in CI annotations.
- `ci.runsettings` configures a stable test runner for CI.

**Gaps:**
- **`AgentBackgroundService` is not tested** beyond the existing smoke test. The event routing, goal lifecycle (IsComplete, HasFailed), and consecutive-failure fallback are critical paths without coverage.
- **`MemorySmithItemRegistry` and `MemorySmithBlueprintRepository`** have no integration tests against a live MemorySmith instance.
- **`LlmChatInterpreter`'s JSON parsing** (`ParseDecision`) is not tested in isolation. A test that feeds a variety of LLM responses (well-formed, malformed, code-fenced, partial) would catch regressions.
- **`BlueprintParser.IsValidGridRow`** has no test for the edge case where a grid row has length 1.
- **No end-to-end test** exercising the full path: chat → LlmChatInterpreter → GoalFactory → HtnPlanner → ActionQueue dispatch.

---

## 12. Identified Technical Debt

| Item | Severity | Phase |
|------|----------|-------|
| `WorldEvent` payload is untyped `Dictionary<string, object?>` | Medium | 6 |
| `SimpleGoal` in Agent.Core | Low | 6 |
| `IBlueprintRepository.SaveAsync` throws NotImplementedException | Medium | 5 |
| No IItemRegistry/IBlueprintRepository caching | Medium | 6 |
| No `bot.on('move')` throttle | Low | 6 |
| `HtnPlanner` still uses `goal is BuildGoal` type-check | Low | 6 |
| `_pendingActions.RemoveAt(0)` O(n) | Low | 6 |
| LLM call blocks event loop | High | 6 |
| `ChatRateLimiter.Prune()` never called | Low | 6 |
| No tool timeout / watchdog | Medium | 6 |
| No MinecraftAdapter reconnect | High | 6 |
| No build resume/checkpoint | Medium | 7 |
| No crafting chain (planks from logs) in DecomposeBuild | High | 6 |
| `/api/blueprints` hardcoded | Low | 6 |

---

## 13. Recommendations

### Immediate (Phase 6)
1. **Move LLM call off event loop** — channel-based background consumer for chat events.
2. **MinecraftAdapter reconnect** — exponential backoff with 3 retries before giving up.
3. **Crafting chain** — `DecomposeBuild` should emit `CraftItem` for planks/slabs after log mining.
4. **IItemRegistry caching** — 60-second TTL cache in `MemorySmithItemRegistry`.

### Medium-term (Phase 6–7)
5. **Typed world events** — discriminated union or sealed class hierarchy instead of `Dictionary<string, object?>`.
6. **FindFlatAreaTool** — terrain scan to auto-set build origin.
7. **IBuildGoal marker interface** — mirrors IItemSpecGoal for the BuildGoal type check.
8. **AgentBackgroundService tests** — event routing + goal lifecycle coverage.
9. **Build resume** — persist progress to WorldState facts; DecomposeBuild skips already-placed blocks.

### Long-term (Phase 7+)
10. **LLM chat history** — multi-turn context window (last 5 messages).
11. **Entity awareness** — player/mob positions from Mineflayer entity tracking.
12. **GOAP integration** — backward-chaining planner for novel goals that lack HTN methods.
13. **Vision model** — Ollama multimodal for screenshot analysis and structure critique.
14. **Multi-agent coordination** — `ChatCoordinator` service + shared state bus.
