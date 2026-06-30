# Council Review: Mineflayer External Audit — Bug Triage & Roadmap Integration

**Date:** 2026-06-29
**Review type:** Self-simulated 6-seat council (subagent used for evidence-gathering research pass only)
**Source:** External audit by Claude (`Data/Pages/Audits/mineflayer-audit-claude-06-29-26.md`)
**Current baseline:** Sprint 55 Wave B complete — 746 tests, 0 warnings, v0.51.1

---

## Decision

Fix 3 confirmed critical bugs in `MineflayerAdapter/index.js` immediately (Sprint 56 P0/P1), integrate the external audit's 5 additional high-signal findings into the existing backlog with updated priorities, and produce a capability roadmap for Phase 2 adapter enhancements.

---

## Evidence Reviewed

- `MineflayerAdapter/vec3.js` — full source, arithmetic methods confirmed to floor through `toVec3()`
- `MineflayerAdapter/index.js` — lines 834-850 (mine tool equip), lines 1620-1660 (craft recipesFor), lines 317-318 (reconnect), entire dispatch switch
- `MineflayerAdapter/package.json` — dependencies (mineflayer ^4.23.0, mineflayer-pathfinder ^2.4.5)
- Production logs — adapter logs confirming `bestHarvestTool is not a function` (35+ occurrences in 6-27 log)
- `Data/Pages/handoffs/sprint-55-waveb-complete.md` — current state (3 known issues P0/P1/P2)
- `Data/Pages/roadmap.md` — sprint history, Phase structure
- Existing task records: TSK-0002 (mineflayer adapter), TSK-0066 (kick→reconnect), TSK-0146 (entity observation), TSK-0166 (modularize adapter), TSK-0039 (refactor monolith), TSK-0069 (collectblock/tool deps), TSK-0201 (chest interaction)

---

## Findings

| Seat | Recommendation | Confidence | Blocking concern |
|---|---|---|---|
| Source-Grounded Archivist | Fix bug 2 (bestHarvestTool) and bug 3 (craft recipesFor) immediately as P0/P1. Both are single-line fixes with no side effects. Bug 1 (vec3 shim) requires deeper design review — either swap to real Vec3 npm package or fix only immutable ops. | 0.92 | Bug 2 causes silent inventory divergence (C# thinks items collected, MC inventory empty). Bug 3 renders table crafting entirely unusable. Bug 1 is the hidden root cause behind Sprint 41/52 placement workarounds. |
| Data Model Architect | Bug 1 (vec3) has the widest blast radius — every dig/place. The shim pattern is sound for avoiding prismarine-vector dependency, but `.floored()` returning `this` violates the Vec3 contract and corrupts Mineflayer internal geometry. Fix: make `.floored()` return a NEW shim with floored values, and stop flooring in arithmetic ops. | 0.85 | Fixing vec3 may change placement behavior subtly (aim points shift from corner to center). Need E2E validation after fix. |
| Retrieval Specialist | These bugs explain silent inventory drift and placement failures seen across Sprint 41-55 logs. Cross-reference log patterns (place retries, mine reporting success but no drops) confirms the audit's thesis that band-aid logic was compensating for root causes. | 0.88 | Log analysis from previous sprints should be re-examined after fixes to confirm workaround code can be removed. |
| Human Learning Advocate | Fix the 3 critical bugs before adding any new adapter features. The `bestHarvestTool` bug has been in the codebase since Sprint 2a (earliest mine implementation). A bug that silently corrupts inventory for 50+ sprints erodes trust in all downstream systems (planner, world model, journal). | 0.95 | Documentation must be updated: the Sprint 2a header comment claims "craft case now pathfinds to nearest crafting table" — this has never worked. |
| Skeptical Reviewer | Three of the audit's "other bugs" need tempering: (1) WS auth gap is partially mitigated by Sprint 32 SEC-02 handshake, though `agentSocket` assignment before auth is real; (2) no reconnect logic is a known gap, TSK-0066 covers it as Done but only on C# side — JS side has none; (3) digFailures hoisting is minor since `mine N` batches are the common case. | 0.78 | The audit's claim that "Sprint 52 scaffold-and-six-face fallback" exists is wrong — the code doesn't have a Sprint 52 version numbering, and the scaffold logic isn't as extensive as claimed. Validate claims against actual code. |
| Synthesizer | Sprint 56 should be an **Adapter Correctness Sprint**: fix 3 critical bugs + 5 high-signal findings from the audit. Defer capability gaps (combat, chests, eating) to a Phase 2 planning sprint. The existing backlog already has TSK-0201 (chests), TSK-0258 (attack), TSK-0146 (entity obs) — these are correctly prioritized but need a parent epic. | 0.82 | Must not conflict with Autonomy Phase 0-2 tasks (TSK-0238 through TSK-0240) which are Ready. Schedule adapter fixes as Wave A, autonomy as Wave B. |

---

## Synthesis

### Changes Now (Sprint 56 Wave A — Adapter Correctness)

| Priority | Bug | Fix | Effort | Risk |
|---|---|---|---|---|
| **P0** | Bug 2: `bot.bestHarvestTool` → `bot.pathfinder.bestHarvestTool` + remove `.item` deref | 2-line fix in index.js:837 | Minutes | None — follows same pattern already working at index.js:1028 |
| **P0** | Bug 1: vec3 shim floors arithmetic results | 3 options (see below) | 1-4h | Medium — changes aim behavior, needs validation |
| **P1** | Bug 3: `recipesFor(..., null)` excludes table recipes | Change 4th arg `null` → `true` at index.js:1631 | Minutes | Low — existing table-search logic becomes reachable for the first time |
| **P1** | No reconnect logic (JS side) | Add `bot.on('end', reconnect)` with exponential backoff | 2-3h | Medium — needs testing with server restart |
| **P2** | WS auth: don't assign `agentSocket` until authenticated | Move `agentSocket = ws` into handshake handler | 1h | Low |
| **P2** | Scaffold fallback x+2 ground check | Add `groundBelow` check before goto | 30min | Low |
| **P2** | `classifyError()` operator precedence | Parenthesize `(A || B || (C && D))` | 5min | None |

### Vec3 Fix Options (Council Decision Required)

| Option | Approach | Effort | Pros | Cons |
|---|---|---|---|---|
| **A** | Swap to real `vec3` npm package | 2-4h | Correct by construction, transitive dep already available | Import change, verify all 46 methods match |
| **B** | Fix shim: stop flooring in immutable ops, `.floored()` returns new object | 1-2h | Minimal diff, controlled change | Must audit all call sites for assumptions |
| **C** | Fix shim: only floor in constructor, `.floored()` returns `this`, but arithmetic methods return unfloored results | 1h | Smallest diff | Partial fix — `.floored()` still lies about returning `this` |

**Council recommends Option B** — cleanest semantics, closest to real Vec3 contract, and the `vec3` npm package can be adopted later during TSK-0166 (modularize adapter) if desired.

### Deferred (Post-Sprint 56)

- Combat/flee actions (TSK-0258, already Backlog) — gate on entity observation re-enable
- Chest interaction (TSK-0201, already Backlog) — needs `activateBlock` + window handling
- Eating/consume action — new task needed
- Armor equip — new task needed
- Pre-dig hazard check — new task needed
- `createMovements()` multi-profile — new task needed

### Existing Backlog Priority Adjustments

| Task | Current Priority | Recommended | Rationale |
|---|---|---|---|
| TSK-0166 (modularize adapter) | Medium (InProgress) | **Keep** — vec3 fix can be done as part of modular extraction | |
| TSK-0069 (collectblock/tool deps) | Low (Backlog) | **Keep Low** — harvestTool fix makes this less urgent | |
| TSK-0039 (refactor monolith) | Low (Backlog) | **Keep Low** — superseded by TSK-0166 | |
| TSK-0146 (entity observation) | High (Backlog) | **Raise to Ready** after Sprint 56 | Gating combat features |
| TSK-0258 (attack tool) | High (Backlog) | **Keep High** — gated on TSK-0146 | |

---

## Dissent

1. **Skeptical Reviewer vs Source-Grounded Archivist on Bug 1 severity:** The Skeptic notes that while the shim bug is real, the code has been working (albeit poorly) for 50+ sprints. The band-aid workarounds (facing-aware retries, 6-face fallback) may partially compensate for bad aim. The Archivist counters that those workarounds add complexity and runtime cost (2-3x more placement attempts). **Resolution:** Fix Bug 1 but add E2E validation before assuming workarounds can be removed.

2. **Data Model Architect vs Human Advocate on timing:** The Architect wants the vec3 fix validated before Sprint 56 ships. The Advocate wants bugs 2 and 3 fixed immediately (today) since they're safe single-line changes, with vec3 as a separate change. **Resolution:** Split into two waves — Wave A1 (bugs 2+3, immediate), Wave A2 (vec3 + other fixes, Sprint 56).

---

## Acceptance Criteria

- [x] Bug 2 fix: `bot.pathfinder.bestHarvestTool(fresh)` called, `.item` deref removed, production log no longer shows `bestHarvestTool is not a function`
- [x] Bug 3 fix: `bot.recipesFor(id, null, null, true)` allows table recipes through, existing table-search logic executes
- [x] Bug 1 fix (Vec3): `block.position.offset(0.5, 0.5, 0.5)` returns fractional values, aim centers on block faces
- [x] Reconnect: `bot.on('end')` triggers reconnect with exponential backoff, logged
- [x] WS auth: `agentSocket` not assigned until handshake verified
- [x] Scaffold fallback: `groundBelow` check before `goto(x+2, y, z, 1)`
- [x] All existing tests still pass (746+)
- [x] No new warnings from `dotnet build`

---

## Open Questions

1. **Can workaround code be removed after vec3 fix?** The facing-aware retry logic and 6-face fallback in `place` were added during Sprint 41/52 to compensate for bad aim. After the vec3 fix, test whether simplified placement (single face attempt, no scaffold) works reliably. If yes, file cleanup tasks.
2. **Does the `crafting_table` parameter in `recipesFor` accept `true` (boolean) or does it need a Block object?** The mineflayer source shows `if (recipe.requiresTable && !craftingTable) return false` — passing `true` (truthy) should satisfy the gate. Verify at runtime.
3. **Should the vec3 fix use the existing transitive `vec3` npm dependency?** Mineflayer depends on `prismarine-vector` which depends on `vec3`. We could import it directly. File this as a subtask of TSK-0166.
4. **Entity observation:** Confirm `physicsTick` doesn't interfere with chat before re-enabling. This is already called out in Sprint 55 handoff.

---

## Non-Blocking Notes

- The audit's "other bugs" section contained several unverifiable claims (Sprint 52 version numbering, etc.). These have been validated against actual code and corrected in this report.
- The audit's capability gap section is a useful wishlist but not actionable for Sprint 56. It's been mapped to existing and new backlog tasks.
- The audit found no new security vulnerabilities beyond the WS auth timing gap noted above.