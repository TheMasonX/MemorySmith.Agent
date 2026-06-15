# Council Review: Phase 0 Bootstrap Completion and Phase 1 Kickoff

Date: 2026-06-15

## Decision

Accept Phase 0 as complete. The MemorySmith.Agent repository is bootstrapped with a clean-building solution, all key interfaces defined, a self-documenting wiki, and a passing CI baseline. Proceed immediately to Phase 1 implementation with the prioritization order below.

## Evidence Reviewed

- `MemorySmith.Agent.slnx` — 9-project solution
- `Agent.Core/Interfaces/` — IAgent, IGoal, IPlan, IMemoryGateway, ITool, IWorldAdapter
- `Agent.Planning/HtnPlanner.cs` — stub with documented Phase 3 intent
- `Agent.Tools/ToolEngine.cs` + `ToolRegistry.cs` — functional registry and engine
- `Agent.World.Minecraft/MinecraftAdapter.cs` + `WebSocketBridge.cs` — full stub with protocol comment
- `WebUI.Blazor/Program.cs` — minimal REST API host
- `MemorySmith.Agent.Tests/CoreModelsTests.cs` — 7 passing NUnit tests
- `Data/Pages/` — 10 wiki pages seeded from Executive Summary
- `MineflayerAdapter/index.js` — Node.js adapter stub
- `.github/workflows/ci.yml` — build + test CI
- CI run on commit `ec623e6` — build-and-test: passing (7/7 tests)

## Findings

| Seat | Recommendation | Confidence | Blocking concern |
|---|---|---:|---|
| Architecture Reviewer | The three-context split (Agent Core / MemorySmith / World Adapters) is correct and well-enforced. Deep-module principle is already present in `WebSocketBridge` hiding `ClientWebSocket` complexity. Keep this discipline through Phase 1. | 94% | `MinecraftAdapter._nodeProcess` is unassigned (CS0649 warning). Phase 1 must wire the subprocess launch or the bridge is permanently a stub. |
| Implementation Reviewer | All 8 library projects build clean. Three bugs were caught and fixed: namespace scope collision in tests, missing `using NUnit.Framework`, and `AddOpenApi` without package. No bugs should flow into main; these all came from skeleton generation. Add a `GlobalUsings.cs` to the test project to prevent the NUnit namespace issue in future test files. | 91% | Test coverage is 7 tests over only `Agent.Core` and `Agent.Tools`. `Agent.World.Minecraft` has no tests yet — critical gap for Phase 1. |
| Integration Reviewer | `IMemoryGateway` is defined but has no implementation. Phase 1 requires at minimum a `MockMemoryGateway` for testability and a `RestMemoryGateway` stub. Without it, the agent loop has no memory access even for basic tool dispatch. | 88% | The MemorySmith REST API base URL is configurable in `appsettings.json`; no auth is wired yet. Consider whether Phase 1 needs authenticated memory access or can use a mock. |
| Node.js Reviewer | `MineflayerAdapter/index.js` correctly stubs all the right dispatch cases and uses ES modules. However, `package-lock.json` is absent — CI cannot reliably install `mineflayer` without it. The Node.js adapter has no tests (mocha/jest stubs). Phase 1 should add `npm ci` and a connection smoke test. | 85% | `mineflayer@^4.23.0` is a range specifier; without a lockfile the CI build is non-deterministic. Run `npm install` once locally to generate the lockfile and commit it. |
| Testing Reviewer | The `MemorySmith.Agent.Tests` project structure is correct (NUnit 4.3.2, NUnit3TestAdapter). Adding a `GlobalUsings.cs` with `global using NUnit.Framework;` removes the per-file boilerplate and prevents the missing-using class of bug permanently. Phase 1 should target: `WebSocketBridge` send/receive tests, `ToolEngine` dispatch tests, `MinecraftAdapter` connection tests (using a mock `IWorldAdapter`). | 90% | Coverage report upload is wired in CI but `dotnet-reportgenerator-globaltool` requires network access on the runner. Verify the coverage step doesn't fail on first run with empty output. |
| Roadmap Reviewer | Phase 1 deliverables are well-scoped. Recommend breaking Phase 1 into two sub-milestones: (1a) Wire the WebSocket bridge end-to-end with a mock Minecraft server and `MoveTo` tool; (1b) Add basic Blazor status panel with SignalR push. This prevents the Blazor work from blocking the critical path (the bot connection). | 87% | Phase 2 (MemorySmith integration) should start by implementing `RestMemoryGateway` against the live MemorySmith instance, not a mock, to validate the real search pipeline early. |
| Synthesizer | Phase 0 is solid. The two highest-risk Phase 1 items are the WebSocket bridge (can it actually connect to a running Minecraft server?) and the NuGet proxy issue in the sandbox (solved for restore; build pipeline is fine). Accept and move to Phase 1 immediately, starting with the bot connection sub-milestone. | 93% | The `MinecraftAdapter` subprocess launch is the single highest-risk item in the entire architecture; it should be the first integration test written in Phase 1. |

## Synthesis

**Phase 0 accepted.** The following items are done:
- Solution structure with all 8 projects + test project ✅
- All core interfaces (`IAgent`, `IGoal`, `IPlan`, `IMemoryGateway`, `ITool`, `IWorldAdapter`, `IPlanner`, `ISpatialAnalyzer`, `IVisionModel`, `IArchitect`, `IBlueprintRepository`) ✅
- 10 wiki pages covering the full architecture ✅
- 7 passing NUnit tests for domain models and tool registry ✅
- CI workflow (build + test) green ✅

**Phase 1 priority order:**

1. **Add `GlobalUsings.cs` to test project** — prevents NUnit namespace bugs in new test files.
2. **Wire `MinecraftAdapter.ConnectAsync`** — spawn the Node subprocess, connect `WebSocketBridge` to `ws://localhost:3000`, verify round-trip send/receive.
3. **Add `MoveTo` and `Status` tools** — implement `IToolRegistry` registrations backed by WebSocket commands.
4. **Implement `ActionQueue` polling loop in an `AgentHost`** — dequeue actions and dispatch via `ToolEngine`.
5. **Add `MockMemoryGateway`** — in-memory `IMemoryGateway` implementation for test isolation.
6. **Generate `package-lock.json`** for the Mineflayer adapter and commit it.
7. **Add Blazor status panel** — connect button, status display, SignalR push (non-blocking, can be parallel to above).

## Dissent

- Integration Reviewer notes that using `MockMemoryGateway` for Phase 1 risks deferring real MemorySmith integration too long. Counter-proposal: start `RestMemoryGateway` in Phase 1 even if it's not wired into the agent loop — just validate the search endpoint responds.
- Node.js Reviewer notes the `MineflayerAdapter` should have at minimum a `process.exit(0)` health check endpoint or signal handler so the C# host can detect Node crashes cleanly.

## Acceptance Criteria for Phase 1

- `MinecraftAdapter.ConnectAsync` launches the Node subprocess and connects the WebSocket bridge.
- A `MoveTo(x, y, z)` tool call round-trips: C# → WebSocket → Node → event back to C#.
- `AgentHost.RunAsync` dequeues and dispatches at least one `ActionData` from `ActionQueue` via `ToolEngine`.
- `MockMemoryGateway.SearchAsync` returns predictable results for test assertions.
- CI remains green after each commit to main.
- `package-lock.json` for `MineflayerAdapter/` is committed.

## Open Questions

- Should the agent loop run in `WebUI.Blazor` (hosted service) or in a separate console project (`Agent.Host`) for cleaner separation?
- What Minecraft server version should the initial integration test target? (Vanilla 1.21? Paper?)
- Should `IWorldAdapter.ReceiveEventsAsync` use `IAsyncEnumerable<WorldEvent>` (current) or `event Action<WorldEvent>` (push model)?
- Should the `ToolEngine` maintain a call log from Phase 1 or defer to Phase 3?
