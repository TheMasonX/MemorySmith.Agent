# MemorySmith.Agent Delta Audit — New Findings Only

**Target:** `TheMasonX/MemorySmith.Agent`  
**Branch / commit context:** `dev/round-3`, commit `57b8fdf79ff7ba6f877e0fcac2211934e0f3279c`  
**Scope note:** This report contains only findings that were not called out in the prior audit.

## New findings

### 1) `WorldStateDiff` now claims to detect unexpected inventory changes, but the new check is unreachable when no inventory expectation exists
**Severity:** High  
**Confidence:** 96%

`HasInventoryMismatch` immediately returns `false` when both `InventoryGained` and `InventoryLost` are `null`. That short-circuits the new “unexpected inventory changes” loop that was added later in the method, so actions with no expected inventory changes can still silently gain or lose items without being flagged. The commit message says this change was meant to detect unexpected inventory changes, but the current control flow prevents that for the exact cases where it matters most. fileciteturn56file0

**Why it matters:** movement, chat, query, and other non-inventory actions can still affect inventory through side effects or adapter bugs. Those anomalies will be missed.

**Fix direction:** treat `ActualInventoryDelta` as authoritative even when expected gains/losses are null, and flag any non-zero delta as a mismatch unless the caller explicitly marks the action as inventory-neutral.

### 2) `WorldStateDiff.DescribeMismatches()` does not describe the new “unexpected inventory change” case
**Severity:** Medium  
**Confidence:** 89%

Even when `HasInventoryMismatch` becomes `true` due to the new unexpected-change logic, `DescribeMismatches()` only builds inventory text from expected gains shortfalls. It never emits a message for “unexpected gain/loss with no expected item,” so the evaluator can receive a mismatch flag with an empty or misleading explanation. fileciteturn56file0

**Why it matters:** the observe→compare→evaluate loop gets weaker because the LLM sees that something is wrong but not what changed.

**Fix direction:** add a second branch that enumerates unexpected delta keys and reports them explicitly, even when there was no planned inventory change.

### 3) Creative provisioning now uses a linked cancellation token source, but the linked CTS is never disposed
**Severity:** Medium  
**Confidence:** 83%

`SetGoal()` now creates a linked CTS with `CancellationTokenSource.CreateLinkedTokenSource(_goalProvisioningCts.Token, _connectionCts.Token)` when `_connectionCts` is present. That linked source is only used to pass a token into `ProvisionGoalIfCreativeAsync` and is never disposed. Because this runs on each creative goal set, it can leak cancellation registrations / handles over time. fileciteturn61file0

**Why it matters:** this is a slow-burn resource leak in the hot goal path, and creative provisioning is one of the more frequently exercised workflows.

**Fix direction:** wrap the linked CTS in a `using`/`try-finally`, or refactor provisioning so the task owns disposal after scheduling.

### 4) The chat command filter is trivially bypassed by leading whitespace
**Severity:** High  
**Confidence:** 95%

`HandleChatEventAsync()` skips chat messages only when `chat.Message.StartsWith('/')`. That misses messages like `" /give ..."` or other prefixed variants, which means a server command can bypass the intended “skip intent parsing” path simply by adding leading whitespace. The message would then flow into the normal intent pipeline instead of being treated as a server command. fileciteturn71file0

**Why it matters:** this is a direct command-filter bypass. It weakens the safety boundary around Minecraft server commands and can feed command-like text into the agent path.

**Fix direction:** normalize leading whitespace before classification, and consider rejecting any message whose trimmed form starts with `/` before it reaches intent parsing or the LLM path.

## Net effect

The new audit surface is smaller than the previous one, but these four issues are high leverage:
- one correctness regression in the new world-diff logic,
- one diagnostics gap in the same area,
- one small but real resource leak in creative provisioning,
- one safety bypass in chat handling.

If you only fix two things first, make them the chat-command bypass and the world-diff early return.
