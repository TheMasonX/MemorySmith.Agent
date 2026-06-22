# MemorySmith.Agent handoff audit — 20260622T164312Z

## Scope

I reviewed the uploaded next-auditor handoff against the accessible GitHub repository pages for `TheMasonX/MemorySmith.Agent`, focusing on the files and contracts the handoff explicitly named: `Program.cs`, `AgentBackgroundService.cs`, `LlmChatInterpreter.cs`, `IToolCaller.cs`, `ToolDispatcher.cs`, `Sprint37Tests.cs`, and the claimed sprint-37 runtime contracts. The handoff asks the next reviewer to verify that `ActionOutcome`, `IntentManager`, `IntentAssessment`, and outcome-based tool dispatch exist and are wired through the runtime path. The visible repo pages I could inspect did not confirm those contracts yet. citeturn2file11turn250851view0turn908393view0turn908393view1turn908393view2turn908393view3turn908393view4

## Bottom line

The plan in the handoff is directionally right, but the visible source still looks materially pre-finish for Sprint 37. The biggest mismatch is that the handoff describes an outcome-based observation pipeline and an extracted intent layer, while the code I could inspect still shows the older shapes: `IToolCaller` only exposes `CallAsync`, `ToolDispatcher` still journals inside `CallAsync`, `LlmChatInterpreter` still owns deterministic fallback behavior, and `AgentBackgroundService` still owns both chat interpretation and goal creation. citeturn447827view0turn901801view5turn147461view7turn901801view3turn953009view2turn953009view3

## High-confidence findings

### 1) The intent layer is not actually isolated yet

The handoff says Sprint 37 should make `IntentManager` own intent-to-goal translation and wire it into DI. In the visible repository pages, I could not find `IntentManager` or `ActionOutcome` in the repo tree, and `Program.cs` still wires `LlmChatInterpreter` and `AgentBackgroundService` directly. That strongly suggests the intent extraction described in the handoff is either not present in the accessible snapshot or not surfaced in the public tree I inspected. citeturn908393view0turn908393view1turn250851view0turn205323view0

Why this matters: if the background service still interprets chat and creates goals directly, the system has two places that can decide semantics. That is exactly the duplication the handoff warns about, and it makes future intent drift more likely. citeturn953009view2turn953009view3

### 2) `LlmChatInterpreter` still has legacy fallback logic

The interpreter’s own documentation says it combines LLM evaluation with deterministic pattern-matching fallback and a distance gate. The implementation also returns early on several deterministic fast paths, including create/cancel/help/status/navigation cases. That means the LLM is not yet the sole high-level intent authority. citeturn147461view3turn901801view3

Why this matters: the handoff’s architectural goal is centralized ownership of intent, but the current interpreter still acts as a policy engine. That is a reasonable transitional design, but it should be called out explicitly in the sprint plan so nobody mistakes it for a fully separated intent layer. citeturn147461view3turn901801view3

### 3) The tool boundary is still `CallAsync`, not outcome-based dispatch

`IToolCaller` currently declares only `CallAsync`. `ToolDispatcher.CallAsync` still resolves the tool, validates its schema subset, executes the tool, and writes a journal entry for success or failure. I found no visible `CallWithOutcomeAsync` on the interface or dispatcher pages I inspected. citeturn447827view0turn901801view5turn147461view4turn147461view5

Why this matters: the handoff’s sprint-37 items explicitly depend on an outcome-aware dispatch path, but the visible source still routes through the older `CallAsync` contract. Until that changes, the observation pipeline cannot be treated as goal-scoped or outcome-scoped in a meaningful way. citeturn2file11turn447827view0turn901801view5

### 4) The background service still owns too many responsibilities

`AgentBackgroundService` still receives `IChatInterpreter`, `GoalFactory`, `IPlanner`, `IToolCaller`, and the world adapter in one class. In `HandleChatEventAsync`, it calls `chatInterpreter.InterpretAsync`, logs the resolved intent, and then directly branches into `CancelGoal()` or `TryCreateGoalFromChatAsync()`. `TryCreateGoalFromChatAsync()` still calls `goalFactory.CreateAsync()` and then `SetGoal(goal)`. That is a clear sign that orchestration, semantic interpretation, and goal instantiation are still coupled in one runtime seam. citeturn953009view2turn953009view3turn953009view0

Why this matters: the handoff is trying to push the system toward LLM-owned intent interpretation and centralized intent/world-state ownership. The code path I inspected still concentrates decision-making in the host loop, which makes the module harder to deepen and harder to test in isolation. citeturn2file11turn953009view2turn953009view3

### 5) The new test change is only a compile fix, not proof of the new runtime contract

Commit `245f78f` is titled as a Sprint 37 test fix and the commit page says it changed one test file with a one-line addition and one-line deletion to fix default-interface-method access in the stub. That is useful, but it does not prove the runtime contracts the handoff asks about: `ActionOutcome`, outcome dispatch, intent extraction, or real goal correlation. citeturn235907view0

Why this matters: the handoff asks whether the new tests prove the dangerous edges, not just the happy path. The visible commit history I could inspect suggests the latest change is a compile correction, so the runtime behavior still needs explicit negative-path coverage. citeturn235907view0turn2file11

## Specific gaps to carry into Sprint 38

The handoff is already pointing at the right unresolved problems: goal correlation may still be placeholder-based, intent extraction may still have fallback drift, `IntentAssessment` may still be scaffolding, and remaining `CallAsync` callers may now be silent if the new outcome path is not fully adopted. That matches what I could verify in the visible source: the old tool contract and the old semantic-routing shape are still there. citeturn2file11turn2file5turn447827view0turn953009view2turn953009view3

The practical risk is not one isolated bug. It is that the repo can end up with two overlapping truth sources: the new sprint narrative and the older runtime path. When that happens, regressions tend to hide in the seams — especially around goal completion, observation timing, and tool journaling. citeturn2file11turn147461view7turn953009view2

## Improvements I would prioritize

1. Finish the intent split before adding more behavior: make `IntentManager` or its equivalent the only place that turns parsed intent into goal intent, and remove the fallback mapping once the new path is proven. citeturn2file11turn2file5
2. Make outcome correlation real: replace any placeholder or implicit goal association with a real goal identifier so observations can be aggregated per goal. citeturn2file5
3. Collapse the tool boundary to one outcome-aware dispatch surface and audit every remaining `CallAsync` caller intentionally. citeturn2file5turn447827view0turn901801view5
4. Move more of the background-service policy into smaller testable modules so the host loop only orchestrates. citeturn953009view2turn953009view3
5. Add negative-path tests for schema failure, duplicate tool calls, ambiguous chat, and outcome-correlation errors instead of only compile-path or happy-path assertions. citeturn2file11turn235907view0

## What to verify next

Verify the current branch or PR snapshot directly for the four sprint-37 contracts the handoff names: `ActionOutcome`, `CallWithOutcomeAsync`, `IntentManager`, and `IntentAssessment`. If those are present in the sprint branch but absent from the public `main` tree, the repo needs an explicit note so the handoff and the visible baseline do not keep diverging. citeturn2file11turn250851view0turn908393view0turn908393view1
