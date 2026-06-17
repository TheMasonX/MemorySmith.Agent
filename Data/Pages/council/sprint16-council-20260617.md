# MemorySmith Council Review — Sprint 16
**Date:** 2026-06-17  
**Branch:** `sprint-5-tool-safety` (PR #1)  
**Head commit reviewed:** `a420cd9` (dead-code fix — LocalKnowledgeResolver)  
**CI status (02b9ba71):** ✅ build-and-test: success  
**Seats:** Source-Grounded Archivist · Data Model Architect · Retrieval Specialist · Human Learning Advocate · Skeptical Reviewer · Synthesizer  
**Additional:** Anonymous Peer Review

---

## Sprint 16 changes under review

| File | Change |
|------|--------|
| `Agent.Planning/Router/PlannerRouter.cs` | P0: [IMPLEMENTED]/[ASPIRATIONAL] XML docs on all `PlannerStrategy` enum values and `Select()` |
| `Data/Pages/Architecture/planner-routing-status-20260617.md` | P0: new architecture inventory for planning layer |
| `Agent.Memory/IKnowledgeResolver.cs` | P1: new interface + `KnowledgeQuery`, `KnowledgeResult`, `KnowledgeCandidate`, `CandidateType` |
| `Agent.Memory/LocalKnowledgeResolver.cs` | P1: two-source stub (IItemRegistry + IMemoryGateway, lexical-first); dead-code fallback removed in follow-up |
| `WebUI.Blazor/Program.cs` | P1: `IKnowledgeResolver` DI registration + `GET /api/agent/resolve` endpoint |
| `MemorySmith.Agent.Tests/KnowledgeResolverTests.cs` | P1: 8 new tests |
| `Agent.Planning/HtnTaskLibrary.cs` | P2: extract `AddCraftingTableIfNeeded` from `DecomposeCraftItem` |
| `Data/Pages/Tasks/phase6-tasks.md` | Sprint 15 + Sprint 16 tracking rows added |

---

## Seat 1 — Source-Grounded Archivist
**Confidence: 0.94**

**P0 — PlannerRouter XML docs:** The annotations accurately describe the codebase state. `PlannerStrategy.Goap` and `PlannerStrategy.LlmAssisted` are declared in the enum but their values are never read in `Select()`. The XML docs now say so explicitly with `"No code path in PlannerRouter.Select currently reads or routes to this value."` This is verifiable in the source — the `Select` method has exactly two branches: `registry.Find` check → `DecomposerPlanner`, fallback → `htnPlanner`. Confirmed accurate. ✓

**P0 — Architecture inventory doc:** Correctly lists the four strategy values with status. The "key files" table matches the actual file locations. The "how to add a new decomposer" instructions are consistent with the established pattern (Program.cs wiring + `CanHandle` prefix convention). ✓

**P2 — AddCraftingTableIfNeeded refactor:** The extracted method preserves all original logic identically. Guard clauses are: `RequiresCraftingTable.Contains(itemId)` then `crafting_table > 0 → return`. Body: `oak_planks < 4` guard → optionally mine oak_log (if `oak_log < 1`) → craft planks → craft table. Matches the original inline code exactly. ✓

**Dead-code fix:** The original code computed `queryAsId = normalizedId` (same formula), making the `queryAsId != normalizedId` condition always false. Removal is correct. Future alias support should be a separate PR when `IItemRegistry.GetByAlias` exists. ✓

---

## Seat 2 — Data Model Architect
**Confidence: 0.92**

**IKnowledgeResolver interface design:** The type hierarchy is clean for a Phase 7-B stub:
- `KnowledgeQuery` — value object with defaults. `CandidateType[]? Types = null` (optional filter) is the right nullability choice.
- `KnowledgeResult.Best` — convenience property; `Candidates.Count > 0 ? Candidates[0] : null` is safe.
- `CandidateType` enum covers all necessary categories for the current two sources. `Craftable` and `Smeltable` are distinguished correctly.
- `KnowledgeCandidate.Confidence: float` — float precision is sufficient for 0–1 ranking scores.

**ClassifySpec heuristic correctness:**
| Item | RequiresSmelting | SourceBlocks | Expected | Result |
|------|-----------------|--------------|----------|--------|
| oak_log | false | [oak_log, birch_log, ...] | DirectMineable | ✓ (Contains("oak_log")) |
| oak_planks | false | [oak_log] | Craftable | ✓ (oak_log ≠ oak_planks, count>0) |
| iron_ingot | true | [iron_ore] | Smeltable | ✓ (RequiresSmelting=true) |
| diamond | false | [diamond_ore] | ❓ | Craftable (SourceBlocks=[diamond_ore] ≠ "diamond") |

**Concern (deferred):** `diamond` would classify as `Craftable` since SourceBlocks=[diamond_ore] doesn't contain "diamond". In Minecraft, diamond is directly mined from diamond_ore — it should be `DirectMineable`. The heuristic of `SourceBlocks.Contains(ItemId)` is an approximation. For items where the drop differs from the block name (diamond from diamond_ore, coal from coal_ore), the heuristic misclassifies. This is a wiki data design issue — once `item_registry/diamond.md` has `source_blocks: diamond_ore` and a new `yields_self: true` field is introduced, the classifier can be improved. Not blocking; records as D1.

**LocalKnowledgeResolver confidence constants:** `RegistryExactMatchConfidence = 0.95f` and `GatewaySearchBaseConfidence = 0.60f` are conservative and reasonable for a stub. The multiplication `0.60 × result.Score` means a score=0.5 gateway result yields confidence=0.30, which sits below the default threshold=0.0 but above nothing — fine. ✓

---

## Seat 3 — Retrieval Specialist
**Confidence: 0.93**

**End-to-end retrieval walkthrough — `GET /api/agent/resolve?q=oak_log`:**
```
query = KnowledgeQuery("oak_log", Types=null, Threshold=0.0, TopN=5)
normalizedId = "oak_log"
registry.GetAsync("oak_log") → ItemSpec(ItemId="oak_log", SourceBlocks=[oak_log,...])
  → KnowledgeCandidate(Id="oak_log", Type=DirectMineable, Confidence=0.95)
candidates.Count (1) < TopN (5) → SearchAsync("oak_log")
  → (depends on wiki state; returns 0–N wiki pages)
Result: Best = {Id="oak_log", Type=DirectMineable, Confidence=0.95}
```
Correct. Registry hit dominates; wiki search fills remaining slots. ✓

**End-to-end — `GET /api/agent/resolve?q=iron+ore&types=WikiPage`:**
```
normalizedId = "iron_ore"
registry.GetAsync("iron_ore") → null (no item-registry/iron-ore wiki page in current setup)
SearchAsync("iron ore") → [SearchResult("ore-guide-1", 0.85, ...) ...]
  → KnowledgeCandidate(Id="ore-guide-1", Type=WikiPage, Confidence=0.51)
Type filter: [WikiPage] → passes
Result: candidates = [{ore-guide-1, WikiPage, 0.51}]
```
✓ 

**Concern (deferred):** The endpoint uses raw `query.Query` for `SearchAsync` (not the normalized form). So `q=iron_ore` sends "iron_ore" to the gateway search while the registry lookup also uses "iron_ore". This is correct — the registry benefits from normalization but the wiki search should use the human-readable form. However, if the user typed "iron ore" (with space), `SearchAsync("iron ore")` is sent to the gateway. This is the right behavior for semantic search (the wiki search engine handles natural language). D2 — document this distinction in the resolver.

**Confidence calculation:** `0.60f * (float)result.Score` — `result.Score` is `double` in IMemoryGateway interface. The cast `(float)result.Score` is precision-narrowing but acceptable for a ranking score. ✓

---

## Seat 4 — Human Learning Advocate
**Confidence: 0.96**

**User-facing impact of Sprint 16:**

| Before Sprint 16 | After Sprint 16 |
|-----------------|----------------|
| PlannerRouter had undocumented GOAP/LLM placeholders confusing future agents | Enum values are explicitly annotated as [IMPLEMENTED] or [ASPIRATIONAL] |
| No single-entry point to ask "what is X?" | `GET /api/agent/resolve?q=oak_log` returns ranked knowledge candidates |
| DecomposeCraftItem emitted MineBlock(oak_log) visually mixed with iron_ore pre-gather | MineBlock(oak_log) is now visually separated under AddCraftingTableIfNeeded |

**Knowledge resolver end-user benefit:** Dashboard integrations, future UI panels, and agent commands ("what do I have for oak_log?") can now call a single endpoint instead of knowing about 4 separate lookup sources. Even as a stub, it establishes the contract. ✓

**Dashboard endpoint note:** The `/api/agent/resolve` endpoint returns clean JSON with `candidateCount`, `wasAmbiguous`, `best`, and `candidates` array. The `wasAmbiguous` flag is immediately useful for UI "did you mean?" interactions. ✓

---

## Seat 5 — Skeptical Reviewer
**Confidence: 0.89**

**Concern 1 — ClassifySpec heuristic (see Seat 2 D1):** Misclassifies items where the drop differs from the block (e.g., diamond). Non-blocking because the resolver is stub-quality, but should be tracked. Confidence: 0.85.

**Concern 2 — No endpoint tests:** `KnowledgeResolverTests.cs` tests `LocalKnowledgeResolver` directly but there are no integration tests for the `/api/agent/resolve` HTTP endpoint. The endpoint has argument parsing logic (the `types` comma-split, the `Math.Max(1, topN)` guard) that is untested. Non-blocking — endpoint testing is typically done manually or with WebApplicationFactory tests, which are not present in this test suite for other endpoints either. D3.

**Concern 3 — `string? q` nullable parameter in endpoint:** The endpoint declares `string? q` but `KnowledgeQuery` takes `string`. The null guard `if (string.IsNullOrWhiteSpace(q)) return ...` is present and C# flow analysis makes `q` non-null after that guard. ✓ No CS8604 warning. ✓

**Concern 4 — `topN: Math.Max(1, topN)`:** The guard against topN=0 is present. ✓ What about negative topN? `Math.Max(1, -5) = 1` — handled. ✓

**Concern 5 — Dead-code removal correctness:** The removed block was:
```csharp
var queryAsId = query.Query.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
if (spec is null && query.Query.Length >= 3 && queryAsId != normalizedId)
```
Since `NormalizeToItemId(query.Query)` and the `queryAsId` formula are identical, `normalizedId == queryAsId` always. The condition never fires. Removal is correct. ✓

**Verdict:** No blocking findings. Sprint 16 is sound. All deferred items are correctness-preserving improvements that don't affect the current feature boundary.

---

## Seat 6 — Synthesizer
**Confidence: 0.94**

**Blocking findings: NONE**

**Deferred findings:**
| ID | Finding | Priority |
|----|---------|----------|
| D1 | `ClassifySpec` misclassifies items where drop ≠ block name (diamond, coal). Needs `yields_self` field in wiki spec or a direct lookup against `CommonMinecraftBlocks.DirectMineBlocks` | P2 — Sprint 17 |
| D2 | Document that `SearchAsync` receives the raw un-normalized query (intentional for semantic search) — add comment in `LocalKnowledgeResolver` | P3 |
| D3 | No integration test for `/api/agent/resolve` HTTP endpoint | P3 — Sprint 17+ |
| D4 | Resolver should add `CandidateType.WorldFact` as a third source (WorldState.Facts) in Phase 7-B growth | P2 — Sprint 17 |
| D5 | Alias-based lookup: once `IItemRegistry` gains alias support, restore a display-name fallback path in `LocalKnowledgeResolver` | P2 — Phase 7-B |

**Acceptance criteria — all met:**
| # | Criterion | Status |
|---|-----------|--------|
| AC1 | `PlannerStrategy.Goap` and `LlmAssisted` annotated as [ASPIRATIONAL] in XML docs | CONFIRMED |
| AC2 | `Select()` documents its two implemented routing paths | CONFIRMED |
| AC3 | `planner-routing-status-20260617.md` accurately describes all four strategy paths | CONFIRMED |
| AC4 | `IKnowledgeResolver` interface with `KnowledgeQuery/Result/Candidate/CandidateType` types exists | CONFIRMED |
| AC5 | `LocalKnowledgeResolver` wraps two sources: IItemRegistry + IMemoryGateway | CONFIRMED |
| AC6 | `GET /api/agent/resolve?q=` endpoint registered; returns ranked candidates | CONFIRMED |
| AC7 | 8 unit tests covering registry hit, gateway fallback, smeltable, craftable, TopN, threshold, type filter, ambiguity, empty | CONFIRMED |
| AC8 | `AddCraftingTableIfNeeded` extracted from `DecomposeCraftItem`; behavior unchanged | CONFIRMED |
| AC9 | Dead-code display-name fallback removed | CONFIRMED |
| AC10 | CI green (build-and-test: success on 02b9ba71) | CONFIRMED |

**Council decision: APPROVED — no blockers. Sprint 16 implementation complete.**

---

## Anonymous Peer Review

**Reviewer: Anonymous (external)**  
**Confidence in overall direction: 0.92**

**Scope discipline:** The Sprint 16 reviewer correctly held to the two-source constraint recommended by the prior anonymous review ("one interface, two concrete sources, no graph traversal"). The `LocalKnowledgeResolver` does exactly that. No creep. ✓

**What I would add:** D1 (ClassifySpec heuristic) is the right call — the current heuristic works for the common case (oak_log, iron_ingot) but will confuse callers who ask for "diamond" and get back `Craftable` instead of `DirectMineable`. This should be addressed in Sprint 17 before the resolver is promoted from stub status.

**What I would caution against:** The `wasAmbiguous` flag is exposed to the API consumer now. Before using it to auto-disambiguate in any UI or agent behavior, define what "ambiguous" means for the agent loop. As implemented, ambiguity of two WikiPage results at scores 1.0 and 0.97 is meaningless to the planner — but ambiguity between a `DirectMineable` and a `WikiPage` at the same confidence would be meaningful. Consider splitting the flag into `wasAmbiguousAmongType` vs. `wasAmbiguousAcrossTypes` in Phase 7-B when the resolver is being actively used.

**Overall rating: APPROVE. Sprint 17 should address D1 before calling the resolver production-ready.**
