# MemorySmith.Agent Additional Delta Audit — CI Script / Adapter Helper Slice

Repository: TheMasonX/MemorySmith.Agent  
Branch: dev/round-3  
Commit reviewed: 0f27af1befb72e7534421a1bd5550aee1e077d96

Scope:
- Delta findings only
- Focus on under-covered CI scripts and adapter helper modules
- Maintainability, brittle assumptions, duplication, and task corrections

Overall confidence: 90%

---

# Executive Summary

This slice found a new cluster of problems outside the host/runtime core:

- CI scripts are making assumptions about repository root that are likely wrong unless manually overridden.
- Dependency inventory logic is incomplete because it only sees package references in `.csproj` files.
- Adapter helper modules still encode shared concepts in conflicting ways.
- Some best-effort fallbacks are operationally quiet enough to hide capability loss.
- A small amount of module-scoped state is still not tied to bot lifetime.

The main maintenance risk here is that the supporting scripts and helpers look simple, but their contracts are actually quite fragile.

---

# New Findings

## 127 — `Verify-AboutDeps.ps1` defaults the repository root to the Scripts directory

Severity: High

Confidence: 98%

### Evidence

The script sets:

```powershell
param(
    [string]$RepoRoot = $PSScriptRoot,
```

and then resolves paths and scans from that root. fileciteturn142file0

`Invoke-PackageVetting.ps1` calls the script without passing `RepoRoot`:

```powershell
& $aboutScript -Quiet:$Quiet
```

fileciteturn141file0

### Problem

`$PSScriptRoot` inside `Verify-AboutDeps.ps1` is the `Scripts` directory, not the repository root.

That means the default path resolution is wrong unless the caller overrides it manually.

This affects both:

- the `.csproj` scan root
- the `about.html` lookup path

### Recommendation

Default `RepoRoot` to the parent of `Scripts`, or require the caller to pass it explicitly.

For example:

```powershell
[string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
```

### Task impact

This is a correction to `TSK-0145` and also affects `TSK-0144`, because the package vetting wrapper currently invokes the About-page checker without overriding the root. fileciteturn141file0turn142file0

---

## 128 — The About-page inventory misses centrally managed package references

Severity: High

Confidence: 94%

### Evidence

`Verify-AboutDeps.ps1` only extracts `PackageReference` entries from `.csproj` files:

```powershell
$pkgRefs = $csprojXml.Project.ItemGroup.PackageReference
```

and builds its package set only from that source. fileciteturn142file0

### Problem

That model misses packages declared elsewhere, such as:

- centrally managed version files
- shared props/targets-based references
- nonstandard package injection patterns

Even if this repo currently keeps most references in `.csproj`, the script’s contract is narrower than the policy claims.

### Recommendation

Either:

- explicitly document the script as `.csproj`-only, or
- expand the scan to include centralized package management inputs

### Task impact

This is an extension of `TSK-0145`, not a separate task.

---

## 129 — `Invoke-PackageVetting.ps1` relies on English CLI strings and loose fallback detection

Severity: Medium

Confidence: 92%

### Evidence

The script interprets `dotnet list package` results by matching English text:

- `"No vulnerable packages found"`
- `"has known vulnerable"`
- `"No deprecated packages found"`
- `"is deprecated"`

and otherwise falls back to project-level scans. fileciteturn141file0

### Problem

This is brittle in two ways:

1. It depends on the exact phrasing of `dotnet` output.
2. It assumes “unknown solution output” means “try projects instead,” which may hide unsupported states rather than reporting them clearly.

### Recommendation

Prefer a structured or exit-code-based contract if available, and treat unsupported/ambiguous solution output as a distinct failure mode.

### Task impact

This should be folded into `TSK-0144`.

---

## 130 — `logger.cjs` writes synchronously and has no retention policy

Severity: Medium

Confidence: 93%

### Evidence

`logger.cjs` uses `appendFileSync` on every log write. It also creates the log directory best-effort, but does not implement any log rotation or retention policy. fileciteturn130file0

### Problem

This creates two maintainability/operations issues:

- synchronous file appends can block the Node event loop under log-heavy conditions
- logs can grow without any cleanup or retention cap

The C# host already uses a retained log sink policy; the Node adapter does not.

### Recommendation

Use asynchronous logging or a buffered writer, and add retention / rotation behavior that mirrors the host’s operational policy.

### Task impact

This is not directly covered by current tasks and should become a small adapter infrastructure cleanup task.

---

## 131 — Creative provisioning uses a raw numeric game-mode check instead of the shared normalizer

Severity: High

Confidence: 91%

### Evidence

`creativeProvider.cjs` gates creative provisioning with:

```js
if (bot.game.gameMode !== 1) return false;
```

fileciteturn131file0

Meanwhile, `gameModeState.js` already normalizes Mineflayer numeric and string forms into canonical labels like `creative` and `survival`. fileciteturn108file0

### Problem

The adapter has two competing game-mode contracts:

- a raw numeric check in creative provisioning
- a normalized game-mode helper elsewhere

That is duplicated semantics, and it can break if Mineflayer changes representation or if `bot.game.gameMode` is not always numeric at the point of use.

### Recommendation

Use the shared normalizer everywhere, or wrap the concept in one helper such as `isCreativeMode(bot)`.

### Task impact

Best treated as an extension of `TSK-0410`.

---

## 132 — Creative slot rotation state is module-scoped instead of bot-scoped

Severity: Medium

Confidence: 90%

### Evidence

`creativeProvider.cjs` tracks `_nextSlotIndex` as a module-level variable. It increments after each successful provisioning and is never reset on reconnect or bot recreation. fileciteturn131file0

### Problem

That means slot rotation state survives across bot lifetimes.

If the adapter reconnects or a new bot instance is created, the provisioning sequence may start from an arbitrary slot index left over from the previous session.

### Recommendation

Scope slot rotation to the bot lifecycle, or reset the index on reconnect / reinitialization.

### Task impact

This is a small adapter lifecycle cleanup item related to `TSK-0410`, but not identical to the existing task description.

---

## 133 — `creativeProvider.cjs` and `logger.cjs` both use best-effort failure suppression, but only one surface is observable

Severity: Medium

Confidence: 88%

### Evidence

`creativeProvider.cjs` catches slot failures and continues to fallback logic. It logs some failures, but it still treats provisioning as best-effort. fileciteturn131file0

`logger.cjs` swallows filesystem errors entirely with empty catches. fileciteturn130file0

### Problem

These are both legitimate best-effort behaviors in isolation, but together they make adapter health harder to judge:

- provisioning may fail without a clear capability degradation signal
- logging can silently fail even when the bot is otherwise healthy

### Recommendation

Keep best-effort behavior only for truly optional paths, and surface structured health signals for capability loss or persistent logging failure.

### Task impact

`TSK-0410` should explicitly cover fallback observability, not just error-catching mechanics.

---

# Task Corrections / Extensions

## TSK-0145 — Correct the repository root assumption
- Default the repo root to the actual repository root, not the Scripts directory.
- Pass `RepoRoot` explicitly from `Invoke-PackageVetting.ps1`.

## TSK-0145 — Expand package discovery scope
- Decide whether the “living inventory” should include centralized package management inputs.
- If not, document the limitation clearly.

## TSK-0144 — Harden CLI result handling
- Treat ambiguous `dotnet list package` output as a distinct state.
- Reduce dependence on exact English output strings.

## TSK-0410 — Extend to cover adapter lifecycle state
- Reset creative slot rotation on reconnect.
- Use a canonical creative-mode helper instead of a raw numeric gate.
- Add observable fallback health for logging / provisioning.

---

# Recommended Next Step

The next slice should look at the remaining helper and boundary modules for the same two classes of problems:

- module-scoped state that should be bot-scoped
- data/inventory scripts that claim broader coverage than they actually implement

---

# Final Assessment

The supporting scripts and adapter helpers are now a meaningful source of technical debt in their own right.

The most urgent fix here is the repository-root bug in `Verify-AboutDeps.ps1`, because it undermines the CI policy that depends on it.

Overall confidence: 90%
