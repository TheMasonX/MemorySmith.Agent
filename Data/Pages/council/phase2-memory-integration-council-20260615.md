# Council Review: Phase 2 Memory Integration Architecture

Date: 2026-06-15

## Decision

Accept Phase 2 implementation as functional. The REST-based IMemoryGateway, DI tool factory, and agent loop in WebUI.Blazor provide a sound Phase 2 foundation. Three issues must be resolved before Phase 3 planner integration; two are Phase 3 concerns only.

## Evidence Reviewed

- `Agent.Core/Interfaces/IMemoryGateway.cs` — 4-method interface
- `Agent.Memory/RestMemoryGateway.cs` — HTTP client calling MemorySmith REST API
- `Agent.Memory/RestMemoryGatewayOptions.cs` — config record
- `WebUI.Blazor/Program.cs` — DI wiring with Agent:Enabled guard
- `Agent.Tools/Tools/SearchMemoryTool.cs`, `GetPageTool.cs`, `CreatePageTool.cs`
- `Agent.World.Minecraft/MinecraftAdapter.cs` — subprocess + WebSocket bridge
- `WebUI.Blazor/AgentBackgroundService.cs` — agent loop
- MemorySmith source: `MemorySmith.App/Controllers/SearchController.cs`, `PagesController.cs`
- `Data/Pages/memory.md` — IMemoryGateway documentation
- `Data/Pages/decisions.md` — D-002 (MemorySmith as memory), D-005 (Microsoft.Extensions.AI)
- Previous council: `Data/Pages/council/phase0-bootstrap-phase1-kickoff-council-20260615.md`
- Local test run: 24/24 passing (7 domain, 7 memory gateway, 4 tool engine, 6 world adapter)

## Findings

| Seat | Recommendation | Confidence | Blocking Concern |
|---|---|---:|---|
| Source-Grounded Archivist | REST endpoints match MemorySmith source. However, unified search returns both `kind=memory` (uses memory UUID) and `kind=page` (uses page slug). These IDs are NOT interchangeable: calling `GetPageAsync(memoryId)` returns 404. | 94% | SearchAsync conflates memory IDs and page slugs in SearchResult.PageId — callers cannot safely call GetPageAsync on any SearchResult without checking Kind first. |
| Data Model Architect | IMemoryGateway is well-scoped. UpdatePageAsync round-trips a GET to preserve the title before PUT; if the page doesn't exist, the GET returns null and the title defaults to pageId, which is wrong. `ToSlug(title)` is also naive for special chars and long titles. | 88% | UpdatePageAsync will produce wrong titles for non-existent pages. Recommend exposing `UpsertPageAsync(pageId, title, content)` or requiring callers to own the title. |
| Retrieval Specialist | SearchMemoryTool wraps SearchAsync correctly. Pages always get Score=0.0 (null from SearchController) while memories have BM25/embedding scores. Pages will rank below memories even when more relevant. | 91% | Pages with Score=0.0 will be consistently under-ranked. SearchResult ordering from MemorySmith is already correct (server-side); gateway should preserve it rather than callers relying on Score. |
| Human Learning Advocate | Agent:Enabled=false default is correct. GetPageTool returns full markdown — useful but verbose. CreatePageTool auto-generates slug from title and returns pageId in Data dict, which agents cannot easily inspect. | 85% | Non-blocking: surface pageId more visibly in the ToolResult.Message for usability. |
| Skeptical Reviewer | DI singleton factory mutates ToolRegistry during resolution; safe but surprising. More critically: `new HttpClient()` in Program.cs bypasses IHttpClientFactory — no connection pooling, no resilience policies. Acceptable for Phase 2 single-agent; not for Phase 3 concurrent planner calls. | 87% | Switch to AddHttpClient<RestMemoryGateway>() before Phase 3 planner integration to prevent socket exhaustion. |
| Synthesizer | Phase 2 architecture is functional. Three items must be addressed before Phase 3: (1) search result kind-disambiguation, (2) update-without-existing-title bug, (3) IHttpClientFactory adoption. Score=0.0 for pages and slug generation quality are Phase 3 concerns, not blockers today. | 92% | Architecture is Phase-2-ready; three blocking items must be fixed before Phase 3. |

## Synthesis

**Phase 2 accepted** with three pre-Phase-3 gates:

**Fix before Phase 3 (required):**
1. **SearchResult kind-disambiguation** — Add `Kind` field to `SearchResult` (or a separate `MemorySearchResult` and `PageSearchResult` discriminated union) so callers can route `kind=page` results to `GetPageAsync` and `kind=memory` results to a future `GetMemoryAsync`. The simplest fix: add `string Kind` to `SearchResult` and populate it from the hit.
2. **UpdatePageAsync title bug** — When the page doesn't exist, fall back to `pageId` as the title gracefully, OR change the pattern: accept `title` as an optional parameter in `UpdatePageAsync(string pageId, string content, string? title = null)`.
3. **IHttpClientFactory adoption** — Replace `new HttpClient()` in `Program.cs` with `builder.Services.AddHttpClient<RestMemoryGateway>((sp, http) => { ... })` before enabling the planner, to prevent socket exhaustion under concurrent LLM+memory calls.

**Defer to Phase 3:**
- Score=0.0 for pages in unified search — add a page-specific relevance signal (e.g. title match boost) or use a separate `SearchPages` endpoint.
- `ToSlug(title)` quality — improve to handle CJK, emojis, very long titles, and slugs that collide with existing pages.

## Dissent

- Data Model Architect prefers a full `IMemoryGateway` interface revision (adding `UpsertPageAsync` with explicit title) over the nullable-title workaround. The Skeptical Reviewer agrees that the interface should be tighter. The Synthesizer defers this to Phase 3 to avoid blocking current progress but acknowledges it as technical debt.
- Retrieval Specialist notes that the ordering issue (pages at score 0.0) could mislead the Phase 3 planner significantly if it uses SearchMemory to find blueprints. Recommends tagging this as a Phase 3 Day 1 fix.

## Acceptance Criteria for Phase 3 Entry

- [ ] `SearchResult` includes `Kind` field populated from unified search `kind` property
- [ ] `UpdatePageAsync` handles missing-page case without producing wrong title
- [ ] `Program.cs` uses `IHttpClientFactory` (or equivalent pooled HttpClient)
- [ ] CI remains green after these fixes (24+ tests passing)

## Open Questions

- Should `IMemoryGateway` be split into `IPageGateway` and `IMemoryRecordGateway` to model the two distinct MemorySmith entity types cleanly?
- Should the agent use MCP tool calls to MemorySmith (via `McpController`) rather than raw REST? The MCP pattern would allow the LLM to call `SearchMemory` as a native tool without the intermediary `SearchMemoryTool` wrapper.
- When Phase 3 adds the planner, should `RestMemoryGateway` be injected into `HtnPlanner` directly, or should the planner only use tools (keeping memory access tool-mediated)?
