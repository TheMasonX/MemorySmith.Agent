# Development Guide

Practical notes for contributing to MemorySmith.Agent or running sessions with AI agents in this repo.

## Repository structure

```
MemorySmith.Agent.slnx   Solution file (VS 2022 .slnx format)
Agent.Core/              Domain models, core interfaces
Agent.Memory/            IMemoryGateway → RestMemoryGateway
Agent.Planning/          HTN/GOAP planner, goals, task library
Agent.Personality/       Agent profile, voice (Phase 4+)
Agent.Tools/             MCP tool registry and implementations
Agent.Vision/            ISpatialAnalyzer, IVisionModel (Phase 4+)
Agent.Construction/      IArchitect, IBlueprintRepository (Phase 3+)
Agent.World.Minecraft/   Mineflayer/Node.js adapter
WebUI.Blazor/            Dashboard host (REST API + future Blazor UI)
MemorySmith.Agent.Tests/ NUnit test suite
MineflayerAdapter/       Node.js Mineflayer bot
Data/Pages/              Wiki pages served by MemorySmith
```

## Build commands

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

## Test conventions

- All tests use **NUnit 4.6.1** with `[TestFixture]` and `[Test]`.
- `GlobalUsings.cs` in the test project imports `NUnit.Framework` globally.
- Use mock implementations: `MockWorldAdapter`, `MockMemoryGateway`, `MockPlanner`.
- File-scoped helper classes use the `file` modifier to avoid name collisions.
- Never use raw string literals with JSON + curly braces when content will be pushed via API — use regular escaped strings instead (prevents subagent double-encoding).

## Test naming conventions

```
{Subject}_{Condition}_{ExpectedBehavior}
```

Examples:
- `WorldState_DefaultPosition_IsOrigin`
- `HtnPlanner_GatherWoodGoal_ContainsMineBlockAction`
- `RestMemoryGateway_SearchAsync_PreservesKind`

## CI pipeline

GitHub Actions runs on every push to `main` / `feature/**`:

1. `dotnet restore MemorySmith.Agent.slnx`
2. `dotnet build --configuration Release --no-restore`
3. `dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage"`

Coverage artifacts are uploaded. Failing tests appear as GitHub annotations via `GitHubActionsTestLogger`.

## Sandbox build notes

If building in the Hyperagent sandbox (proxy-restricted environment):

1. Install .NET 10: `curl -sL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh | bash -s -- --channel 10.0`
2. NuGet requires overriding BOTH `HTTPS_PROXY` and `https_proxy` (lowercase) env vars pointing to the auth proxy on port 9081.
3. Run the auth proxy: `python3 authproxy.py &`
4. Build flags: `-p:CopilotSkipCliDownload=true` (skips npm download for GitHub Copilot SDK)

## Adding new packages to tests

Test project packages must match MemorySmith versions:
- `Microsoft.NET.Test.Sdk 18.6.0`
- `NUnit 4.6.1`
- `NUnit3TestAdapter 6.2.0`
- `coverlet.collector 10.0.1`
- `GitHubActionsTestLogger 2.4.1`

## Namespace conventions

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

## Council review process

High-impact architectural decisions use the MemorySmith council review format (see `Data/Pages/council/`). The process runs 6 seats (Source-Grounded Archivist, Data Model Architect, Retrieval Specialist, Human Learning Advocate, Skeptical Reviewer, Synthesizer) with explicit dissent and acceptance criteria.

When to run a council review:
- New bounded context or project added
- Interface signature changes
- Integration pattern changes (memory gateway, world adapter)
- Before starting a new phase
