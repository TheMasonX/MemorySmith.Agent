# MemorySmith.Agent — Deep Audit DELTA (Pass 2, dev/round-3)

Report: msa_sprint58_deep_audit_dev_round3_20260701_1500_CT_DELTA.md

Generated: 2026-07-01 16:15 CT
Repository: TheMasonX/MemorySmith.Agent
Branch: dev/round-3
Commit: `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c` (unchanged from Pass 1)

This is a **delta only** — it does not repeat F1–F9 or the confirmed-still-open items from the first report. Pass 2 covered previously-unread files in full: `HtnTaskLibrary.cs` (full read, 867 lines), `Agent.Core/ReplanGovernor.cs`, `Agent.Planning/AliasRegistry.cs`, `WebUI.Blazor/Program.cs` (full DI graph, 841 lines), `Agent.World.Minecraft/WebSocketBridge.cs`, `Agent.Planning/ChatRateLimiter.cs`, `Agent.Core/Models/ActionOutcome.cs`, and cross-checked every constructor pattern and DI registration in the solution. Two new findings resulted; a handful of other areas checked (`AliasRegistry` fuzzy matching, `ChatRateLimiter`, `WebSocketBridge`) turned up nothing new — noted at the end for completeness.

---

## Findings Table (Delta)

| # | Finding | Class | Severity | Confidence |
|---|---|---|---|---|
| D1 | `HtnTaskLibrary` has two constructors that populate `_methods` differently; the one used by production DI registration may leave the task registry **empty** | Verified code defect / **possible Critical production bug**, verification-limited | **Critical if confirmed (25/25)** | 45% that it manifests in production (see caveat below) — 100% that the split-initialization defect itself exists in source |
| D2 | `ReplanGovernor`'s graduated stall-recovery delay is off by one tier — the first stall always waits the *second* tier's delay, not the first | Verified Issue | 6/25 (Low) | 93% |

---

## D1. `HtnTaskLibrary`'s two constructors initialize `_methods` differently — production DI registration is at risk of resolving to the empty one

**Classification:** Verified code defect (source-level, 100% certain) → **possible Critical production bug (severity 25/25) contingent on .NET DI constructor selection, which I could not execute/compile-verify in this sandbox — treat this as a same-day "go verify" item, not a closed finding.**

### The defect (100% certain — direct source read)

`Agent.Planning/HtnTaskLibrary.cs:157-175`:
```csharp
public HtnTaskLibrary(ILogger<HtnTaskLibrary>? logger = null)
{
    _logger = logger;
    _methods = new Dictionary<string, TaskDecomposer>(StringComparer.OrdinalIgnoreCase);
    // ← _methods is initialized EMPTY here and never populated in this constructor.
}

public HtnTaskLibrary() : this(null)
{
    _methods = new Dictionary<string, TaskDecomposer>(StringComparer.OrdinalIgnoreCase)
    {
        ["GatherWood"]      = GatherWoodDecompose,
        ["FindTree"]        = FindTreeDecompose,
        ["MineWood"]        = MineWoodDecompose,
        ["Collect"]         = CollectDecompose,
        ["SurviveNight"]    = SurviveNightDecompose,
        ["FindShelter"]     = FindShelterDecompose,
        ["LightArea"]       = LightAreaDecompose,
        ["WaitForSunrise"]  = WaitDecompose,
        ["Wander"]          = WanderDecompose,
        ["Explore"]         = ExploreDecompose,
        ["FindFlatArea"]    = FindFlatAreaDecompose,
    };
    // ← _methods is re-assigned here with the 11 real task decomposers,
    //    AFTER the base call already set it empty.
}
```
The zero-argument constructor calls `: this(null)` (running the 1-parameter constructor's body first, which sets `_methods` to an empty dictionary), then its own body **overwrites** `_methods` with the real, populated dictionary. **Only the zero-argument constructor ever produces a working `HtnTaskLibrary`.** If anything ever calls `new HtnTaskLibrary(someLogger)` directly (the 1-parameter constructor, not routed through the 0-arg one), the resulting instance's `_methods` is permanently empty, and every call to `HasTask(...)` returns `false` while every call to `Decompose(...)` throws:
```csharp
public IReadOnlyList<ActionData> Decompose(string taskName, string[] parameters, WorldState state)
{
    if (!_methods.TryGetValue(taskName, out var decompose))
        throw new InvalidOperationException(
            $"No decomposition registered for task '{taskName}'. ...");
    ...
}
```
This would affect `GatherWood`, `FindTree`, `MineWood`, `Collect`, `SurviveNight`, `FindShelter`, `LightArea`, `WaitForSunrise`, `Wander`, `Explore`, and `FindFlatArea` — i.e., every task-library-driven goal that isn't handled by a registered `IGoalDecomposer` (per `HtnPlanner`'s fallback role, confirmed in Pass 1 F6).

### The exposure (confirmed via source, effect on production unverified)

`WebUI.Blazor/Program.cs:319`:
```csharp
builder.Services.AddSingleton<HtnTaskLibrary>();
```
This is a **bare** registration with no factory delegate — unlike almost every other complex type in this same file (`ActionRegistry`, `DecomposerRegistry`, `HtnPlanner`, `ToolDispatcher`, etc.), which are all registered via explicit `sp => new Foo(...)` factories that call the exact constructor the author intended. Because `HtnTaskLibrary` is registered bare, **which constructor runs is decided by the built-in `Microsoft.Extensions.DependencyInjection` container's own constructor-selection algorithm**, not by an explicit call site the codebase controls.

The documented behavior of that container (`ActivatorUtilities`/internal `CallSiteFactory`) is: among all public constructors, it selects the one with the **most parameters that are all resolvable as registered services** (a parameter's own default value is only used as a last resort if the parameter type isn't otherwise resolvable). `ILogger<HtnTaskLibrary>` **is** resolvable here — this app is built with `WebApplication.CreateBuilder(args)` (`Program.cs:22`), which registers the full generic logging infrastructure (`ILogger<T>` for any `T`) automatically. That makes the 1-parameter constructor "satisfiable via real injection," and per the documented algorithm it is preferred over the 0-parameter constructor because it has more resolvable parameters. If that holds here, **the production DI container is expected to select the constructor that leaves `_methods` empty.**

### Why I am not reporting this at 100% confidence

I do not have a .NET SDK available in this sandbox (network egress is restricted to the allow-list in this environment, which does not include the .NET package feeds; `apt-get install dotnet-sdk-8.0` failed with 404s against the mirrors reachable here) and could not compile-and-run a minimal repro of `new ServiceCollection().AddLogging().AddSingleton<HtnTaskLibrary>().BuildServiceProvider().GetRequiredService<HtnTaskLibrary>().HasTask("GatherWood")` to confirm the outcome empirically. I'm relying on documented framework behavior rather than an executed test, so I'm flagging this as **high-priority-to-verify** rather than a confirmed-closed finding. I also note the obvious counter-argument: if this always reproduced, "gather wood," "survive the night," and "wander" would be completely non-functional in every real playtest since whenever this dual-constructor pattern was introduced, which seems like it should have been caught immediately in manual testing — that tension is exactly why this needs a two-minute empirical check rather than being taken on faith either direction. It's possible something about this app's specific DI setup order, a scoped/factory registration I missed elsewhere, or a subtlety in how `AddSingleton<T>()` (as opposed to `AddScoped`/`AddTransient`) resolves constructors changes the outcome.

**100% certain regardless of the above:** the two-constructor split-initialization pattern is a real defect independent of which one DI happens to pick today. It is fragile by construction — any future refactor (e.g., adding a third constructor, changing DI registration to use `[ActivatorUtilitiesConstructor]`, or a framework upgrade that changes constructor-selection tie-breaking) could silently flip which constructor executes, with no compiler error and no test catching it, because **every single unit test in the suite calls `new HtnTaskLibrary()` directly** (`CraftItemGoalTests.cs`, `HtnPlannerTests.cs`, `HtnPlannerBuildTests.cs`, `HtnTaskLibraryCraftingTests.cs`, `HtnTaskLibraryExtraTests.cs`, `Sprint19Tests.cs`, and others — grep confirms zero tests resolve `HtnTaskLibrary` through a `ServiceProvider`/`ServiceCollection`). The test suite exercises a code path production doesn't necessarily use.

### Recommendation (do this regardless of the verification outcome)

1. **Immediate, 2-minute check:** in `Program.cs`, change line 319 to an explicit factory: `builder.Services.AddSingleton<HtnTaskLibrary>(sp => new HtnTaskLibrary());` (or `sp => new HtnTaskLibrary(sp.GetService<ILogger<HtnTaskLibrary>>())` **only if** the constructor is also fixed per #2). This removes all ambiguity immediately and is a one-line, zero-risk change regardless of what DI would have done.
2. **Root-cause fix:** delete the split-initialization anti-pattern. Either (a) remove the 1-parameter constructor's independent field assignment and have it delegate to the population logic too (e.g., extract the dictionary literal into a static factory method both constructors call), or (b) collapse to a single constructor with an optional logger parameter and always populate `_methods` in one place.
3. **Regression guard:** add a test that mirrors the *exact* production registration — build a real `ServiceCollection`, call `.AddLogging()` and `.AddSingleton<HtnTaskLibrary>()` exactly as `Program.cs` does, resolve it, and assert `HasTask("GatherWood")`, `HasTask("SurviveNight")`, and `HasTask("Wander")` are all `true`. This is the test that should have existed already and would have caught this regardless of which way DI resolves it today.
4. Audit the rest of `Program.cs` for other bare `AddSingleton<T>()`/`AddScoped<T>()`/`AddTransient<T>()` calls against types with multiple public constructors — I found two bare registrations besides `HtnTaskLibrary` (`ChatRateLimiter`, `IntentManager`, `LiveLogBuffer`, `ChatInterpreter`, `LlmContextLogger`), but only `ChatInterpreter` has multiple constructors, and its alternate constructor requires a non-optional `string` parameter that DI cannot satisfy anyway, so it doesn't share this specific hazard (confirmed by reading both constructors in full — the ambiguity precondition that hits `HtnTaskLibrary` requires *two or more* constructors that are all independently DI-resolvable, and `ChatInterpreter` doesn't meet that bar). `HtnTaskLibrary` appears to be the only type in the solution with this specific dual-resolvable-constructor hazard.

---

## D2. `ReplanGovernor`'s graduated stall-recovery delay is off by one tier

**Classification:** Verified Issue · **Severity 6/25 (Low)** · **Confidence: 93%**

`Agent.Core/ReplanGovernor.cs` documents (and is clearly intended to implement) a graduated backoff schedule `[5, 10, 20, 30]` seconds (`DefaultStallGraduatedDelaysSec`, line 36) — i.e., the first stall should wait 5s before an auto-recovery attempt, the second consecutive stall 10s, etc. The increment that is supposed to drive this schedule happens at the moment a stall is **declared**, not at the moment a recovery **fails**:
```csharp
if (_identicalPlanCount >= _threshold)
{
    _isStalled = true;
    _stalledAt = DateTimeOffset.UtcNow;
    _stallAttempt++; // increment so next recovery uses longer delay
    return ReplanVerdict.Stalled;
}
```
`_stallAttempt` starts at `0`. On the very first stall, this line immediately bumps it to `1` — and the *very next* read of `_stallAttempt` (by `CurrentStallDelay`, the auto-recovery check inside `Evaluate`, or `TryAutoRecover`) uses `_graduatedDelaysSec[Math.Min(_stallAttempt, 3)]`, i.e., index `1` → **10 seconds**, not index `0` → 5 seconds. The comment ("increment so next recovery uses longer delay") describes the intended effect for a *future* stall, but there is no separate "delay for the stall currently in progress" vs. "delay for the next one" — it's the same field, read on the very next check, so the increment actually shortens the schedule by skipping tier 0 entirely on every occurrence, not just subsequent ones. Net effect: the agent's very first stall-recovery wait is 10s instead of the documented/intended 5s, the second is 20s instead of 10s, and the third-and-beyond all cap at the final 30s tier one stall earlier than the `[5,10,20,30]` schedule implies. `RecordProgress()` and `Reset()` correctly zero out `_stallAttempt`, so this only affects the *shape* of the graduated backoff, not its eventual convergence to the 30s cap — the practical impact is the agent waits somewhat longer than intended before each of its first few stall-recovery attempts.

**Why this wasn't caught:** `MemorySmith.Agent.Tests/ReplanGovernorTests.cs` is the only test file that constructs a governor with a custom `stallGraduatedDelaysSec` array, and it uses a single-element array (`stallGraduatedDelaysSec: [0]`, line 100). With a 1-element array, `Math.Min(_stallAttempt, 0)` is always `0` regardless of `_stallAttempt`'s value, so the off-by-one is structurally invisible to that test — it can never observe which tier index is actually selected.

**Recommendation:** Move the `_stallAttempt++` to occur only when a recovery attempt is granted-but-then-fails again (i.e., increment in the branch that re-enters `Stalled` after a `Proceed`, rather than in the branch that first declares the stall), or equivalently, use `_graduatedDelaysSec[Math.Min(_stallAttempt - 1, ...)]` with a floor at 0 when reading the delay so the just-incremented value doesn't apply to itself. Add a test with the real `[5, 10, 20, 30]` array asserting the first stall's `CurrentStallDelay` is `5s`, not `10s`.

---

## Areas Re-checked With No New Findings

For completeness — these were read in full during this pass and did not surface anything beyond what's already in Report 1 or already tracked:

- `Agent.Planning/AliasRegistry.cs` (300 lines, full read) — alias tables and fuzzy-match logic (`TryResolve`, `Search`) are internally consistent; the `.Take(2)`/`Count == 1` ambiguity-avoidance in fuzzy matching is a deliberate, reasonable design choice, not a bug.
- `Agent.Planning/ChatRateLimiter.cs` (full read) — sliding-window + per-player cooldown logic is correct and properly locked.
- `Agent.World.Minecraft/WebSocketBridge.cs` (structural + header read) — wire-protocol documentation is consistent with `ActionProtocol.cs` and corroborates Pass-1 F1's root cause (ADR-010 forwards `ActionData.Tool` as-is, no lowercasing) rather than adding anything new.
- `Agent.Core/Models/ActionOutcome.cs` (partial read) — `OutcomeType`/`StructuredEffect` vocabulary is coherent with its documented intent.
- `WebUI.Blazor/Program.cs` (full 841-line read) — used specifically to build the DI-registration audit behind D1; no other bare-registration/multi-constructor hazards found besides the one noted in D1's recommendation #4.
