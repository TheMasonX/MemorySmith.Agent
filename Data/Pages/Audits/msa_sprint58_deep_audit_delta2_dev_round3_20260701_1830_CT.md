# MemorySmith.Agent — Deep Audit Delta #2 (Sprint 58 Wave D / dev/round-3)

Report: msa_sprint58_deep_audit_delta2_dev_round3_20260701_1830_CT.md

Generated: 2026-07-01 18:30 CT
Same commit as prior report: `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c` (`dev/round-3`)

**This is a delta-only report.** It does not repeat F1–F9 or the tracked-items table from `msa_sprint58_deep_audit_dev_round3_20260701_1500_CT.md`. Everything below is new: newly-read files (`HtnTaskLibrary.cs` full pass, `AliasRegistry.cs`, `ChatInterpreter.cs`, `ReplanGovernor.cs`, `GoalFactory.cs` construction paths, `WorldState.cs`, and a codebase-wide scan for the same class of bug as F1) plus one methodology upgrade: two findings below (F10, F12) were verified against the actual `dotnet/runtime` source for `Microsoft.Extensions.DependencyInjection`'s constructor-selection algorithm (fetched live), not just reasoned about, which is why their confidence is unusually high for something this environment can't execute directly.

---

## Findings Table (New This Pass)

| # | Finding | Class | Impact | Likelihood | Severity | Confidence |
|---|---|---|---|---|---|---|
| F10 | `HtnTaskLibrary`'s DI-selected constructor leaves `_methods` empty in production | Verified Issue | 4 | 3 | **12 (High)** | 92% |
| F11 | `ReplanGovernor` graduated stall-backoff never escalates past the 2nd tier (flat ~10s instead of 5→10→20→30s) | Verified Issue | 3 | 5 | **15 (High)** | 90% |
| F12 | Alias/fuzzy-resolution layer (`ChatInterpreter.Resolve*`, `AliasRegistry.TryResolve/Search`) is entirely dead code, contradicting its own doc comment | Verified Issue / Doc Drift | 2 | 5 | **10 (Moderate)** | 93% |
| F13 | `ResolveCraftItem` would mis-resolve "iron"/"gold"/"copper" to raw ore blocks if ever wired up (latent correctness bug inside dead code) | Probable Issue | 2 | 2 (contingent on F12 fix) | 4 (Low) | 85% |
| F14 | `GatherWoodGoal` + `SurviveNightGoal` + 9 of 11 `HtnTaskLibrary` dictionary tasks are unreachable via any live chat/intent path | Consolidation Opportunity | — | — | — | 88% |

---

## F10. `HtnTaskLibrary`'s DI-selected constructor leaves `_methods` empty in production
**Classification:** Verified Issue · **Severity 12/25 (High)** · **Confidence: 92%**

`HtnTaskLibrary` has two public constructors (`HtnTaskLibrary.cs:36-40, 156-172`):
```csharp
public HtnTaskLibrary(ILogger<HtnTaskLibrary>? logger = null)   // ctor A — 1 optional param
{
    _logger = logger;
    _methods = new Dictionary<string, TaskDecomposer>(StringComparer.OrdinalIgnoreCase); // EMPTY
}

public HtnTaskLibrary() : this(null)                              // ctor B — 0 params
{
    _methods = new Dictionary<string, TaskDecomposer>(StringComparer.OrdinalIgnoreCase)
    {
        ["GatherWood"] = GatherWoodDecompose, ["FindTree"] = FindTreeDecompose,
        ["MineWood"] = MineWoodDecompose, ["Collect"] = CollectDecompose,
        ["SurviveNight"] = SurviveNightDecompose, ["FindShelter"] = FindShelterDecompose,
        ["LightArea"] = LightAreaDecompose, ["WaitForSunrise"] = WaitDecompose,
        ["Wander"] = WanderDecompose, ["Explore"] = ExploreDecompose,
        ["FindFlatArea"] = FindFlatAreaDecompose,
    }; // fully populated, but this body only runs if ctor B is the one invoked
}
```
`Program.cs:319` registers it as `builder.Services.AddSingleton<HtnTaskLibrary>();` — a bare registration that lets the container choose the constructor. **This is not C# language overload resolution** (which would pick ctor B for a literal `new HtnTaskLibrary()` call) — it is the .NET DI container's own constructor-selection algorithm, which is different and, in this case, picks the other one.

Verified directly against the live `dotnet/runtime` source (`CallSiteFactory.CreateConstructorCallSite`, fetched from `raw.githubusercontent.com/dotnet/runtime/main/.../CallSiteFactory.cs`): with 2+ constructors, the container sorts them by parameter count **descending** and picks the first one whose parameters are *all* resolvable (either a registered service exists, or the parameter has a default value) as `bestConstructor` — it does not prefer the exact-arity/parameterless overload the way plain C# `new T()` would. Ctor A (1 parameter) is tried first because it has more parameters than ctor B (0). Its parameter `ILogger<HtnTaskLibrary>? logger = null` resolves successfully — `ILogger<T>` is registered by default in every ASP.NET Core / generic-host app, and this specific logger is genuinely used at `HtnTaskLibrary.cs:499`, so it's not a vestigial injection the container would skip. **Ctor A is therefore selected, and `_methods` is permanently empty for the DI-constructed singleton actually used at runtime.**

**Effect:** `HasTask(...)` returns `false` and `Decompose(...)` throws `InvalidOperationException` for every dictionary-driven task name (`GatherWood`, `FindTree`, `MineWood`, `Collect`, `SurviveNight`, `FindShelter`, `LightArea`, `WaitForSunrise`, `Wander`, `Explore`, `FindFlatArea`) on the production instance. `DecomposeBuild`/`DecomposeCraftItem`/`DecomposeGatherItem` are unaffected — they're separate public methods that don't consult `_methods` (confirmed: zero internal calls to `Decompose(` or `_methods[` from within the class other than the public `Decompose` method's own lookup).

**Reproduction:** `POST /api/agent/plan` with `{"GoalName": "GatherWood"}` or `{"GoalName": "SurviveNight"}` (`Program.cs:589-609`) constructs the goal via `GoalFactory`, routes to `PlannerRouter` (no `IGoalDecomposer` handles either type), falls to `HtnPlanner`, hits `_library.HasTask(goal.Name) == false`, then phase-by-phase fallback (`FindTree`/`MineWood`/`Collect` for GatherWood — also all false), then `TryLlmFallbackAsync`. If an LLM provider is configured and available, the failure is **silently masked** — the LLM improvises actions for what was designed to be a deterministic, tested decomposition. If no LLM is configured, the endpoint throws `InvalidOperationException`.

**Why tests don't catch it:** unit tests almost certainly construct `new HtnTaskLibrary()` directly in C# code, where normal C# overload resolution *does* prefer the parameterless ctor (a documented "fewer defaulted arguments wins" tie-break rule) — so ctor B runs, `_methods` is populated, and every test passes. The bug is invisible to any test that doesn't specifically construct the class the way DI does (`ActivatorUtilities.CreateInstance` or the container itself).

**Practical impact today:** narrower than the severity number alone suggests — see F14: the two goal types that depend on this (`GatherWoodGoal`, `SurviveNightGoal`) both appear to already be unreachable from the normal chat/LLM command surface, which is why this hasn't surfaced as a player-visible bug. It remains a live landmine for the `/api/agent/plan` endpoint and for any future code that reintroduces a dictionary-driven task as a real goal.

**Recommendation:** Delete ctor A's field initializer duplication and instead have a single constructor with the full dictionary as a field initializer, e.g.:
```csharp
public HtnTaskLibrary(ILogger<HtnTaskLibrary>? logger = null)
{
    _logger = logger;
    _methods = new(StringComparer.OrdinalIgnoreCase) { ["GatherWood"] = GatherWoodDecompose, ... };
}
```
and remove the second constructor entirely — there's no need for two once the single optional-logger constructor does the full job. Add a regression test that resolves `HtnTaskLibrary` through an actual `ServiceCollection`/`ServiceProvider` (not `new HtnTaskLibrary()`) and asserts `HasTask("GatherWood")` — this is the class of bug a "does it compile and do unit tests construct it directly" review will never catch.

---

## F11. `ReplanGovernor`'s graduated stall backoff never escalates past the second tier
**Classification:** Verified Issue · **Severity 15/25 (High)** · **Confidence: 90%**

`ReplanGovernor` is documented as implementing a "graduated retry delay [5, 10, 20, 30]s" for repeated stalls (`ReplanGovernor.cs:32-36`, Sprint 52 changelog note). Tracing the actual state transitions:

1. First stall: `Evaluate` increments `_stallAttempt` from 0 → 1 in the same call that sets `_isStalled = true` (`ReplanGovernor.cs:107-113`).
2. The *next* call to `Evaluate` while stalled reads the delay via `idx = Math.Min(_stallAttempt, _graduatedDelaysSec.Length - 1)` (`ReplanGovernor.cs:91`) — but `_stallAttempt` is already `1` at this point (set in step 1), so the very first recovery wait uses `_graduatedDelaysSec[1]` = **10s**, not `[0]` = 5s. **Index 0 (5s) is unreachable in normal operation.**
3. When the timeout elapses and auto-recovery fires — both inline in `Evaluate` (`ReplanGovernor.cs:95-99`) and in the separate `TryAutoRecover()` method (`ReplanGovernor.cs:159-163`) — `_stallAttempt` is reset to **0**, identically to what `RecordProgress()` does for genuine forward progress (`ReplanGovernor.cs:130-132`).
4. If the same underlying problem causes another stall shortly after (no real progress was made, the recovery attempt just retried the same broken plan), `_stallAttempt` increments from the reset 0 → 1 again, so the next recovery wait is **10s again**, not 20s.

Net effect: every stall-recovery cycle waits the same ~10s regardless of how many times the agent has stalled in a row on the same underlying problem. Tiers 3 and 4 (20s, 30s) and tier 1 (5s) are dead code paths under the normal auto-recovery flow — only reachable if `RecordProgress()`/`Reset()` are *not* called between stalls in some way I haven't found, which contradicts the documented intent that repeated stalling should back off progressively.

**Why this wasn't caught:** `ReplanGovernorTests.cs` has no test that stalls twice in a row through the auto-recovery path and asserts the delay actually increased between the two — the closest test uses a single-element delay array `[0]` (`ReplanGovernorTests.cs:100`), which can't exercise escalation at all.

**Recommendation:** Auto-recovery (the "let's try again, we're not sure if it's fixed" path) and confirmed progress (`RecordProgress`) are semantically different and should not both reset `_stallAttempt` to 0. Only `RecordProgress`/`Reset` should clear it. `Evaluate`'s inline auto-recovery and `TryAutoRecover()` should leave `_stallAttempt` untouched (or increment it further if the retry *also* stalls again) so the delay actually escalates: `5 → 10 → 20 → 30` and stays at 30 until real progress is recorded. Also fix the off-by-one so the first stall genuinely waits 5s, not 10s: compute the index from `_stallAttempt - 1` (or read the delay before incrementing) at the point the stall is declared.

---

## F12. The alias/fuzzy-resolution layer is entirely dead code — and its own doc comment says otherwise
**Classification:** Verified Issue / Documentation Drift · **Severity 10/25 (Moderate)** · **Confidence: 93%**

`ChatInterpreter.cs:301-306` states outright: *"The alias dictionaries (ItemAliases, BlueprintAliases, CraftAliases) and resolver methods are preserved for use by LlmChatInterpreter's item normalization in Sprint 36."* This is not true at the current commit. A full read of `LlmChatInterpreter.cs` shows exactly one reference to the alias system: `AliasRegistry.GetAliasesForPrompt()` (`LlmChatInterpreter.cs:376`), which just inlines a static 13-entry hint block into the system prompt text. There is no call anywhere in `LlmChatInterpreter.cs` to `ChatInterpreter.ResolveItem`, `ResolveBlueprint`, `ResolveCraftItem`, or `AliasRegistry.TryResolve`/`Search`. `GoalFactory`'s `GatherItem:`/`CraftItem:`/`Build:` prefix handlers (`GoalFactory.cs:69-145`) take the `itemId`/`blueprintId` substring as-is with no normalization step either.

In other words: production item/blueprint-name resolution relies entirely on the LLM emitting a correct canonical ID, nudged only by the 13-entry static hint text. The actual fuzzy-matching system that exists to catch what the hint text misses — `AliasRegistry.TryResolve` (exact alias → normalize → known-ID lookup → alias-value match → substring fuzzy match, `AliasRegistry.cs:180-227`) built for Sprint 57 TSK-0304 specifically to add "fuzzy matching" — is **never invoked from the live chat path**. Confirmed by full-repo grep: its only callers outside its own file are `Sprint30Tests.cs` (an old regression suite) and nothing else.

**Recommendation:** Either (a) wire `AliasRegistry.TryResolve` into `GoalFactory`'s `GatherItem:`/`CraftItem:` handlers as a normalization/typo-correction step before goal construction — this is genuinely useful given LLM outputs aren't 100% consistent and the mechanism already exists and is tested — or (b) if the LLM-only approach is intentional (simpler, one source of truth), delete `ChatInterpreter.ResolveItem`/`ResolveBlueprint`/`ResolveCraftItem` and `AliasRegistry.TryResolve`/`Search`/`_knownItemIds` (~150 lines) and correct the stale doc comment. Leaving it as-is is the worst of both options: dead code that looks load-bearing from its own documentation, with no actual safety net for LLM typos/variants in production.

---

## F13. `ResolveCraftItem`'s fallback would mis-resolve "iron"/"gold"/"copper" to raw ore blocks
**Classification:** Probable Issue (latent, inside dead code) · **Severity 4/25 (Low)** · **Confidence: 85%**

`ChatInterpreter.ResolveCraftItem(raw)` (`ChatInterpreter.cs:347-352`) checks `AliasRegistry.CraftAliases` first, then falls back to `ResolveItem(raw)`, which checks the shared `AliasRegistry.ItemAliases`. `CraftAliases` has no entry for bare `"iron"`, `"gold"`, or `"copper"` (only multi-word entries like `"iron pickaxe"`), so a raw craft-intent token of just `"iron"` falls through to `ItemAliases["iron"] = "iron_ore"` — a mined block, not a craftable item. This is the same alias-collision documented for gather-context in the Sprint 35 audit (`AliasRegistry.cs:22-23` explicitly calls out that the gather-oriented value wins for these three keys), but here it leaks into the craft-context fallback specifically, which the original documented trade-off didn't seem to anticipate.

Currently inert because `ResolveCraftItem` has zero call sites (see F12) — this is a "fix if you revive it" note, not an active production bug. Flagging it now so it isn't reintroduced silently if F12's recommendation (a) is adopted.

**Recommendation:** If `ResolveCraftItem` is wired back in per F12, add explicit `CraftAliases` entries for `"iron"`, `"gold"`, `"copper"` (e.g., mapping to `"iron_ingot"`, `"gold_ingot"`, `"copper_ingot"` — plausible craft targets — or reject bare ore-family names as ambiguous and ask the player to specify a tool/item). If F12's recommendation (b) is adopted instead, this becomes moot — delete it along with the rest.

---

## F14. `GatherWoodGoal`, `SurviveNightGoal`, and 9 of 11 `HtnTaskLibrary` dictionary tasks are unreachable from any live command path
**Classification:** Consolidation Opportunity · **Confidence: 88%**

Tracing real command flow end-to-end: player chat like "get wood" is parsed by `LlmChatInterpreter`/`IntentManager` into a `"gather"` intent with an item token that gets normalized (per its own comment) from `"wood"` → `"oak_log"`, which becomes a `GoalRequest("GatherItem:oak_log")` (`IntentManager.cs:228`) → `GenericGatherGoal` (implements `IItemSpecGoal`) → routed by `PlannerRouter` to `GatherGoalDecomposer`. **`GatherWoodGoal` (the older, dictionary/phase-driven goal with `Name = "GatherWood"`) is never constructed by this path.** Its only construction site is `GoalFactory.Creators["GatherWood"]` (`GoalFactory.cs:38`), reachable only via a literal goal-name string `"GatherWood"` — which nothing in `LlmChatInterpreter.cs` or `IntentManager.cs` ever emits (confirmed by grep: zero occurrences of the literal string `"GatherWood"` outside `GoalFactory`/`HtnTaskLibrary`/tests). The only live reachability path is the diagnostic `POST /api/agent/plan` endpoint (see F10).

Same story for `SurviveNightGoal` — constructible only via `GoalFactory.Creators["SurviveNight"]`, with no caller anywhere in the chat/intent layer or in `AgentBackgroundService.cs` that ever passes `"SurviveNight"` as a goal name. There is no automatic "it's getting dark, self-preserve" trigger wired into the live agent loop either (confirmed: zero references to `SurviveNightGoal` in `AgentBackgroundService.cs`).

Since neither goal type is reachable from real gameplay, the following are effectively dead legacy code, predating the later `IItemSpecGoal`/decomposer-registry generalization referenced throughout the class docs (Sprints 27, 35):
- `Agent.Planning.Goals.GatherWoodGoal` (whole file)
- `Agent.Planning.Goals.SurviveNightGoal` (whole file)
- `Agent.Planning.Decomposition.SurviveNightGoalDecomposer` (whole file)
- 9 of 11 entries in `HtnTaskLibrary._methods`: `GatherWoodDecompose`, `FindTreeDecompose`, `MineWoodDecompose`, `CollectDecompose`, `SurviveNightDecompose`, `FindShelterDecompose`, `LightAreaDecompose`, `WaitDecompose`, `ExploreDecompose` (roughly 300–400 lines total across `HtnTaskLibrary.cs`, not yet individually measured) — only `Wander`/`FindFlatArea` task-decomposer entries might still matter indirectly, but even those aren't obviously reachable as standalone top-level goals either (`Wander`/`FindFlatArea` are more commonly dispatched as *tools*, not goals — see prior report's F1 for the tool-vs-goal naming domain distinction).
- `GoalFactory.Creators["GatherWood"]`/`["SurviveNight"]` entries.

This is squarely in the "legacy fallback we can't afford to carry" category the audit brief asked about — a whole earlier generation of the goal/task-library architecture is still compiled, tested (giving false confidence it's load-bearing), and silently broken by F10, but not actually exercised by anything a player or the LLM can trigger.

**Recommendation:** Confirm with the team whether `SurviveNightGoal`/`GatherWoodGoal` are intentionally reserved for a future feature (e.g., an autonomous "it's dark, take shelter" trigger that hasn't been wired into `AgentBackgroundService` yet) or are pure legacy. If the former, prioritize actually wiring the trigger (and fix F10 first, since it's currently broken even if reactivated). If the latter, this is a clean, low-risk deletion candidate — removing an entire generation of parallel goal-decomposition machinery that current architecture has already superseded, which directly reduces the "greenfield project carrying legacy" surface area highlighted as a priority.

---

## Cross-Cutting Note: Documentation Drift Is Now a Recurring Pattern

This is the second and third confirmed instance (F6 in the prior report; F12 here) of a class-level XML doc comment asserting something about the current wiring that a direct code trace shows is false — in both cases, the comment describes a *past* refactor as if it fully completed (`HtnPlanner`'s "type-switches removed"; `ChatInterpreter`'s "resolvers preserved for LlmChatInterpreter's use in Sprint 36"). Both are plausible, confident-sounding, and wrong. Given the project's stated audit-driven workflow already treats prior sprint handoffs as a trusted source for "what's already fixed," it's worth flagging that **in-code doc comments are being trusted at the same level and are demonstrably not reliable** for that purpose in at least two places found so far — a third full pass might reasonably budget time to specifically cross-check doc comments claiming "X was removed/wired/handled elsewhere" against an actual call-site trace, since this pattern has now hit exactly 100% (2/2, plus this makes it 2 of 2 checked, not yet a large sample, but worth flagging as a category).

---

## Updated Confidence Summary (This Pass)

| Confidence band | Findings |
|---|---|
| 90–100% | F10 (92%, verified against live `dotnet/runtime` source), F11 (90%), F12 (93%) |
| 80–89% | F13 (85%), F14 (88%) |

F10 and F11 are the two most consequential findings across both reports to date: F10 because it's a category of bug (DI constructor ambiguity) that's easy to reintroduce elsewhere and trivially invisible to unit tests; F11 because it silently defeats a specifically-tuned reliability mechanism (Sprint 52 explicitly retuned these delay values, apparently without anyone verifying escalation actually occurs).
