# MemorySmith.Agent Sprint 48 Audit Report

**Branch:** `sprint-35-llm-first`
**Commit:** `04ba99bdb5fa35f185877c6a2a79f7e561fab973`
**Sprint:** Sprint 48 — Audit-Driven Corrections (TSK-0105, TSK-0103, TSK-0082)
**Date:** 2026-06-25

---

# Executive Summary

This audit reviewed the implementation of:

* **TSK-0105** — Whole-word bot name matching
* **TSK-0103** — Deterministic response distance gating
* **TSK-0082** — Shared smeltable mapping extraction

along with adjacent planning, chat interpretation, and architecture concerns.

Overall, the sprint moves the codebase in a positive direction by reducing duplication, improving correctness, and increasing test coverage. The extracted smelting knowledge module is directionally correct, and the bot-name matching fix eliminates a class of false-positive detections.

However, the audit identified one likely runtime correctness bug and several architectural inconsistencies that should be addressed before these patterns spread elsewhere in the codebase.

## High Priority Findings

| ID         | Finding                                                                                  | Severity | Confidence |
| ---------- | ---------------------------------------------------------------------------------------- | -------- | ---------- |
| AUD-48-001 | SmeltableMapping can produce invalid mine targets (`raw_iron`, `raw_gold`, `raw_copper`) | High     | 97%        |
| AUD-48-002 | Bot-name regex cache exists but is not actually used                                     | Medium   | 96%        |
| AUD-48-003 | Deterministic and LLM chat paths use different distance calculations                     | Medium   | 78%        |

---

# Scope Reviewed

## Files Inspected

### Chat Interpretation

* `Agent.Planning/ChatInterpreter.cs`
* `Agent.Planning/LlmChatInterpreter.cs`

### Smelting / Planning

* `Agent.Planning/SmeltableMapping.cs`
* `Agent.Planning/Goals/SmeltGoal.cs`
* `Agent.Planning/HtnTaskLibrary.cs`

### Tests

* `MemorySmith.Agent.Tests/Sprint48Tests.cs`

### Documentation

* `Data/Pages/roadmap.md`

### Architecture Guidance

* Matt Pocock's "Improve Codebase Architecture" skill document

---

# Detailed Findings

## AUD-48-001

## Invalid Mine Targets Produced by SmeltableMapping

### Severity

High

### Confidence

97%

### Evidence

The new shared mapping introduces three concepts into a single module:

* Smeltable inputs
* Smeltable outputs
* Mineable source blocks

These concepts are not equivalent.

The mapping currently includes:

* `raw_iron`
* `raw_gold`
* `raw_copper`

inside the set of mineable entities.

The planner subsequently uses:

```csharp
SmeltableMapping.GetInputBlock(...)
```

and

```csharp
SmeltableMapping.IsSmeltableMineableBlock(...)
```

during decomposition.

This can result in plans equivalent to:

```text
MineBlock(raw_iron)
MineBlock(raw_gold)
MineBlock(raw_copper)
```

which are not valid world actions.

### Impact

The planner may generate impossible plans despite all unit tests passing.

This is particularly dangerous because:

* The mapping appears internally consistent.
* The tests validate mapping consistency.
* The generated plan can still fail at runtime.

### Recommendation

Separate:

```text
Smelt Input Item
```

from

```text
Mineable Source Block
```

into distinct concepts.

Example:

```csharp
GetSmeltInput()
GetSourceBlock()
```

rather than treating them as interchangeable.

### Suggested Priority

Immediate.

This is the only finding likely to directly generate invalid gameplay actions.

---

## AUD-48-002

## Compiled Regex Cache Is Not Used

### Severity

Medium

### Confidence

96%

### Evidence

TSK-0105 introduces:

```csharp
_botNameRegex
```

and helper construction logic.

However the actual matching path uses:

```csharp
Regex.IsMatch(...)
```

inside a static helper.

The cached regex instance is never consulted.

### Impact

Current behavior is correct.

Performance and maintainability expectations are not.

The code now contains:

* dead state
* duplicated regex ownership
* misleading optimization signals

### Recommendation

Choose one:

#### Option A

Use cached compiled regex everywhere.

#### Option B

Remove the cache entirely.

The current middle ground provides neither simplicity nor optimization.

### Suggested Priority

Next sprint cleanup.

---

## AUD-48-003

## Distance Calculation Drift Between Chat Paths

### Severity

Medium

### Confidence

78%

### Evidence

Deterministic path:

```text
Distance = X/Z only
```

LLM path:

```text
Distance = X/Y/Z
```

Both influence whether a message is considered directed at the bot.

### Impact

The same player message may be:

* accepted by deterministic interpretation
* ignored by LLM interpretation

depending on vertical separation.

Examples include:

* caves
* towers
* cliffs
* underground mining

### Recommendation

Centralize distance calculation into a shared helper.

Example:

```csharp
IChatAddressingPolicy
```

or

```csharp
ChatDistanceCalculator
```

used by both paths.

### Suggested Priority

Before additional chat reliability work lands.

---

# TSK-Specific Review

## TSK-0105 — Whole-Word Bot Name Matching

### Assessment

Successful.

### Confidence

95%

### Positive Outcomes

Eliminates false positives such as:

```text
agent
```

matching

```text
agentic
```

or

```text
leo
```

matching unrelated words.

### Test Coverage

Good.

Current tests cover:

* exact match
* case-insensitive match
* embedded sentence match
* substring rejection
* end-of-word rejection

### Missing Tests

Potential future additions:

* names containing spaces
* names containing punctuation
* names containing dashes

Examples:

```text
Agent Bot
R2-D2
Mr.Bot
```

---

## TSK-0103 — Response Distance Gating

### Assessment

Successful.

### Confidence

92%

### Positive Outcomes

Correctly prevents distant players in multiplayer environments from accidentally triggering the bot.

The solo-player bypass is a practical usability improvement.

### Remaining Concern

Distance logic duplication remains.

The policy exists in multiple locations rather than one authoritative module.

---

## TSK-0082 — Shared Smeltable Mapping

### Assessment

Mostly successful.

### Confidence

88%

### Positive Outcomes

Removes duplicated smelting knowledge.

Improves locality.

Improves maintainability.

Improves consistency between:

* planner
* goals
* future smelting features

### Remaining Concern

Mineable block semantics and smelt-input semantics are currently conflated.

---

# Architectural Review

Using Matt Pocock's architecture framework:

## Deep Module Candidate #1

### Smelting Knowledge Module

Current module:

```text
SmeltableMapping
```

contains several related but distinct concepts.

A deeper module would expose:

```text
SmeltingKnowledge
```

with higher-level operations:

```csharp
CanSmelt(item)
GetOutput(item)
GetSourceBlock(item)
RequiresMining(item)
```

Benefits:

* Better locality
* Reduced planner knowledge leakage
* Fewer duplicated rules

Recommendation Strength:

**Strong**

---

## Deep Module Candidate #2

### Chat Addressing Module

Current logic is spread across:

* deterministic interpreter
* LLM interpreter

Both independently decide:

```text
Is this message for me?
```

A dedicated module could own:

* name matching
* distance gating
* multiplayer behavior
* direct mention requirements

Benefits:

* One source of truth
* Better testability
* Reduced behavioral drift

Recommendation Strength:

**Strong**

---

## Deep Module Candidate #3

### Planner Knowledge Catalogs

The codebase now contains several growing knowledge registries:

* blueprint aliases
* smelt mappings
* crafting relationships
* mining aliases

These are gradually becoming a domain concept.

Future direction:

```text
GameKnowledge
```

or

```text
MinecraftKnowledge
```

module.

Recommendation Strength:

Worth Exploring

---

# Suggested Sprint 49 Follow-Up

## P1

Fix invalid mineable smelting entries.

## P1

Unify chat distance calculations.

## P2

Remove or properly wire `_botNameRegex`.

## P2

Expand bot-name tests to punctuation and multi-word names.

## P2

Begin consolidating game knowledge registries into a deeper planning module.

---

# Final Assessment

Sprint 48 successfully improves correctness, reduces duplication, and adds valuable test coverage.

The extracted smelting mapping is the largest architectural improvement in the sprint, but it also introduces the most significant correctness risk because mineable blocks and smeltable items are currently treated as interchangeable.

The highest-value immediate fix is:

**AUD-48-001 — separate mineable source blocks from smelt input items.**

This is the only identified issue likely to generate invalid plans during normal gameplay.

## Overall Sprint Quality

**Implementation Quality:** 8.5 / 10

**Test Coverage Quality:** 8 / 10

**Architectural Improvement:** 8 / 10

**Risk Level:** Low–Moderate

**Confidence in Audit:** 89%
