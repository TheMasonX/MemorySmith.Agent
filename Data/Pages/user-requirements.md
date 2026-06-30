# User Requirements

## Architecture hard requirements

These requirements are now treated as binding design guidance for future work:

- ExecutionContext is the canonical runtime state object. It should carry the state that flows through planning, dispatch, evaluation, and replanning instead of relying on loose arguments and repeated state derivation.
- Removal is preferred over deprecation and fallback. The supported architecture should be the modern typed pipeline only; legacy compatibility shims should be removed rather than preserved.
- Planning and replanning should rely on explicit preconditions, postconditions, and structured remediation policies rather than free-text fallbacks.
- Fresh world-state and inventory truth should be treated as prerequisites before plan generation.
- AgentBackgroundService should evolve into an orchestration layer rather than remain the primary owner of runtime policy and side effects.

## Source context

This guidance was synthesized from the 2026-06-30 external audit set and the human review notes for the current architecture direction.
