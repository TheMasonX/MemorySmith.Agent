# Features Reference

A cross-sprint catalogue of agent features, their configuration knobs, and the architectural notes worth remembering when tuning or extending them. Sprints are listed newest-first.

> **New: Feature wiki pages** in [`Data/Pages/Features/`](../Features/) provide comprehensive overviews of each major subsystem. This reference focuses on detailed per-sprint configuration notes. Start with the [Feature Deep-Dives](../home.md#feature-deep-dives) table on the home page.

## Sprint 23 — Damage interrupt + World KB routing

### `DamageTakenEvent` and damage interrupt system

**What it does.** Whenever the agent's health drops by at least a threshold amount in a single update, the current action plan is atomically discarded and a priority `GetStatus` action is enqueued so the planner re-evaluates against fresh health, hunger, and threat context.

**How it's wired.**
- The Node.js Mineflayer adapter still only emits raw `HealthEvent`s — the projector compares consecutive HealthEvents on the C# side and synthesizes a `DamageTakenEvent` when the new value is lower than the previous one.
- `DamageTakenEvent` carries `PreviousHealth`, `Health`, `Delta` (always negative), `Food`, and `Timestamp`.
- The interrupt path calls `ActionQueue.ClearAndEnqueue(GetStatus)` so a concurrent chat-response Enqueue cannot slip between the clear and the priority push.

**Configuration.** None required for the default 6 HP threshold; per-goal overrides via `IGoal.DamageInterruptThresholdHp` (below).

**Architecture note.** Routing damage-specific behavior through `DamageTakenEvent` instead of subscribing to `HealthEvent` means healing/eating no longer accidentally trips damage handlers — a recurring class of bug in Sprints 18–21.

### `IGoal.DamageInterruptThresholdHp`

**What it does.** Lets each goal opt out of, raise, or lower the default damage interrupt threshold.

**Semantics.**
- `null` (default interface implementation) — use the system default of **6 HP**
- `0` — never interrupt this goal on damage; reserved for future combat goals that need to manage their own damage response without the planner ripping the plan out mid-swing
- Any positive integer — goal-specific threshold (e.g., a fragile exploration goal might set `3` to bail out earlier)

**Configuration.** Override the property on your `IGoal` implementation; existing goals need no changes.

**Architecture note.** Default interface implementation means this is a non-breaking addition — all current goals automatically opt into the 6 HP default without source changes.

### World KB tool routing

**What it does.** Splits the agent's two MemorySmith calls along functional lines:
- `SearchMemory` and `CreatePage` route to the **world KB** for in-game observations, block discoveries, biome notes, and exploration history
- `GetPage` routes to the **agent KB** for sprint docs, design notes, and code documentation

**How it's wired.** `Program.cs` resolves a keyed `IMemoryGateway` (`"world"`) via `IServiceProvider.GetKeyedService<IMemoryGateway>("world")` and passes the appropriate gateway to each tool's constructor. When the world key resolves to null (unconfigured), it falls back to the agent gateway so the agent still functions.

**Configuration.** Set `Agent:Memory:WorldKbUrl` in `appsettings.json`. See [world-kb-deployment.md](world-kb-deployment.md) for instance setup.

**Architecture note.** Tool descriptions (visible to the LLM during planning) explicitly call out the routing so the model picks the right tool — historically the model would alternate between SearchMemory and GetPage for the same intent.

### `WorldKbUrl` null default + startup warning

**What it does.** `RestMemoryGatewayOptions.WorldKbUrl` now defaults to `null` instead of the previous `"http://127.0.0.1:6869"`. On startup, if `agentEnabled && WorldKbUrl is null`, the host logs a `LogWarning` pointing at the migration note and the deployment guide.

**Migration.** If you relied on the implicit localhost default in earlier sprints, set `WorldKbUrl` explicitly in `appsettings.json`. Otherwise the agent and world memories will share a single MemorySmith instance and the warning will fire each boot.

**Architecture note.** Removing the hardcoded localhost default eliminates a class of subtle "why does prod store agent docs and world facts in the same KB?" misconfigurations. The startup warning makes the implicit fallback visible instead of silent.

### `ActionQueue.ClearAndEnqueue` atomic interrupt method

**What it does.** Atomically clears any pending actions and enqueues a single priority action in one `lock`-protected operation. `EnqueueAll` is now also lock-protected so the interrupt path observes a consistent queue.

**Why.** Without atomicity, a concurrent `Enqueue` from `ChatConsumerAsync` (or a bulk `EnqueueAll` from the planner) could slip between a separate `Clear` and the priority push, defeating the damage interrupt by leaving a stale chat response or partial plan ahead of `GetStatus`.

**Architecture note.** This builds on the Sprint 12 switch from `Queue<T>` to `ConcurrentQueue<T>` — `ConcurrentQueue` makes individual ops thread-safe, but does not give you compound atomic ops. `ClearAndEnqueue` provides the compound op the interrupt path specifically needs without coarsening locks across the rest of the queue surface.

## Sprint 22 — Planner completeness + World KB separation

### `CraftItemGoal` staleness gate

**What it does.** `CraftItemGoal` now refuses to re-issue a craft request when its last inventory snapshot is older than a freshness window (currently tied to the runtime replan interval). Prevents the planner from queuing a craft against stale state immediately after an inventory event flushes.

**Configuration.** Tunable via `Agent:Runtime:ReplanIntervalSeconds`.

**Architecture note.** Surfaced as a bug in Sprint 21 where the agent would request a craft, get an inventory update mid-action, and then re-request the same craft against the pre-update snapshot.

### HtnPlanner quantity propagation fix

**What it does.** Sub-task quantities (e.g., "mine 8 oak_log" inside "craft 4 plank") now correctly propagate up the HTN decomposition tree. Previously a Sprint 19 refactor silently dropped the quantity when wrapping a sub-task in a compound task, causing the planner to mine a single log for a multi-output recipe.

**Configuration.** None — pure bug fix.

### Health-critical check (threshold = 6 HP)

**What it does.** A coarse health-critical predicate that goals consult before committing to risky sub-plans. Threshold is **6 HP** (3 hearts) — the same number Sprint 23's damage interrupt uses as its default.

**Configuration.** Currently hard-coded; Sprint 23 added per-goal overrides via `IGoal.DamageInterruptThresholdHp` for the interrupt path. The static threshold here will likely be unified with that property in a future sprint.

### World KB separation (`WorldKbUrl` config)

**What it does.** Introduces a second `IMemoryGateway` keyed singleton (`"world"`) so world observations can be persisted to a different MemorySmith instance from agent codebase documentation. Sprint 22 added the plumbing; Sprint 23 made it default-on by routing tools and switching the default to null.

**Configuration.** `Agent:Memory:WorldKbUrl` — see [world-kb-deployment.md](world-kb-deployment.md).

## Configuration cheatsheet

| Setting                                    | Default                | Notes                                     |
| ------------------------------------------ | ---------------------- | ----------------------------------------- |
| `Agent:Enabled`                            | `true`                 | Set false to load WebUI without the bot   |
| `Agent:Minecraft:ServerHost`               | `localhost`            | MC server hostname                        |
| `Agent:Minecraft:ServerPort`               | `25565`                | MC server port                            |
| `Agent:Minecraft:BotUsername`              | (required)             | In-game name                              |
| `Agent:Llm:Endpoint`                       | `http://localhost:11434` | Ollama or OpenAI-compatible             |
| `Agent:Llm:Model`                          | (required)             | e.g. `llama3.1:8b`                        |
| `Agent:Llm:LlmTimeoutSeconds`              | `60`                   | Per-request LLM timeout                   |
| `Agent:Llm:PlayerCooldownSeconds`          | `2`                    | Per-player rate limit cooldown            |
| `Agent:Llm:GlobalPerMinuteMax`             | `30`                   | Hard cap on LLM calls/minute              |
| `Agent:Memory:BaseUrl`                     | `http://localhost:5000`| Agent KB                                  |
| `Agent:Memory:WorldKbUrl`                  | `null`                 | World KB; null = use agent KB + warning   |
| `Agent:Memory:WorldTimeoutSeconds`         | `30`                   | World KB request timeout                  |
| `Agent:Runtime:ReplanIntervalSeconds`      | `2`                    | How often planner reconsiders             |
| `Agent:Runtime:ActionTimeoutSeconds`       | `30`                   | Single-action wall clock cap              |

## Related guides

- [running-the-agent.md](running-the-agent.md) — getting-started walkthrough
- [world-kb-deployment.md](world-kb-deployment.md) — second MemorySmith instance setup
