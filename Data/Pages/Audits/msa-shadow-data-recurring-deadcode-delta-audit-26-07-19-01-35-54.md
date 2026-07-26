# MemorySmith.Agent — Delta Audit #4: Shadow-Copied Reference Data, Recurring Dead-Code Pattern, Test-Suite Health

**Repo/commit:** `TheMasonX/MemorySmith.Agent` @ `0f27af1befb72e7534421a1bd5550aee1e077d96` (`dev/round-3`) — same commit as prior three reports.
**This is a delta report.** Contains only findings not already in the three prior reports or in `Data/Tasks/*.json`.
**This pass covered:** the full NUnit test suite (822 `[Test]` methods across 61 files, quality + organization audit), the D11 follow-ups flagged-but-not-verified in Delta #2 (`WorldStateProjector.cs` self-clone, `HtnTaskLibrary.cs` internal clones, `CommonMinecraftBlocks.cs` vs `HtnTaskLibrary.cs` clones), and a cross-check of `Agent.Core/Runtime` history against the task tracker's own record of the same problem being flagged before.

---

## Executive Summary

| # | Finding | Class | Confidence | Impact |
|---|---|---|---|---|
| G1 | **Three domain-data dictionaries are shadow-copied, and the "canonical" versions are the dead ones.** `Agent.Core.CommonMinecraftBlocks` declares `public` `PlankToLogMap`, `IronIngotRequirements`, and `CobblestoneRequirements` — but **zero code anywhere references them**. `Agent.Planning.HtnTaskLibrary` instead re-declares `private` copies of all three, **byte-for-byte identical** (down to entry order and casing) except the access modifier, and those are the ones actually used. The same file *correctly* delegates to `CommonMinecraftBlocks.DirectMineBlocks` elsewhere ("single source of truth" per its own Sprint-14 comment) — proving the right pattern was known and applied once, then not followed for these three. | Duplicated Code / Dead Code (inverted — the "shared" copy is the dead one) | 96% | Medium (drift risk: a future 9th wood type only needs updating in the copy that's used, but nothing stops someone editing the dead public one and being confused when it has no effect) |
| G2 | **`WorldStateProjector.StoreFacts`'s event-type switch duplicates the `Pos`/`Health`/`Food` fact-setting body between `SpawnEvent` and `StatusEvent`** (byte-identical 3-line block), with `HealthEvent` handling a subset (`Health`/`Food` only) via its own third copy. jscpd-flagged, hand-verified. | Duplicated Code / Repeated Switches | 92% | Low-Medium |
| G3 | **`HtnTaskLibrary`'s creative-mode and survival-mode build-placement loops duplicate ~25 lines of filtering logic** (skip already-placed blocks per TSK-0125, skip placement at the bot's own position per TSK-0123, tag context, add to actions) — two parallel copies of the same loop, one per game-mode branch. Both historical fixes (TSK-0123, TSK-0125) had to be hand-applied to both copies when they landed; a third fix would face the same risk. | Duplicated Code / bug-risk multiplier | 93% | Medium |
| G4 | **The exact "AgentRuntime scaffolding accumulates without ever being consumed" failure mode has now occurred, been marked fixed, and recurred — at least twice.** TSK-0126 (Sprint 51, status `Done`) explicitly identified and removed a first instance (`_agentRuntime` field injected into `AgentBackgroundService`'s constructor but read nowhere in ~2,300 lines) — confirmed genuinely fixed; the field is gone from ABS today. But a structurally identical problem was later reintroduced (the `AgentRuntime` record + 5 `*ManagerImpl` classes flagged in Report #1's F2) and marked resolved again via TSK-0292, without the second instance ever actually being wired either. Two separate "Done" resolutions of the same architectural mistake, neither of which held — this points at a process gap (nothing in CI catches "DI-registered singleton with zero resolvers") rather than a one-off oversight. | Recurring architectural failure / process gap | 90% | Medium (mostly a "fix the process, not just the code" signal) |
| G5 | **Undetected duplicate backlog tasks**: TSK-0364 and TSK-0397 both describe the identical bug (`GatherItemDecompose` emits a full-count `MineBlock` action for *every* source-block variant of an item — e.g. 7 separate 10-count mining actions for `oak_log`'s 7 wood-type variants — rather than tracking cumulative yield across variants), filed from two different 2026-07-11 audit passes that didn't cross-reference each other. Not caught by the Wave D/E handoff's own duplicate-cleanup pass (which caught other pairs but not this one). | Backlog hygiene | 91% | Low (SNR cost only) |
| G6 | **Test-suite organization nit**: 23 of 61 test files are named `SprintNTests.cs` (containing 44 distinct, often unrelated `[TestFixture]` classes bundled by *when written* rather than *what's tested* — e.g. `Sprint39Tests.cs` alone holds 9 fixtures spanning goal IDs, `LlmEvaluatorImpl`, `ChatInterpreter`, and JSON truncation handling). This is the test-suite analog of Report 2's D10 (`"Sprint N"` comment-as-changelog) applied to file layout: finding every test for a given production class means grepping across a dozen files instead of opening one. The suite is otherwise healthy — 822 `[Test]` methods, 100% contain at least one assertion by direct scan, zero `[Ignore]`/skip attributes, only 3 uses of the weak "just don't throw" assertion pattern. | AI-codesmell / maintainability nit | 85% | Low |

**Overall read on this pass:** the test suite itself is in good shape — this is a genuine positive finding worth stating plainly rather than only reporting problems. The real substance this round is G1 (a textbook "we built the shared abstraction and then didn't use it" case, with the proof of the correct pattern sitting three lines away in the same file) and G4 (evidence that this project's dead-scaffolding problem, flagged repeatedly across this audit series, is a *recurring* pattern rather than an isolated one — worth a structural fix, e.g. a Roslyn analyzer or a lightweight CI check flagging DI-registered types with no resolvers, rather than relying on audits to keep catching it).

---

## Detailed Findings

### G1 — Shadow-copied domain-data dictionaries; the shared "source of truth" is unused

**Evidence.**
```
$ grep -rn "CommonMinecraftBlocks.PlankToLogMap\|CommonMinecraftBlocks.IronIngotRequirements\|CommonMinecraftBlocks.CobblestoneRequirements" --include=*.cs
(no results — zero consumers of the public canonical versions, anywhere)
```
`Agent.Core/CommonMinecraftBlocks.cs`:
```csharp
public static readonly IReadOnlyDictionary<string, string> PlankToLogMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [8 entries] };
public static readonly IReadOnlyDictionary<string, int> IronIngotRequirements = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [9 entries] };
public static readonly IReadOnlyDictionary<string, int> CobblestoneRequirements = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [5 entries] };
```
`Agent.Planning/HtnTaskLibrary.cs` — three `private` fields with the identical dictionary contents, entry order, and casing (`diff` confirms zero content difference beyond the `public`→`private` modifier and doc-comment wording).

The same file, three lines earlier in its own history (Sprint 14 comment): `DirectMineBlocks now delegates to CommonMinecraftBlocks.DirectMineBlocks` — i.e., `HtnTaskLibrary` **already has the correct, established pattern for this exact situation**, used for a fourth dataset, in the same file. The three maps in this finding simply didn't follow it — most likely because each was added in a separate sprint (per the surrounding "Sprint N" comments, consistent with Report 2's D10 finding about sprint-by-sprint accretion without a consolidation pass) without checking whether an equivalent already existed in `Agent.Core`.

**Why "the shared copy is dead" matters beyond tidiness:** it inverts the usual risk. Normally the worry with duplicated reference data is "the copy goes stale while the original updates." Here, the *original* is the one nobody is watching — if someone updates `CommonMinecraftBlocks.IronIngotRequirements` (reasonably assuming it's the live source, since it's `public` and named for exactly this purpose) expecting it to affect crafting behavior, nothing happens, because the actually-executing code path reads `HtnTaskLibrary`'s private copy instead. That's a silent no-op bug waiting to happen, not yet triggered only because no one has touched the public copy since it was written.

**Recommendation.**
1. Delete `HtnTaskLibrary`'s three private dictionaries; replace their use sites with `CommonMinecraftBlocks.PlankToLogMap` / `.IronIngotRequirements` / `.CobblestoneRequirements`, mirroring the existing `DirectMineBlocks` delegation exactly.
2. This is almost entirely mechanical (delete 3 field declarations + find/replace the ~dozen use sites within `HtnTaskLibrary` to the qualified name) and low-risk since the content is already proven identical.
3. Consider a one-time repo-wide pass (or a jscpd run scoped specifically to `Agent.Core/*.cs` vs every other project) to check whether this same "public canonical field, unused, shadowed by a private copy elsewhere" shape recurs for any of `CommonMinecraftBlocks`'s *other* public members — this audit checked the 3 jscpd-flagged pairs specifically but did not exhaustively diff every public member of `CommonMinecraftBlocks` against every other file.

**Confidence: 96%.** The zero-consumer claim and the byte-identical-content claim are both direct, mechanically verified facts (grep + diff), about as strong as static evidence gets.

---

### G2 — Duplicated fact-setting body across `WorldEvent` switch cases in `WorldStateProjector`

**Evidence** (`Agent.Core/WorldStateProjector.cs`, `StoreFacts`):
```csharp
case SpawnEvent e:
    result = result.With(b => {
        b.SetFact($"{prefix}Pos", e.Pos.ToString(), source);
        b.SetFact($"{prefix}Health", e.Health.ToString(), source);
        b.SetFact($"{prefix}Food", e.Food.ToString(), source);
    });
    break;
case HealthEvent e:
    result = result.With(b => {
        b.SetFact($"{prefix}Health", e.Health.ToString(), source);
        b.SetFact($"{prefix}Food", e.Food.ToString(), source);
    });
    break;
...
case StatusEvent e:
    result = result.With(b => {
        b.SetFact($"{prefix}Pos", e.Pos.ToString(), source);
        b.SetFact($"{prefix}Health", e.Health.ToString(), source);
        b.SetFact($"{prefix}Food", e.Food.ToString(), source);
    });
    break;
```
`SpawnEvent` and `StatusEvent` bodies are byte-identical; `HealthEvent`'s is a strict subset. This is a textbook Repeated-Switches / duplicated-case-body pattern — three separate `WorldEvent` subtypes independently re-stating "how to record position/health/food as facts."

**Recommendation.** Extract a private helper, e.g. `static WorldStateBuilder.Builder SetVitals(WorldStateBuilder.Builder b, string prefix, Position pos, double health, double food)` (or a narrower `SetHealthFood` overload for the `HealthEvent` case), and call it from all three cases. Low-risk, purely mechanical; also shrinks one of the files already flagged as carrying meaningful "Sprint N" comment bloat (Report 2, D10 — this file was #5 on that list with 35 occurrences).

**Confidence: 92%.**

---

### G3 — Duplicated build-placement filter loop across creative/survival branches

**Evidence** (`Agent.Planning/HtnTaskLibrary.cs`, two locations ~80 lines apart within the same build-decomposition method): both the creative-mode branch (operating on `creativeBlockActions`/`creativeBotPos`) and the survival-mode branch (`blockActions`/`botPos`) independently implement:
```csharp
for (int i = 0; i < <list>.Count; i++)
{
    // TSK-0125: skip blocks already marked as placed
    var statusKey = BuildFactKeys.BlockStatus(blueprint.Name, i);
    if (state.Facts.TryGetValue(statusKey, out var statusVal) &&
        statusVal?.ToString() == BuildFactKeys.BlockStatusPlaced)
        continue;

    var placeAction = <list>[i];

    // TSK-0123: skip PlaceBlock at bot's current position — no reference surface.
    if (placeAction.Arguments.TryGetValue("x", out var px) && px is int placeX &&
        placeAction.Arguments.TryGetValue("y", out var py) && py is int placeY &&
        placeAction.Arguments.TryGetValue("z", out var pz) && pz is int placeZ)
    {
        if (placeX == <botPos>.X && placeZ == <botPos>.Z && placeY == <botPos>.Y)
            continue;
    }

    placeAction.Context[BuildFactKeys.PlaceBlockProgressBlueprintId] = blueprint.Name;
    placeAction.Context[BuildFactKeys.PlaceBlockProgressBlockIndex]  = i;
    actions.Add(placeAction);
}
```
The inline `// TSK-0123` / `// TSK-0125` comments are themselves evidence this is risky duplication in practice, not just in theory: both fixes were real bugs that had to be independently patched into **both** copies when they were discovered. A third bug in this filtering logic (e.g., an off-by-one in the position comparison, or a new skip condition) faces the identical risk of being fixed in one branch and missed in the other — exactly the failure mode that already played out once on the JS side (Report 1, F6's `BLOCK_MINING_ALIASES` duplicate, where a fix landed in one copy and not the other).

**Recommendation.** Extract a shared private method, e.g. `FilterPlacementActions(IReadOnlyList<ActionData> allBlockActions, WorldState state, string blueprintName, Position botPos)`, returning the filtered+tagged list; call it from both the creative and survival branches. This is a same-file, same-method refactor — low risk, and it directly reduces the "next placement-logic bug needs two coordinated edits" exposure.

**Confidence: 93%.**

---

### G4 — The AgentRuntime dead-scaffolding pattern has recurred, and been marked "Done" twice, without holding either time

**Evidence.** TSK-0126 ("fix lying comment in GatherItemDecompose, resolve AgentRuntime dead code"), status `Done`, closed in Sprint 51 (~9 sprints before this audit's commit). Its description explicitly names the problem: *"`_agentRuntime` field is injected in `AgentBackgroundService` constructor... but appears unused anywhere in ~2300 lines. Either integrate it... or remove dead code."* Verified: `AgentBackgroundService.cs` at HEAD has **zero** references to `agentRuntime`/`AgentRuntime` — this specific instance genuinely was fixed (the dead constructor parameter was removed, not just hidden).

However, Report #1 (finding F2, this audit series) independently — without prior knowledge of TSK-0126 — found and confirmed that a **structurally identical** problem exists today: an `AgentRuntime` record + 5 `*ManagerImpl` classes, registered in DI (`Program.cs:403`), with **zero consumers anywhere**, doc-commented as a "Sprint 40 target" that's now 20 sprints overdue, and separately marked `Done` via TSK-0292 without that resolution ever actually holding either.

**The pattern, stated precisely:** this project has now built "AgentRuntime"-shaped orchestration scaffolding, had it flagged as dead/unwired, and had that flag marked resolved, on two separate and non-overlapping occasions (Sprint 51 / TSK-0126, and sometime before Sprint 55 / TSK-0292) — and in the second occasion, the "resolution" didn't actually wire anything either. This is stronger and more specific evidence than either individual false-completion finding on its own: it suggests the recurring failure isn't "one bad sprint" but **the absence of any mechanical check** that would catch "a DI-registered service type has zero resolvers" before a task gets marked Done on the strength of "the code compiles and the class exists."

**Recommendation.**
1. Beyond the code-level fix already recommended in Report #1 (F2 — wire or delete the current `AgentRuntime`/Managers scaffolding), recommend a **process-level** fix: a lightweight CI check (even a simple script grepping for `services.Add*<IFoo` registrations with no corresponding `GetRequiredService<IFoo>`/constructor-injection use elsewhere in the same solution) run as part of the Definition of Done for any task claiming to "wire" or "integrate" a component. This is the kind of check a human reviewer might reasonably skip (the code compiles, tests may even pass with the DI registration present but unused) but that would have caught both TSK-0292 and TSK-0309 (Report #1, F3) before they were marked Done.
2. When TSK-0292 is reopened (per Report #1's recommendation), consider explicitly linking it to TSK-0126 in its record, so the next reader has the full history of this specific recurring mistake rather than rediscovering it a third time.

**Confidence: 90%.** The "TSK-0126 genuinely fixed its specific instance" claim and the "a structurally identical problem exists today, separately marked Done" claim are both independently verified facts. The characterization of this as a "process gap" rather than "bad luck twice" is a reasonable inference from the pattern, not something provable outright — hence not higher.

---

### G5 — Duplicate backlog tasks: TSK-0364 and TSK-0397

**Evidence.** Both `Backlog`, both dated 2026-07-11, both describing `GatherItemDecompose` emitting one full-count `MineBlock` action per source-block variant rather than tracking cumulative yield (TSK-0364's example: 7 oak-family wood variants each getting a separate mining action; TSK-0397's: identical scenario, `OakLogSpec`'s 7 source blocks). Filed from two different audit documents (`codebase-audit-20260711-10agent-swarm-5chair-council.md` P1-001, and an "Internal audit Sprint 60... MSA-PLAN-001") that evidently didn't cross-check each other before filing. Not caught by the Wave D/E handoff's own duplicate-cleanup pass, which flagged other pairs (TSK-0384/0407, TSK-0407/0408, TSK-0380–82/0387–89 — see Report 1, F7) but missed this one.

**Recommendation.** Merge into one task (TSK-0397 has the more complete reproduction detail and a clearer proposed fix name — "mine single source block with fallback" — so it's the natural keeper); archive TSK-0364 with a pointer to TSK-0397, following the same pattern already used for the other duplicate pairs.

**Confidence: 91%.**

---

### G6 — Test file organization: sprint-numbered files bundle unrelated fixtures

**Evidence.**
```
$ ls MemorySmith.Agent.Tests/Sprint*.cs | wc -l
23
$ grep -h "public sealed class\|public class" MemorySmith.Agent.Tests/Sprint*.cs | wc -l
44
```
23 files named by sprint number contain 44 separate `[TestFixture]` classes — e.g. `Sprint39Tests.cs` (708 lines) alone holds 9 unrelated fixtures: `Sprint39GoalIdTests`, `Sprint39LlmEvaluatorSignatureTests`, `Sprint39IntentDraftNamespaceTests`, `Sprint39IChatInterpreterContractTests`, `Sprint39ChatInterpreterFastPathTests`, `Sprint39TruncatedJsonTests`, `Sprint39LlmEvaluatorImplTests`, `Sprint39TypedGoalRequestTests`, `Sprint39SchemaValidationExtensionTests`. By contrast, the other 38 test files are named after the class they test (`AgentBackgroundServiceTests.cs`, `WorldStateProjectorTests.cs`, `CraftItemGoalTests.cs`, etc.) and are easy to locate. Finding *all* tests relevant to, say, `LlmEvaluatorImpl` today means checking `Sprint39Tests.cs` and potentially several other `SprintNTests.cs` files rather than a single obviously-named file.

**Positive finding, stated for balance:** the suite itself is healthy by the metrics this pass could check — 822 `[Test]` methods, a direct scan found 0 with no `Assert`/`.Should()` call in their body, 0 `[Ignore]`/skip attributes anywhere, and only 3 total uses of the weaker "assert it doesn't throw" pattern as opposed to asserting actual outcomes. This is worth stating plainly: the team's test discipline is solid; this finding is purely about *findability*, not *quality*.

**Recommendation.** Not urgent — a good candidate for opportunistic cleanup rather than a dedicated task: as files are touched for other reasons, consider splitting multi-fixture `SprintNTests.cs` files into per-class files and merging fixtures for the same production class that are currently scattered across multiple sprint files. If a dedicated pass is ever justified, it would naturally pair with the "Sprint N comment" cleanup already recommended in Report 2 (D10), since both stem from the same "organize by when, not by what" habit.

**Confidence: 85%.** The file/class counts are exact; whether this rises to "worth prioritized cleanup" vs. "acceptable as-is" is a judgment call, hence not higher.

---

## Assumptions & Open Questions

1. **G1's recommendation assumes `Agent.Planning` already has a project reference to `Agent.Core`** sufficient to use `CommonMinecraftBlocks` directly — confirmed true (it already does, via the existing `DirectMineBlocks` delegation in the same file), so no new project reference is needed.
2. **G4's "process gap" framing is a recommendation, not a directive** — the team may already have reasons (velocity pressure, reviewer bandwidth) that a lightweight CI check wouldn't fully address; it's offered as the most mechanical fix that directly targets the specific, now twice-observed failure mode, not as the only possible fix.
3. **G1's exhaustiveness caveat**: this pass diffed the 3 jscpd-flagged pairs specifically; a full member-by-member diff of `CommonMinecraftBlocks` against the rest of the codebase (to check for a 4th or 5th shadow-copy instance) was not performed and could turn up more.
4. **No dynamic verification** was possible for G2/G3 (no running agent/Minecraft server in this sandbox) — both findings are static-duplication observations; their *behavioral* correctness (i.e., that the two copies really do produce identical runtime behavior today, not just identical source text) was checked by reading, not by execution.

---

## Suggested Sequencing (additive to prior reports)

1. G1 (delete shadow dictionaries, reference the canonical ones) — mechanical, high-confidence, low-risk; good candidate to batch with Report 3's E2 (tool schema caching) as a "small mechanical cleanups" PR.
2. G5 (merge duplicate tasks) — 5 minutes, zero code risk.
3. G2, G3 — same-file/same-method extractions, low risk, good pairing with the F1 (AgentBackgroundService decomposition) and D-series cleanup work already queued from prior reports.
4. G4's code-level component (wiring/deleting AgentRuntime) was already sequenced in Report 1; this report only adds the process-check recommendation, which can be scoped independently and doesn't block anything else.
5. G6 — no urgency; opportunistic only.
