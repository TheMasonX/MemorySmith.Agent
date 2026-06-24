# Council Review: Sprint 45 Wave B Implementation Approach

## Decision

Implement TSK-0094 (blueprint validation) and TSK-0092 (null cache TTL) immediately as P0; implement TSK-0089 (nav contract) after recording an explicit approach decision; defer TSK-0093 (ParseItemSpec) to Sprint 46+; gather evidence for TSK-0096 (mining double-counting) before proceeding.

## Evidence Reviewed

- Code evidence collected by Explore subagent — full source context for all 5 tasks
- `Agent.Planning/IntentManager.cs` — navigate case requiring all 3 coords
- `Agent.Memory/MemorySmithItemRegistry.cs` — null cache TTL (60s default), ParseItemSpec
- `Agent.Memory/MemorySmithBlueprintRepository.cs` — GetAsync returns blueprint without Id validation
- `Agent.Construction/BlueprintParser.cs` — Parse returns empty Blueprint for malformed input
- `Agent.Core/WorldStateProjector.cs` — ApplyBlockMined and ApplyItemCollected both increment inventory
- `MemorySmith.Agent.Tests/ItemSpecParserTests.cs` — 16 test callers of ParseItemSpec
- `MemorySmith.Agent.Tests/ItemRegistryTests.cs` — existing null-cache test
- Previous council report: `Data/Pages/council/audit-task-prioritization-council-2026-06-24.md`
- Wave A delivery evidence: 641/641 tests, 0 build warnings

## Findings

| Seat | Recommendation | Confidence | Blocking concern |
|---|---|---|---|
| **Source-Grounded Archivist** | TSK-0094 and TSK-0092 are safe, evidence-backed, minimal-risk — implement immediately. TSK-0089 needs an approach decision first. TSK-0096 is an accepted tradeoff, not a bug. TSK-0093 is a breaking change with no production benefit — defer. | 0.90 | TSK-0089 has a dual-path issue (fast-path works, LLM path silent-fails). Must decide: fix IntentManager or fix prompt. |
| **Data Model Architect** | TSK-0094 guard is type-safe and consistent with SearchAsync pattern. TSK-0092 is a simple config addition. TSK-0093 changes a public API — not worth the regression risk. TSK-0096 model already has acknowledgment in code. | 0.88 | ParseItemSpec being `public static` means any return-type change affects external consumers. Must assess whether anyone outside the repo calls it. |
| **Retrieval Specialist** | TSK-0092 (null TTL) directly affects retrieval reliability — a transient outage caches null for 60s. Promote to P0. TSK-0094 protects goal creation from malformed data. TSK-0089 silent-fails navigate intents on the LLM path — P1. | 0.85 | 60s null cache means items added to wiki are invisible for a full minute. 5s default is a safe middle ground. |
| **Human Learning Advocate** | TSK-0094 (~5min) and TSK-0092 (~5min) are quick wins. TSK-0089 (~30min) is straightforward once decision is made. TSK-0093 (~2-3 hours + test updates) is disproportionate to value. TSK-0096 (~2-4 hours) needs evidence of real-world impact first. | 0.82 | Without effort estimates tasks look equal priority. Tag clearly: "minutes" vs "hours" vs "deferred". |
| **Skeptical Reviewer** | TSK-0094 guard is correct but must also validate that blocks/materials exist, not just Id. TSK-0092: if we uncache null too aggressively, missing items hammer the API. 5s default with exponential backoff would be better. TSK-0093 is a textbook "gold-plating" risk. TSK-0096: the existing code comment IS the decision — don't reopen it without evidence. | 0.78 | TSK-0094 scope creep risk: "while we're here, validate materials too" expands scope. Keep it focused on Id/Name only. TSK-0092: 5s null TTL + no caching on HttpRequestException avoids transient-outage caching. |
| **Synthesizer** | **P0 now:** TSK-0094, TSK-0092. **P1 after decision:** TSK-0089. **Research gate:** TSK-0096. **Defer:** TSK-0093. Wave B is achievable in Sprint 45 if sequenced correctly. | 0.88 | TSK-0093 is the only high-regression item — deferring it removes the biggest risk from Wave B. |
| **Anon Peer Reviewer** | Wave A looks solid. TSK-0091 fire-and-forget is safe (ActionQueue is thread-safe). TSK-0088 try/catch scope is appropriate. For Wave B: TSK-0094 and TSK-0092 should be first. TSK-0093 is over-engineering. TSK-0096 is a design constraint, not a bug. | 0.85 | No blocking concerns on Wave A. Wave B plan is sound with the proposed prioritization. |

## Synthesis

### Implement now (Wave B — immediate)

1. **TSK-0094 (P0)**: Add `if (string.IsNullOrEmpty(blueprint.Id)) return null;` in `MemorySmithBlueprintRepository.GetAsync` after parsing. Matches existing `SearchAsync` pattern. Test: blueprint with missing `# Heading` returns null.
   - Do NOT expand scope to validate materials/blocks — keep focused.

2. **TSK-0092 (P0)**: Add `NullCacheTtlSeconds` option to `RestMemoryGatewayOptions` (default 5s). Use it in `MemorySmithItemRegistry.GetAsync` when caching null results. Existing `ItemCacheTtlSeconds = 0` (caching disabled) also disables null caching.

### Implement after decision (Wave B — decision-gated)

3. **TSK-0089 (P1)**: Choose option (a) — update LLM prompt to always emit explicit coords. This is lower risk than adding player-position resolution to IntentManager. Record decision as a task comment before coding.

### Evidence-gated

4. **TSK-0096 (P2)**: Gather evidence from logs of actual double-count incidents. If no evidence of real-world harm, close as "won't fix — documented design constraint."

### Deferred

5. **TSK-0093 (P3)**: Defer to Sprint 46+. No concrete consumer exists that needs to distinguish "not found" from "malformed." Breaking 16 test callers for zero production benefit is not justified.

## Dissent

**Disagreement on TSK-0089 approach.** The Source-Grounded Archivist and previous council prefer prompt-only fix (lower risk). The Data Model Architect prefers runtime resolution (more robust). **Resolution:** Choose the lower-risk option (prompt fix) for Wave B. If the prompt fix proves insufficient, revisit with a dedicated task in Sprint 46.

**Disagreement on TSK-0092 null TTL value.** The Skeptical Reviewer argues for exponential backoff rather than a fixed short TTL. **Resolution:** Use a fixed 5s default (consistent with the existing simple TTL pattern). If API hammering becomes a concern, revisit with exponential backoff as a separate task.

## Acceptance Criteria

- [ ] All Wave B tasks have passing tests covering the fix and regression cases
- [ ] `dotnet test MemorySmith.Agent.Tests` passes with zero failures (641+ baseline)
- [ ] `dotnet build` passes with zero warnings
- [ ] TSK-0094 guard checked in GetAsync AND SearchAsync (for consistency)
- [ ] TSK-0092 null cache uses separate config option, not same as valid cache
- [ ] TSK-0089 approach decision recorded before implementation
- [ ] TSK-0096 evidence review documented (even if "no evidence found, keeping tradeoff")
- [ ] TSK-0093 deferred with explicit rationale in task comments
- [ ] Council report saved to wiki

## Open Questions

1. **TSK-0089 prompt location**: Which exact prompt file tells the LLM to "set coords to null" for navigate? Is it `LlmChatInterpreter.cs` inline prompt, or `Data/Pages/Prompts/wiki-chat-agent.md`?
2. **TSK-0092 exponential backoff**: Is a single fixed TTL sufficient, or should null-cache implement exponential backoff (5s, 10s, 20s, max 60s) to handle truly-missing items differently from temporarily-unavailable items?
3. **TSK-0096 log evidence**: Where in the production logs would double-count incidents appear? Are there existing log queries for inventory drift?