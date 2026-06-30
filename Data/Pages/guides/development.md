# Development Guide

Practical notes for contributing to MemorySmith.Agent or running sessions with AI agents in this repo.

## Repository Structure

```
MemorySmith.Agent.slnx   Solution file (VS 2022 .slnx format)
Agent.Core/              Domain models, core interfaces, WorldState, ActionQueue
Agent.Memory/            IMemoryGateway → RestMemoryGateway, World KB support
Agent.Planning/          HTN planner, goals, GoalFactory, decomposers, governor
Agent.Personality/       Chat interpretation, LLM pipeline, rate limiting
Agent.Tools/             ToolDispatcher (single dispatcher) + MCP tool implementations
Agent.Vision/            ISpatialAnalyzer, IVisionModel (Phase 4+)
Agent.Construction/      IArchitect, IBlueprintRepository
Agent.World.Minecraft/   Mineflayer/Node.js adapter, WebSocket bridge
WebUI.Blazor/            Dashboard host (REST API + future Blazor UI), DI root
MemorySmith.Agent.Tests/ NUnit test suite (746+ tests)
MineflayerAdapter/       Node.js Mineflayer bot + logStructured file logger
Data/Pages/              Wiki pages served by MemorySmith
```

## Build Commands

```bash
# Full solution
dotnet restore MemorySmith.Agent.slnx
dotnet build   MemorySmith.Agent.slnx --configuration Release
dotnet test    MemorySmith.Agent.slnx --configuration Release

# Single project
dotnet build Agent.Planning/Agent.Planning.csproj --configuration Release

# Run the web host
dotnet run --project WebUI.Blazor
```

## Sprint Workflow

```
implement → push → CI green (conclusion: success) →
6-seat council review (Data/Pages/council/) → fix blockers → next sprint
```

- No sprint ships with a failing CI or a **blocking** council finding.
- Council review written to `Data/Pages/council/<topic>-council-<date>.md`.
- Workflow: implement → local build/test → push → council review → fix blockers → confirm CI green.

**Council seats:** Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer. Each seat: confidence %, explicit dissent, blocking vs deferred.

## Test Conventions

- All tests use **NUnit 4.6.1** with `[TestFixture]` and `[Test]`.
- `GlobalUsings.cs` in the test project imports `NUnit.Framework` globally.
- Use mock implementations: `MockWorldAdapter`, `MockMemoryGateway`, `MockPlanner`.
- File-scoped helper classes use the `file` modifier to avoid name collisions.
- **AGENTS.md Rule E-1:** Never patch C# verbatim-string files (`""`) via agent intermediary — use `github__create_or_update_file` directly with paramsFile. Verbatim regex safe patch pattern documented in AGENTS.md.

## Test Naming Conventions

```
{Subject}_{Condition}_{ExpectedBehavior}
```

Examples:
- `WorldState_DefaultPosition_IsOrigin`
- `HtnPlanner_GatherWoodGoal_ContainsMineBlockAction`
- `RestMemoryGateway_SearchAsync_PreservesKind`
- `ReplanGovernor_ThreeIdenticalFingerprints_TransitionsToStalled`

## Baseline Test Count

| Version | Passed | Skipped | Notes |
|---------|--------|---------|-------|
| v0.55.0 | 746+ | 10 | CUDA/ONNX skips expected |

The 10 skipped tests are CUDA/ONNX-model-dependent. Any other skip is a regression.

## CI Pipeline

GitHub Actions runs on every push to `main`:

1. `dotnet restore MemorySmith.Agent.slnx`
2. `dotnet build --configuration Release --no-restore`
3. `dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage"`

Coverage artifacts uploaded. Failing tests surface as GitHub annotations via `GitHubActionsTestLogger`.

To diagnose CI failures without admin rights: use `github__pull_request_read method=get_check_runs` plus `...commits/<sha>/check-runs` and `.../check-runs/<id>/annotations` REST endpoints.

## Serilog Structured Logging

Log files are in `logs/`:
- `logs/agent-<date>.log` — human-readable (Debug level, ms precision)
- `logs/agent-structured-<date>.json` — machine-readable JSON with structured properties

See [Logging Guide](logging.md) for key message patterns and log analysis.

## Sandbox Build Notes

If building in the Hyperagent sandbox (proxy-restricted environment):

1. Install .NET 10: `curl -sL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh | bash -s -- --channel 10.0`
2. NuGet requires overriding **BOTH** `HTTPS_PROXY` and `https_proxy` (lowercase) env vars pointing to the auth proxy on port 9081.
3. Run the auth proxy: `python3 authproxy.py &`
4. Build flags: `-p:CopilotSkipCliDownload=true` (skips npm download for GitHub Copilot SDK)
5. Source fetch: `curl -sL https://codeload.github.com/TheMasonX/MemorySmith.Agent/tar.gz/<sha>`

## Adding New Packages to Tests

Test project packages must match MemorySmith versions:
- `Microsoft.NET.Test.Sdk 18.6.0`
- `NUnit 4.6.1`
- `NUnit3TestAdapter 6.2.0`
- `coverlet.collector 10.0.1`
- `GitHubActionsTestLogger 2.4.1`

## Namespace Conventions

| Project | Root namespace |
|---|---|
| `Agent.Core` | `Agent.Core` |
| `Agent.Planning` | `Agent.Planning` |
| `Agent.Planning/Goals/` | `Agent.Planning.Goals` |
| `Agent.Memory` | `Agent.Memory` |
| `Agent.Tools` | `Agent.Tools` |
| `Agent.World.Minecraft` | `Agent.World.Minecraft` |
| `WebUI.Blazor` | `WebUI.Blazor` |
| Tests | `MemorySmith.Agent.Tests` |

**Important:** All `using` directives must appear **before** the file-scoped `namespace` declaration. Never use fully-qualified names like `Agent.Core.Position` inside `MemorySmith.Agent.Tests` — the `Agent` prefix resolves to `MemorySmith.Agent`, not the root.

## Key Coding Rules (from AGENTS.md)

- All timeouts, TTLs, radii → named constants or `*Options` properties (no magic numbers)
- `*Options` classes must be `sealed record`
- Timeouts stored as `int *Seconds`, convert to `TimeSpan` at use site
- Test-injectable delays: optional constructor params with `null = use defaults`
- `TreatWarningsAsErrors = true` — zero warnings policy
