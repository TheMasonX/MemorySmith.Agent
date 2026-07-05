# MemorySmith.Agent — Deep Audit Round 2 (DELTA ONLY)

Report: msa_deep_audit_dev_round3_20260701_delta_CT.md

Scope: **New findings only.** Everything in `msa_deep_audit_dev_round3_20260701_1500_CT.md` (§3.1–3.4, A-1–A-4) still stands and is not repeated here. This pass read, line-by-line, everything not yet covered in round 1: `Agent.Memory` (988 lines), `Agent.Construction` (full), `Agent.Tools` (full, 1,379 lines across 14 tools + dispatcher), `Agent.World.Minecraft` (full, 867 lines). Cross-checked every new finding against `Data/Tasks/*.json` before writing it up.

---

## New Finding 1 — `MemorySmithItemRegistry` never got the TSK-0109 cancellation fix its twin class received

**Confidence: 88% (Verified)**
**File:** `Agent.Memory/MemorySmithItemRegistry.cs`

TSK-0109 (`Done`) fixed `MemorySmithBlueprintRepository`: a caller-cancelled request was being caught by the generic `catch (TaskCanceledException)` fallback path and silently continuing into local-file/search fallback logic instead of honoring cancellation. The fix added an explicit `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` guard *before* the `TaskCanceledException` catch, in both `GetAsync` and `SearchAsync`.

`MemorySmithItemRegistry.FetchAsync` has the **exact same shape of try/catch** (`HttpRequestException` → log + fall through; `TaskCanceledException` → log + fall through) for both its direct-lookup and search-fallback HTTP calls — and has **zero** `OperationCanceledException` guards anywhere in the file (confirmed via grep, 0 matches). Since `TaskCanceledException` derives from `OperationCanceledException`, a caller that cancels a `GetAsync(itemId, ct)` call while `HttpClient` is mid-request will have that cancellation caught as if it were a network timeout, and the registry will proceed to try `LoadLocalPage` and then a gateway search — i.e., it keeps doing I/O after the caller asked it to stop.

TSK-0109's own description ("From supplemental audit finding 4... a caller that cancels does not actually get cancellation behavior") describes a class of bug, but the fix was scoped and applied to one of two structurally identical classes.

**Recommendation:** Apply the identical `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` guard to `MemorySmithItemRegistry.FetchAsync`'s two try blocks (direct lookup + search fallback). This is a copy-paste-sized fix. File as `P2: Apply TSK-0109 cancellation fix to MemorySmithItemRegistry (sibling class was missed)`.

---

## New Finding 2 — 11 of 14 tools re-parse their JSON schema from a string literal on every single dispatch

**Confidence: 75% (Verified pattern + call site; perf impact not benchmarked)**
**Files:** `Agent.Tools/Tools/*.cs`, `Agent.Tools/ToolDispatcher.cs:113`

`ToolDispatcher.CallAsync` reads `tool.InputSchema` on every invocation (`var schema = tool.InputSchema;`) — i.e., once per action the agent dispatches (potentially hundreds of times per session). Three tools (`FindFlatAreaTool`, `QueryBlocksTool`, `QueryEntitiesTool` — all Sprint 40+/55) implement `InputSchema` as a `private static readonly JsonDocument _schemaDoc` field, parsed once, with the property just returning `_schemaDoc.RootElement`. `FindFlatAreaTool` even has a comment explaining why: *"the document is disposed immediately, leaving the element over freed memory"* (that specific framing is a little off for managed code — nothing actually calls `Dispose()` so there's no use-after-free; the real cost is repeated parsing + an un-returned `ArrayPool` rental per call — but the fix itself, caching the parsed document, is correct regardless of the stated rationale).

The other 11 tools (`ChatTool`, `CraftItemTool`, `CreatePageTool`, `FurnaceTool`, `GetPageTool`, `GetStatusTool`, `MineBlockTool`, `MoveToTool`, `PlaceBlockTool`, `SearchMemoryTool`, `WanderTool`) all still do `public JsonElement InputSchema => JsonDocument.Parse("""...""").RootElement;` inline — full JSON parse of a static, never-changing string, on every property access, every dispatch, for the lifetime of the process.

**Why this matters for "no legacy patterns":** this is a fix that already exists in the codebase (three tools have it) and simply wasn't back-ported to the other eleven. It's exactly the kind of drift that accumulates when a good pattern is introduced without a corresponding "update the existing N call sites" task.

**Recommendation:** Mechanical refactor — convert the remaining 11 tools' `InputSchema` to the same `static readonly JsonDocument` pattern. Low risk (pure perf/hygiene, no behavior change), good candidate for a single small PR. File as `P3: Cache InputSchema JsonDocument in remaining 11 Agent.Tools implementations (consistency + avoid re-parse per dispatch)`.

---

## New Finding 3 — `WebSocketBridge`'s "permanently deaf" state has no escalation path, and `IsConnected` is checked by nobody

**Confidence: 85% (Verified)**
**Files:** `Agent.World.Minecraft/WebSocketBridge.cs`, `MinecraftAdapter.cs`, `Agent.Core/Interfaces/IWorldAdapter.cs`

This is distinct from the already-tracked/`Done` work (`TSK`: auto-reconnect with exponential backoff, kick→reconnect loop verified end-to-end) — those cover the *reconnect mechanism*. This finding is about what happens when reconnection is exhausted.

`RunReceiveLoopWithRetryAsync` retries the WebSocket connection 3 times (5s apart), and on final exhaustion does exactly one thing:

```csharp
_logger.LogError(
    "WebSocketBridge: All {MaxRetries} reconnect attempts failed. Agent is permanently deaf.",
    maxRetries);
```

...then falls into `finally { _inbound.Writer.TryComplete(); }`. No exception is thrown, no event is raised, nothing calls back into `MinecraftAdapter` or `AgentBackgroundService`. The bridge's own `IsOpen` property will (correctly) report `false` afterward, and `MinecraftAdapter.IsConnected` forwards that — but a repo-wide grep for `IsConnected` shows **it is read by nothing** other than its own definition and implementation. There is no watchdog, no dashboard tile, no health-check endpoint, no periodic check in `AgentBackgroundService`'s loop that would notice the agent has gone deaf.

Two compounding gaps:
1. **No log-independent alerting.** If nobody is actively tailing logs at the moment of the `LogError` call, the agent silently sits running — still polling its own internal loop, still "alive" in any process-level sense — but permanently unable to receive world events, indefinitely, until manually restarted.
2. **No full-stack recovery attempted.** The 3 retries only re-open the `ClientWebSocket` against the existing configured URL. If the underlying cause is the **Node.js process itself** having crashed (not just the socket), reconnection will fail all 3 attempts in ~15 seconds and give up — even though `MinecraftAdapter.StartNodeProcessAsync` (which can relaunch the Node subprocess) already exists in the same class and is never invoked from the retry path.

**Recommendation:** Two independent, separable fixes:
- Cheap: surface the terminal failure as something `AgentBackgroundService` can react to — e.g., an event/callback, or simply have `AgentBackgroundService` poll `_worldAdapter.IsConnected` on its existing tick loop and log/escalate (dashboard status, chat message, structured journal entry) if it flips to permanently false.
- Larger: consider whether `WebSocketBridge`'s retry-exhaustion path should signal `MinecraftAdapter` to attempt one full respawn of the Node process (bounded, e.g. one attempt) before giving up entirely, since "the socket won't reconnect" and "the game process died" are different failure modes with different correct responses.

File as `P1: WebSocketBridge permanent-failure has no escalation or recovery path — IsConnected is read by nothing`. Suggest P1 (not P2/P3) because this is a silent-failure mode for the whole agent, not a cosmetic issue — the same severity class as the other P1 findings already on the board.

---

## Confidence Summary (this delta only)

| Finding | Confidence | Basis |
|---|---|---|
| 1. ItemRegistry missing TSK-0109 fix | 88% | Verified — grep shows zero `OperationCanceledException` guards vs. 2 in the sibling class |
| 2. 11/14 tools re-parse schema per dispatch | 75% | Verified pattern + hot-path call site; performance magnitude not measured |
| 3. WebSocketBridge permanent-deaf has no escalation | 85% | Verified — grep confirms `IsConnected` has zero consumers; retry path confirmed to only re-open the socket, never relaunch Node |

## Not re-verified this pass (disclosed scope limit)

`WebUI.Blazor/Managers/{Recovery,State,DashboardPublisher}Impl.cs`, `WebUI.Blazor/Dashboard/*`, `Agent.Vision`, `Agent.Personality`, and the remaining `MineflayerAdapter` JS modules (`config.js`, `movements.js`, `logger.js`, `gameModeState.js`) were not read line-by-line this round. Given the density of findings already surfaced in two passes over the higher-traffic modules, these are the next-highest-value places to look if a third pass is wanted — flagging so it's an explicit choice, not an oversight.
