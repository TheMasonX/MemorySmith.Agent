# MemorySmith.Agent audit delta — Sprint 50 Wave D follow-up

**Date:** 2026-06-26  
**Scope:** Delta only — corrections to prior claims and new recommendations from the latest inspection of commit `d2ef16ab86d433cc62912939c213cde088dcaf05` on `sprint-35-llm-first`.

## Executive summary

The biggest correction is that the new “context wiring” is not yet an end-to-end feature. The dispatcher now copies non-internal `ActionData.Context` into `Arguments`, but the downstream tools still do not consume that path in the way the sprint task describes. `SearchMemoryTool` still returns only search metadata, not coordinates; `MoveToTool` still requires explicit `x/y/z`; and the task file for TSK-0004 says the intended flow is for SearchMemory to surface coordinates that MoveTo reads from context. That makes the current implementation a partial scaffold, not a completed carry chain. fileciteturn12file0turn18file0turn24file0turn22file0

A second correction: the chat policy is slightly out of sync with the implementation. The docs in `LlmChatInterpreter` say only stop/cancel, status, inventory, and help are deterministic fast-paths, but the code still fast-paths `navigate`. That is not necessarily wrong, but it is a policy drift that should be made explicit or removed so the “LLM owns intent” architecture stays honest. fileciteturn47file0

The SQLite telemetry addition is straightforward and useful, but the broad `NU1903` suppression should be treated as a temporary risk acceptance with a follow-up task, not as a permanent “all clear.” fileciteturn55file0turn9file1

## Corrections to earlier claims

### 1) “TSK-0004 is wired” should be downgraded to “dispatcher-only carry merge”
**Confidence: 95%**

The current code copies `action.Context` entries into `action.Arguments` before dispatch, excluding only underscored keys and `correlationId`. That is a useful transport mechanism, but it is not yet a full carry model because the producer and consumer sides are not aligned. `SearchMemoryTool` currently returns `query`, `results`, `bestPageId`, and `count` only. There is no coordinate extraction, and there is no `nearestX/Y/Z` population in the reviewed tool output. Meanwhile `MoveToTool` still throws unless `x`, `y`, and `z` are present in `arguments`. The task file for TSK-0004 explicitly says SearchMemory should extract coordinates and MoveTo should read from context when arguments are absent, which is not what the code currently does. fileciteturn12file0turn24file0turn22file0turn18file0

Implementation consequence: if upstream tools start writing carry values before the schema contract is adapted, `ToolDispatcher` can still reject them as unexpected properties. The dispatcher validates all supplied argument properties against the tool schema before execution, so context-to-arguments merging can become a silent footgun unless the tool schema is widened or context is resolved after validation. fileciteturn40file0turn41file0

### 2) “Only four deterministic chat fast-paths” should be updated to “four plus navigate”
**Confidence: 90%**

`LlmChatInterpreter` explicitly documents four deterministic fast-paths, but the code still returns the pattern result immediately for `navigate`. If that is intentional, the docs should say so and explain why navigation is considered safe enough to skip the LLM. If it is not intentional, remove it now so the architecture stays coherent and future changes do not have to work around an exception that lives in one place only. fileciteturn47file0turn49file0

### 3) “SQLite telemetry is complete” should be framed as “added, but with a dependency-risk follow-up”
**Confidence: 80%**

The sink is present and wired, but the project file suppresses `NU1903` globally for the web host. That makes sense only if there is a tracked follow-up to revisit the dependency graph or replace the sink when a cleaner package path is available. Otherwise the warning suppression will outlive the underlying reason for it. fileciteturn37file0turn55file0

## New recommendations

### 1) Make context carry explicit instead of implicit
**Confidence: 92%**

The current “copy context into arguments” approach is simple, but it couples hidden data flow to schema validation. That is fragile. A better pattern is to resolve context inside the target tool or to add a dedicated carry contract that is separate from user-supplied tool arguments.

Practical path:
1. Keep `ActionData.Context` as the internal carry bag.
2. Add an explicit allowlist of carry keys per tool, or add a typed `ContextKeys`/`ContextSchema` field on each tool.
3. In `ToolDispatcher`, validate only the declared input schema, then merge allowed carry keys right before execution or let the tool read them directly.
4. Add a regression test for a carry key that is valid for `MoveTo` but invalid for another tool so the contract stays scoped.

This avoids the current situation where one tool’s internal context can accidentally become another tool’s malformed arguments. `ActionData` already documents that context is shared across a plan, but the transport layer should not assume every context key is a first-class argument. fileciteturn33file0turn40file0turn41file0

### 2) Finish the SearchMemory → MoveTo handoff before broadening the pattern
**Confidence: 94%**

The task file for TSK-0004 is clear enough to implement directly: SearchMemory should extract coordinates from the best result when present, then MoveTo should fall back to context coordinates if explicit arguments are missing. That gives you a small, testable end-to-end flow before you generalize context carry to other tool chains. fileciteturn18file0turn24file0turn22file0

Suggested implementation order:
1. Extend `SearchMemoryTool` to inspect the best hit and emit structured fields such as `nearestX`, `nearestY`, `nearestZ`, plus a confidence or source marker.
2. Make `MoveToTool` accept nullable `x/y/z` or resolve coordinates from context when absent.
3. Add tests for both explicit and context-driven movement.
4. Only then let `AgentBackgroundService` or `ToolDispatcher` merge carry keys into arguments for tools that opt in.

That sequence keeps the first implementation narrow and makes failures obvious.

### 3) Add a clarification-question bridge or remove the field from the prompt
**Confidence: 70%**

The LLM prompt and sprint docs talk about low-confidence clarification questions, but I did not find a concrete runtime bridge in the reviewed files that guarantees those questions are emitted to the user and that no goal is created. That should be nailed down before you keep relying on the field in the schema.

Suggested minimum:
1. Decide where the clarification gets surfaced.
2. Make that place the only writer of the follow-up chat message.
3. Add a test that a low-confidence `clarify` draft does not enter goal creation.

If that path already exists elsewhere, it deserves a dedicated test and a short comment near the entry point so it is not mistaken for dead schema.

### 4) Either document or remove `navigate` from the deterministic fast-path
**Confidence: 88%**

This is mostly a code health item, but it matters because it creates an implicit contract drift. You can resolve it in one of two ways:
- Keep `navigate` as a deterministic shortcut and document it as an intentional zero-risk command.
- Or remove it from the fast-path and let the LLM classify it like the rest of the non-trivial intents.

For a greenfield system, the cleaner choice is usually to make special cases rare and explicit.

## Open questions

The main open question is whether context carry should remain a generic dispatcher concern or become a per-tool contract. The current code hints at both models at once, which is why it feels almost finished but still brittle. The safest near-term answer is to scope carry to the one flow that needs it first, then expand only after tests prove the contract. fileciteturn33file0turn40file0turn18file0

Another open question is whether `navigate` is meant to stay deterministic. If yes, the docs should say so; if no, the code should stop special-casing it.

## Recommended next implementation slice

If I were handing this to the next agent, I would do the following in order:

1. Add a `SearchMemory` test that returns a best result with coordinates and verifies those coordinates appear in tool output.
2. Update `SearchMemoryTool` to emit structured coordinate hints.
3. Update `MoveToTool` to accept context fallback or nullable coordinates.
4. Move context merging behind an opt-in tool contract.
5. Add a low-confidence clarification test and wire the bridge if it is missing.
6. Decide the final policy for `navigate` and make docs and code match.

That keeps the next sprint focused on one end-to-end user-visible improvement instead of spreading risk across several loosely coupled subsystems.