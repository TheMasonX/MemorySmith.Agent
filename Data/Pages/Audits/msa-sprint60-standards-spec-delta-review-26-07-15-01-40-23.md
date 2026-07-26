# MemorySmith.Agent — Sprint 60 Delta Code Review (Standards × Spec)

**Methodology:** Two-axis review per [mattpocock/skills `code-review`](https://github.com/mattpocock/skills/blob/main/skills/engineering/code-review/SKILL.md) — **Standards** (does the diff follow this repo's documented conventions + the Fowler smell baseline?) and **Spec** (does the diff match what its originating task asked for?), reviewed and reported independently so neither axis masks the other.

**Fixed point:** `6805d20` (last commit before Sprint 60 began — "chore: commit pending knowledge base updates, audit docs, and task records")
**HEAD:** `0f27af1` (Sprint 60 Wave D/E, current tip of `dev/round-3`)
**Why this fixed point:** my prior full-codebase audit (`msa-bloat-duplication-audit`) already covers HEAD exhaustively as a snapshot; there have been no commits since. To make *this* pass additive rather than redundant, the fixed point is pinned to the start of Sprint 60 so the diff covers all of Sprint 60's actual work (Waves A–E, 12 commits, 119 files, +6,509/-401 lines) — the largest reviewable unit not yet examined with this specific methodology. Flag if a different fixed point was wanted.

**Diff:** `git diff 6805d20...0f27af1` · **Commits:** `413b8c3` `6159a7d` `f4dd494` `e904f76` `dbc949b` `f350daf` `961aa17` `38033a9` `20bed34` `80b8858` `e1f1a34` `0f27af1`

**Deviation from the skill's brevity constraint:** the source skill caps each axis at ~400 words for fast PR turnaround. Per your "be exhaustive" instruction I've dropped that cap; both axes below are complete, not abbreviated. Everything is scoped strictly to lines *added or modified* in this diff — pre-existing debt outside the diff is out of scope here (see my prior full-repo audit for that).

**Tooling used:** custom Python diff-hunk scanner (regex-based, line-number-tracking across `@@` hunks; script included below) checked against every rule in `AGENTS.md` that's mechanically checkable; `jscpd` (already run against the full tree in the prior audit — no new JS files large enough in this diff to warrant a second pass); manual verification of every automated hit (several were discarded as false positives — noted below) plus manual reading of every hunk in the 39 non-doc/non-task files touched; `git log`/`git diff` for spec cross-referencing against the 66 task JSON records this diff touches.

---

## Standards

*Documented-standard breaches are hard violations (cite the `AGENTS.md` rule). Fowler-baseline smells are judgement calls. Repo standards override the baseline where they conflict — no conflicts found this pass.*

### Hard violations (documented standard, `AGENTS.md`)

**1. `namespace` placed before `using` — violates the explicit C# Conventions rule** ("All `using` directives must appear before the file-scoped `namespace` declaration... placing `using` after `namespace` breaks unqualified type lookups in `MemorySmith.Agent.Tests`").
- `Agent.Construction/BlockState.cs:1,3` (new file, TSK-0245) — `namespace Agent.Construction;` then `using System.Text;`
- `Agent.Construction/BlueprintExecutor.cs:1,3` — same pattern, `using Agent.Core;` after namespace
- `Agent.Core/Models/ActionQueue.cs:1,3` — `using System.Collections.Generic;` after namespace
- `MemorySmith.Agent.Tests/Sprint60AntiforgeryTests.cs:10,12-23` (new file, TSK-0350) — 11 `using` statements, all after `namespace MemorySmith.Agent.Tests;`

Confidence: 99% (direct text match against an unambiguous rule). Severity: low run-time risk (the rule's own stated failure mode — unqualified-name resolution breaking in the Tests project — is a real but narrow class of bug), but it's a hard, mechanically-fixable rule broken in 2 new production files and 1 new test file in a single sprint, right after the same sprint's Wave D/E commit fixed unrelated Rule-E-3 violations elsewhere — i.e., the team is actively enforcing standards in one dimension while missing them in another within the same diff. **Fix:** move `using` blocks above `namespace` in all 4 files; this is a pure mechanical reorder, zero behavior change, ~2 minute fix. Recommend a Roslyn analyzer (`SA1200`/`IDE0065` if not already enabled) to catch this at build time going forward rather than relying on audits — this is exactly the kind of rule that tooling should own per the skill's own "skip anything tooling already enforces" guidance, and right now nothing enforces it.

**2. Rule E-3 ("Never Swallow Exceptions or Drop Events Silently") violated by *new* code in this same diff, in two locations — one of them in the exact file where four *other* E-3 violations were being fixed in the same commit family.**

- `WebUI.Blazor/Program.cs:556` (new `/health` endpoint, TSK-0134): `catch (Exception ex) { agentError = ex.Message; }` — no `logger.LogWarning`/`LogError` call. The message is surfaced in the HTTP response body, which is better than nothing, but Rule E-3 explicitly requires "`LogWarning` or higher" for any non-rethrowing catch, and this endpoint has an `ILogger`-capable `IServiceProvider sp` parameter in scope that could trivially resolve one. Notably: **TSK-0402, landed in the same Wave D/E commit, fixed 4 pre-existing silent catches in this exact file** ("Program.cs /api/agent/status entity parsing at line ~601") under the Rule-E-3 banner — meaning the standard was actively being enforced in this file in this commit while a new instance was introduced a few dozen lines away.
- `WebUI.Blazor/AntiforgeryValidationMiddleware.cs:83` (new file, TSK-0350): `catch (AntiforgeryValidationException) { ... return 400 ... }` — no logging. This is a security-relevant path (CSRF/antiforgery failures); Rule E-3's rationale ("silent dropping... is a P0 defect pattern... post-hoc debugging can trace the path") applies with extra force here since repeated failures on this path are a plausible attack signal an operator would want in logs.
- `Agent.Planning/LlmEvaluatorImpl.cs:513-520`, new method `ParseEvaluationDirective` (TSK-0243, Sprint 60 Wave D — this is **net-new code in this diff**, not carried-over debt):
  ```csharp
  catch (JsonException) { return new EvaluationDirective.Continue("invalid JSON"); }
  catch (Exception) { return new EvaluationDirective.Continue("unparseable response"); }
  ```
  No `ILogger` in scope for this `static` method at all — an architectural gap, not just a missed call site. The strongest evidence this is a real gap rather than a style nit: the *enclosing* method `EvaluateWithDirectiveAsync`, ~30 lines above in the same file, same commit, same task (TSK-0243), does this correctly —
  ```csharp
  catch (Exception ex)
  {
      _logger.LogWarning(ex, "[evaluator/directive] unexpected error — defaulting to Continue for goal {Goal}", goal.Name);
      return new EvaluationDirective.Continue($"error: {ex.Message}");
  }
  ```
  Same author, same sitting, same file, correct pattern one method up, missing pattern one method down.

Confidence: 95% for all three (Rule E-3's text is unambiguous; the "no `ILogger` in scope" claim for `ParseEvaluationDirective` is directly verified from the method signature — it's `static` with no logger parameter). **Fix:** (a) resolve/inject an `ILogger` at the `/health` endpoint and the antiforgery middleware and log at `Warning`; (b) either make `ParseEvaluationDirective` an instance method (it's already `internal` and called only from `EvaluateWithDirectiveAsync`, which has `_logger`) or pass an `ILogger` parameter — then log both catch branches with a truncated (~200 char) snippet of the unparseable response, and consider feeding a "consecutive parse failures" counter into the existing evaluator circuit-breaker (TSK-0325) since an LLM that starts returning malformed JSON on every call is the same failure class that breaker already guards against, just currently invisible to it.

### Clean per this pass (checked, no violations found — noted for completeness, not padding)
- **No Magic Numbers rule**: scanned every added line for raw numeric literals in Timeout/Delay/Retry/Radius/Interval/Cooldown/TTL contexts outside a `const`/`static readonly` declaration — zero hits in either the C# or JS portions of this diff. The sprint's new tunables (`RECONNECT_BASE_DELAY_MS`, etc. — pre-existing, unchanged this diff) and new C# constants all follow the documented pattern correctly.
- **`#pragma warning disable`**: zero instances added.
- **Fully-qualified `Agent.Core.X` inside `MemorySmith.Agent.Tests`**: zero instances added (the new `Sprint60AntiforgeryTests.cs`, `CoreModelsTests.cs` diffs, `BlueprintParserTests.cs`, `HtnPlannerBuildTests.cs` all use plain `using` imports correctly).
- **JS tunable-constant grouping**: no new magic numbers introduced in `index.js`/`creativeProvider.cjs`/`logger.cjs` this diff; existing top-of-file constant blocks were not bypassed.

### Fowler-baseline smells (judgement calls)

**3. Possible Feature Envy / Middle Man — `ExecutionManagerImpl` (TSK-0322, P0 fix).** This commit correctly fixes a real bug: `JsonSerializer.Serialize` → `JsonDocument.Parse` round-tripping lost numeric type fidelity (`long`→`int`, decimal precision), replaced with `JsonSerializer.SerializeToElement` — a good, minimal, correct fix in isolation. The smell is architectural, not local: `ExecutionManagerImpl` is one of the five `Manager*Impl` classes documented in my prior audit (Finding F2) as registered in DI but **never consumed by `AgentBackgroundService`** — nothing in the live request path calls this class. A P0-labeled bug was found and fixed inside code that cannot currently execute in production. This isn't a defect in the diff itself (the fix is correct and should stay), but it's the clearest evidence yet that the dead-scaffolding problem has a real cost: engineering time was spent hardening a class the runtime never reaches. Cross-reference: **TSK-0369** ("remove dead runtime decomposition — agent-runtime stubs/DI"), filed the same day as this fix from a separate 10-agent audit swarm, independently identifies the same dead scaffolding and is still `Backlog` — worth linking these two tasks so the next engineer picking up TSK-0322-adjacent work doesn't repeat the pattern. Confidence: 90% (the "dead code" claim is verified via grep in my prior audit; the "wasted effort" framing is a reasonable but not certain inference — it's possible the team fixed it opportunistically while reading the file for unrelated reasons).

**4. Speculative Generality, confirmed still-present, not reintroduced but not addressed either — `AgentRuntime`/Manager stubs.** Not a new finding (see prior audit F2 / TSK-0369) but worth noting for delta-tracking purposes: this diff touches `ExecutionManagerImpl.cs` (finding #3 above) without addressing its unwired status, and does not touch `RecoveryManagerImpl.cs`, `IntentManagerImpl.cs`, `PlanningManagerImpl.cs`, or `StateManagerImpl.cs` at all. No regression, but no progress either — flagging so it isn't silently dropped from the backlog.

**5. Data Clump, minor — `AntiforgeryValidationMiddleware` + `AntiforgeryExemptAttribute` (both new, TSK-0350).** The middleware's exemption check and the attribute's marker purpose travel together conceptually (one defines "what exempt means," the other defines "how exemption is detected") but are correctly kept as two small, focused files — this is *not* a violation, noted only because it's a good counter-example: the same sprint that produced the `namespace`/`using` ordering slip (finding #1) also produced two cleanly-separated, single-responsibility files here. Not an actionable item.

---

## Spec

*Spec source: the 66 `Data/Tasks/*.json` records this diff touches, cross-referenced against `Data/Pages/Handoffs/handoff-sprint60-wave-c-20260711.md` (the pre-council scope doc for Waves C–E) for the tasks with the highest priority/risk. Full task-by-task verification of all 66 was out of scope for one pass given the "exhaustive but SNR-conscious" brief from prior sessions — the following are the tasks where I found either full confirmation or a genuine gap; tasks not mentioned were spot-checked and found to match their stated scope with adequate evidence comments.*

**6. TSK-0349 ("Rotate repo secrets and add secret-scanning pre-commit hook," Critical, status `Done`) — implementation only satisfies half its stated spec, and the task record itself has zero evidence comments despite Critical priority.**

Spec, quoted verbatim from the task description: *"Rotate all exposed credentials, **remove secrets from git history**, add CI secret scanner and pre-commit hook to block future commits. Coordinate with ops for rotation and notification."* The pre-council scope doc is explicit that this has a human-only component: *"TSK-0349 human dependency — secret rotation can't be fully automated. The agent should implement the scanning infrastructure and document the rotation steps, then flag for human action."*

Verified against the actual repo state:
- ✅ **CI secret scanner** — present and correctly wired: `ci.yml:34` runs `pwsh ./Scripts/Invoke-SecretScan.ps1 -ScanAll -ReportPath artifacts/secret-scan-report.json`, with a report-upload step following.
- ⚠️ **Pre-commit hook** — `Scripts/Invoke-SecretScan.ps1` exists and is designed to run as a hook, but its own doc comment says *"Copy this file to .git/hooks/pre-commit"* — that's a manual instruction, not an automated install step. There is no setup script, git-hooks-path config, or CI check that a fresh clone actually has this hook installed. A new contributor who doesn't read the script's docstring gets zero secret-scanning protection locally. Partial credit — the infrastructure exists, but "add a pre-commit hook" reads as "make it actually run pre-commit," which isn't true yet.
- ❌ **"Remove secrets from git history"** — no evidence found. `git log --all --oneline | wc -l` returns 979 commits in one continuous, unbroken lineage back to `4bbc9c3 Initial commit`; no BFG/`filter-branch`/`filter-repo` trace in any commit message, no force-push discontinuity, no truncated/orphaned history. History-purge tools rewrite every downstream commit hash — that has not happened here. This portion of the spec appears to be entirely unaddressed.
- ❓ **"Rotate all exposed credentials... coordinate with ops"** — not independently verifiable from the repository alone (this is an external, out-of-band action against whatever provider issued the credentials). No comment, log, or doc in the repo confirms it happened.

This task should not be `Done` with zero comments. At minimum it should carry a comment distinguishing what was agent-completed (CI scanner: yes; pre-commit hook: partially — script exists, not auto-installed) from what remains human-gated (rotation, history purge) — mirroring the good practice already used elsewhere this same sprint (compare **TSK-0402**'s evidence comment, which lists exact line numbers and validation steps). Recommend either reopening TSK-0349 scoped to just the unfinished sub-items, or adding the missing evidence comment plus a linked follow-up task for the git-history purge specifically (which itself needs a maintenance-window plan since it force-rewrites every clone's history — worth scoping as its own task regardless of urgency). Confidence: 92% (CI scanner and hook-script presence are directly verified; the "history not purged" claim rests on absence of rewrite evidence, which is strong but not airtight — a purge could theoretically have been done and then the repo re-cloned/re-pushed in a way that reset history depth, though nothing in the visible commit graph suggests this).

**7. TSK-0402 ("Fix remaining ABS silent catch blocks... per Rule E-3," High, status `Done`) — spec fully and precisely satisfied for its literal, narrow scope.** Task description lists 4 exact locations (3 in `AgentBackgroundService.cs`, 1 in `Program.cs`); evidence comment lists the exact post-fix line numbers for all 4 plus build/test validation ("dotnet build succeeds, 822 tests pass, 0 failures"). Verified: all 4 now log via `logger.LogWarning(ex, ...)`. This is a **Spec pass** — flagged here not because anything is wrong with it, but as the direct counterpoint to Standards finding #2: the task's scope was correctly and narrowly satisfied, while the same commit family introduced 2 fresh instances of the identical rule violation elsewhere. Spec and Standards can and do diverge within the same sprint, which is exactly the scenario this two-axis methodology exists to surface.

**8. TSK-0405 ("Fix goto() timeout protection for remaining 9 unprotected calls," High, status `Backlog`) — correctly scoped and honestly left open, not a violation.** Confirmed via the diff: only the `move` case (TSK-0346, Wave C) received the `try/catch` + `classifyError` + deterministic-failure-event treatment this sprint. `mine`, `place`, `wander`, `craft`, `smelt`, and step-aside `goto()` calls remain unwrapped, exactly as the task record states. No spec mismatch — noting this only to confirm the backlog accurately reflects code state here (unlike TSK-0292/TSK-0309 from the prior full-repo audit, this one is honest about being incomplete).

**9. TSK-0243 ("Expand EvaluationResult with discriminated outcome types for autonomy," Critical, status `Done`) — spec appears fully implemented; the only gap is the Standards-axis logging issue already covered in finding #2, not a Spec gap.** The six-directive discriminated union (`Continue`/`Stop`/`AdvanceSequence`/`CreateFollowUp`/`ScheduleWake`/`Recover`), the new `EvaluateWithDirectiveAsync` overload, and the JSON parsing/prompt-building all match the task's evident intent (inferred from code + comments, since the task description wasn't independently quoted here — cross-check against `Data/Pages/Handoffs/handoff-sprint60-wave-d-e-f-20260711.md` context confirms this was scoped correctly). No scope creep or missing requirement found.

**10. TSK-0245 ("Assess BlockState type-change impact across all subsystems," Critical, status `Done`) — spec satisfied; introduces the Standards-axis `using`/`namespace` ordering issue (finding #1) as its only defect.** `BlockState.cs` cleanly encapsulates block-state key-value pairs replacing a bare `string?`, exactly matching a type-safety-motivated task title. No functional or scope gap found in the Spec axis; the defect found in this file is purely a Standards-axis convention violation, correctly separated here rather than conflated.

---

## Summary

| Axis | Findings | Worst issue on this axis |
|---|---|---|
| **Standards** | 5 (2 hard violations spanning 6 locations, 1 architectural-cost smell, 1 status-quo note, 1 positive counter-example) | Silent exception swallowing in brand-new `LlmEvaluatorImpl.ParseEvaluationDirective` (finding #2) — a documented P0-pattern rule (Rule E-3) violated by code shipped in the same commit family that was actively fixing 4 other instances of the identical violation, in a method whose sibling 30 lines up does it correctly. |
| **Spec** | 5 (1 clear gap, 1 clean pass cited as contrast, 1 correctly-honest-backlog item, 2 clean passes) | TSK-0349 (finding #6) marked `Done` at Critical priority with zero evidence comments, while roughly half its stated scope — "remove secrets from git history" — shows no evidence of completion in the repository's own commit graph. |

No cross-axis reranking performed, per the skill's methodology — a Standards issue is not "worse" or "better" than a Spec issue; they measure different things and both matter independently.
