# MemorySmith.Agent — Deep-Dive Audit: Delta Report #7
**Scope:** Continuation of the audit series. This pass covered the remaining unreviewed `Agent.Tools/Tools/*.cs` files (`ChatTool`, `CraftItemTool`, `CreatePageTool`, `FindFlatAreaTool`, `GetStatusTool`, `MineBlockTool`, `WanderTool`), all of `Agent.Vision` (4 files), `Agent.Personality`, and — prompted by a parsing anomaly hit while cross-checking task status — a full-corpus survey of `Data/Tasks/*.json` schema consistency.
**Format:** Deltas only.

---

## Summary

| # | Type | Item | Severity | Confidence |
|---|------|------|----------|------------|
| 1 | New | Sprint 25 P0-A's "safe integer parsing" fix (`TryGetInt32` instead of throwing `GetInt32`) was applied to only 3 of ~9 numeric-argument tools | Medium | 92% |
| 2 | New | `Agent.Vision` (4 files: `WorldVision`, `ISpatialAnalyzer`, `IVisionModel`) is entirely dead scaffolding with zero implementations and zero callers — and `TSK-0204` is scoped/prioritized on the false premise that a working vision pipeline already exists | Medium | 90% |
| 3 | New, meta/tooling | 10 of the earliest `Data/Tasks/*.json` files use PascalCase field names while all 328 others use lowercase — silently invisible to any tooling (including this audit's own scripts, until this pass) written against the lowercase schema | Medium | 98% |
| 4 | New, cosmetic | `GetStatusTool.cs` has a dangling `<see cref="StatusTool"/>` doc-comment reference to a class deleted in Sprint 25 P0-B | Trivial | 95% |

---

## 1 — "Safe integer parsing" fix never generalized beyond `FindFlatAreaTool` (Medium, 92%)

`FindFlatAreaTool.cs`'s own comment documents a deliberate Sprint 25 P0-A fix: *"Safe integer parsing via TryGetInt32 to handle scientific notation gracefully"* — replacing a throwing `GetInt32()` call with `TryGetProperty(...) && x.TryGetInt32(out var v) ? v : default`. Surveying every tool with a numeric argument:

| Tool | Numeric arg(s) | Parsing style |
|---|---|---|
| `FindFlatAreaTool` | `radius`, `minFlatArea` | ✅ Safe (`TryGetInt32`, falls back to default) |
| `QueryBlocksTool` | `x2`, `y2`, `z2` | ✅ Safe |
| `QueryEntitiesTool` | `radius` | ✅ Safe (additionally clamps to a max of 64) |
| `MoveToTool` | `x`, `y`, `z`, `nearestX/Y/Z` | ❌ Throwing `GetInt32()` |
| `PlaceBlockTool` | `x`, `y`, `z` | ❌ Throwing `GetInt32()` |
| `FurnaceTool` | `count` | ❌ Throwing `GetInt32()` |
| `CraftItemTool` | `count` | ❌ Throwing `GetInt32()` |
| `MineBlockTool` | `count` | ❌ Throwing `GetInt32()` |
| `WanderTool` | `radius`, `maxDistanceFromSpawn` | ❌ Throwing `GetInt32()` |

Six of nine tools with numeric arguments still call `JsonElement.GetInt32()` directly with no `TryGetInt32`/try-catch guard. `JsonElement.GetInt32()` throws `InvalidOperationException`/`FormatException` if the LLM emits the value as a JSON string (e.g. `"count": "5"`), a non-integer number (`"count": 5.5`), or in scientific notation for a value where `TryGetInt32` would gracefully coerce/reject but `GetInt32()` does not.

**Actual severity, given prior findings:** confirmed via delta report #4 that `ToolDispatcher.CallAsync` has a Sprint-25-P0-C top-level catch-all, so this does **not** crash the tool-dispatch loop — it converts the exception into a generic `ToolResult(false, ...)` failure instead of the tool gracefully defaulting/proceeding, which is the exact failure mode Sprint 25 P0-A specifically set out to prevent for `FindFlatAreaTool`. The fix was correct and well-reasoned; it just never propagated to its five/six siblings with structurally identical arguments.

**Recommendation:** Generalize the `TryGetInt32`-with-default pattern to `MoveToTool`, `PlaceBlockTool`, `FurnaceTool`, `CraftItemTool`, `MineBlockTool`, and `WanderTool`. Given the repetition, this is also a good opportunity to extract a small shared helper (e.g. `static int GetIntOrDefault(this JsonElement e, string prop, int fallback)` in a shared `Agent.Tools` extensions file) rather than hand-copying the `TryGetProperty && TryGetInt32` idiom into six more files — consolidating the pattern *and* fixing the gap in one pass. No existing task tracks this; `TSK-0332` (MineBlock timeout, Done) and other per-tool tasks touch some of these files for unrelated reasons but don't address argument parsing robustness.

---

## 2 — `Agent.Vision` is entirely dead scaffolding, and `TSK-0204` is scoped against a false premise (Medium, 90%)

The whole `Agent.Vision` project — `WorldVision.cs` (24 lines), `Interfaces/ISpatialAnalyzer.cs`, `Interfaces/IVisionModel.cs` — has **zero implementations and zero callers** anywhere else in the codebase. Confirmed directly:
- `ISpatialAnalyzer` (interface, defines `AnalyzeAsync`) — no class implements it, including `WorldVision` itself (despite `WorldVision`'s doc comment claiming it "feeds into" this interface, they're not actually connected via any inheritance or DI relationship).
- `IVisionModel` (interface, defines `CritiqueAsync` for screenshot-based build critique) — no implementation anywhere; no screenshot-capture mechanism exists in the Mineflayer adapter to even produce the `byte[] screenshotBytes` this interface would need.
- `WorldVision` itself — its two methods (`GetBlockAt`, `GetNearbyEntities`) are never called from `AgentBackgroundService` or anywhere else; it's never registered in `Program.cs`'s DI container.

This is correctly reflected in the backlog as **not yet built** — `TSK-0005` ("Implement SpatialAnalyzer.cs in Agent.Vision," Backlog) explicitly says *"`ISpatialAnalyzer` is defined but `WorldVision` is just a stub"* — accurate, no correction needed there. `TSK-0023` ("Agent Vision," Backlog, Medium) is the top-level "build a vision pipeline" tracking task — also accurately scoped as not-yet-started.

**The gap is in `TSK-0204`** ("Add visual screenshot feedback via existing vision pipeline," Backlog, Low): its own problem statement asserts *"The agent can see the Minecraft world (`Agent.Vision` exists)... Uses existing vision pipeline"* as its scoping premise. This is not accurate — `Agent.Vision` *existing as a namespace with two interface stubs and an unimplemented class* is not the same as a working vision pipeline the task could build on top of. As scoped (`Low` priority, framed as "just add a chat command on top of what's there"), a developer picking this up would discover mid-task that they first need to: implement screenshot capture in the Mineflayer adapter (not present anywhere today), implement `IVisionModel` against Ollama/OpenAI Vision (currently zero implementations), and implement `ISpatialAnalyzer` or bypass it — i.e., most of `TSK-0005`'s and `TSK-0023`'s scope — before the actual "add a `ShowMe` chat command" work in `TSK-0204` becomes possible at all. Its `Low` priority and narrow-sounding scope likely understate the real effort by a wide margin.

**Recommendation:** Correct `TSK-0204`'s problem statement to drop the "existing vision pipeline" premise, and either (a) mark it explicitly blocked-by `TSK-0005`/`TSK-0023`, or (b) fold it into `TSK-0023` as an acceptance-criterion / end-to-end scenario rather than a standalone Low-priority task, since it cannot be meaningfully started independently. No other task currently notes this dependency.

---

## 3 — 10 early `Data/Tasks/*.json` files use PascalCase keys, invisible to lowercase-schema tooling (Medium, 98%)

Full-corpus survey (`glob` over all 338 `Data/Tasks/*.json` files, checking for `"status"` vs. `"Status"` as the key): **328 files use lowercase field names** (`id`, `title`, `status`, `priority`, `description`, ...) and **10 files use PascalCase** (`Id`, `Title`, `Status`, `Priority`, `Description`, ...):

```
tsk-0001-implement-websocket-bridge-full.json
tsk-0002-implement-mineflayer-adapter-full.json
tsk-0005-implement-spatial-analyzer.json
tsk-0006-add-microsoft-extensions-ai-ollama.json
tsk-0007-implement-goap-fallback.json
tsk-0008-add-blazor-status-panel-signalr.json
tsk-0009-minecraft-version-block-id-config.json
tsk-0010-generic-gather-goal-arbitrary-items.json
tsk-0012-deploy-minecraft-memorysmith-wiki.json
tsk-0013-game-inventory-query-tool.json
```

All ten are among the very earliest task IDs (`0001`–`0013`), strongly suggesting the tracking system's JSON schema was changed from PascalCase to lowercase early in the project's life, and these ten original files were never migrated to match. Confirmed the data inside them is otherwise well-formed and complete (spot-checked `tsk-0005`: full `Title`, `Description`, `Status: "Backlog"`, `Priority: "Medium"`, labels, timestamps — just under the old key names).

**Why this matters:** any script, dashboard, or automation — including every status/priority lookup performed by *this audit series* across seven reports — that parses these files expecting lowercase keys (the dominant, 328-file convention) will silently get `None`/empty/default values for these 10 files instead of an error, because `dict.get('status')` on a dict that only has `'Status'` returns `None` rather than raising `KeyError`. That's a footgun by omission: these 10 tasks would be systematically excluded from any "show me all Backlog tasks" or "count tasks by priority" report without anyone noticing, since nothing fails loudly. Given `tsk-0001`/`tsk-0002` ("implement websocket bridge full" / "implement mineflayer adapter full") sound foundational and are presumably long since superseded by later, more specific tasks, low-severity in practice — but `tsk-0005`/`tsk-0007`/`tsk-0009`/`tsk-0010` are all still marked `Backlog` in their native schema and would currently be invisible to any lowercase-schema backlog review.

**Recommendation:** Migrate these 10 files to the lowercase schema for consistency (mechanical, low-risk — a one-time script rewriting the 10 files' top-level keys). Given how easy this is to silently reintroduce (e.g., a future bulk-import script defaulting to PascalCase), consider a lightweight CI check that validates all `Data/Tasks/*.json` files conform to one schema. No existing task tracks this.

---

## 4 — Dangling `<see cref="StatusTool"/>` in `GetStatusTool.cs` (Trivial, 95%)

```csharp
// GetStatusTool.cs:5-7
/// <summary>
/// Compatibility alias for plans that dispatch "GetStatus".
/// Sends the same world action as <see cref="StatusTool"/>.
/// </summary>
```

`StatusTool` was deleted under Sprint 25 P0-B as a duplicate of `GetStatusTool` — confirmed via `Program.cs:281` (*"Sprint 25 P0-B: StatusTool deleted (duplicate of GetStatusTool)"*) and `ToolDispatcher.cs:54-55`'s own comment referencing the same cleanup. The alias registration itself (`d.Register("Status", new GetStatusTool(world))`) was correctly updated at the time — only this one `<see cref>` in the surviving sibling file's doc comment was missed, and now references a type that no longer exists. If the project builds with `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (common for library projects with XML-doc-driven tooling), this would produce a `CS1574` "XML comment has cref attribute that could not be resolved" warning on every build.

**Recommendation:** Update the comment to describe the current reality directly (e.g., *"Registered under both `GetStatus` and `Status` — see `Program.cs` tool registration; `StatusTool` was removed as a duplicate under Sprint 25 P0-B."*) rather than pointing at a deleted type.

---

## Assumptions & Open Questions (this pass)

1. Finding 3's severity assumes tooling in active use actually parses `Data/Tasks/*.json` directly with a lowercase-key assumption (as this audit's own scripts have done) — if the "real" tracking system reads these files through a proper deserializer with case-insensitive property matching (common in .NET `System.Text.Json` with `PropertyNameCaseInsensitive = true`), the practical impact would be limited to non-.NET tooling (Python scripts, shell greps, this audit). Worth a quick check of whatever actually renders the task board, if that's a separate codebase not included in this repository snapshot.
2. Did not verify whether the project's `.csproj` files actually set `<GenerateDocumentationFile>true</GenerateDocumentationFile>` for `Agent.Tools` — Finding 4's "produces a build warning" claim is based on standard C# XML-doc behavior when that flag is set; if it isn't set for this specific project, the dangling `cref` is silent (still worth fixing for reader clarity, just without the compiler-warning angle).
