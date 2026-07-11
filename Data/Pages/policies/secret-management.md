# Secret Management Policy — MemorySmith.Agent

**Adopted:** Sprint 60 (2026-07-11)
**Scope:** All secrets, credentials, API keys, tokens, and connection strings used by MemorySmith.Agent.

## Policy

### S-1: No secrets in source code

Secrets **must not** be hardcoded in source files, configuration files committed to the repository, or any file tracked by git.

**Allowed mechanisms:**
- Environment variables (`MEMORYSMITH_API_KEY`, `MSA_LLM_API_KEY`, etc.)
- User Secrets (`dotnet user-secrets` for local development)
- GitHub Actions Secrets for CI/CD
- Azure Key Vault or similar vault services (production)

### S-2: Pre-commit secret scanning

All commits **must** pass the pre-commit secret scan (`Scripts/Invoke-SecretScan.ps1`) before being committed. The pre-commit hook is installed at `.git/hooks/pre-commit`.

If the pre-commit hook blocks a false positive:
1. Verify it's genuinely not a secret
2. Add the pattern to `$falsePositivePatterns` in `Scripts/Invoke-SecretScan.ps1`
3. Commit the updated script alongside the change

### S-3: CI secret scanning

Every CI run includes a full-repo secret scan (`Scripts/Invoke-SecretScan.ps1 -ScanAll`). The scan is currently **report-only** (`continue-on-error: true`) until the baseline is clean.

### S-4: Secret rotation

If a secret is exposed (committed to the repo, shared in logs, or otherwise compromised):
1. **Immediately** revoke the compromised secret
2. Generate a new secret
3. Update all configurations (env vars, secrets stores, CI secrets)
4. Record the rotation in the audit log
5. If the secret was committed to git history, purge it using `git filter-repo` (see [Secret Rotation Procedures](#secret-rotation-procedures))

---

## Secret Inventory

| Secret | Config Key | Env Variable | Used By | Rotation Frequency |
|--------|-----------|--------------|---------|--------------------|
| MemorySmith API Key | `Agent:Memory:ApiKey` | `MEMORYSMITH_API_KEY` | RestMemoryGateway | On exposure or quarterly |
| LLM API Key | `Agent:Chat:LlmApiKey` | `MSA_LLM_API_KEY` | LlmProvider (cloud) | On exposure or quarterly |
| Adapter Secret | `Agent:Minecraft:AdapterSecret` | `WS_TOKEN` (Node.js) | WebSocket handshake | On exposure or quarterly |
| Agent API Key | `Agent:ApiKey` | `Agent__ApiKey` | ApiKeyMiddleware | On exposure or quarterly |

---

## Secret Rotation Procedures

### Prerequisites
- Admin access to the secret source (GitHub, LLM provider, etc.)
- Access to deployment environments (dev/staging/production)

### Step-by-Step: Rotating the MemorySmith API Key

1. **Generate a new key** in the MemorySmith API server admin panel.
2. **Update the local development environment:**
   ```powershell
   $env:MEMORYSMITH_API_KEY = "new-key-value"
   ```
3. **Update CI/CD secrets:**
   - In GitHub, navigate to Settings → Secrets and variables → Actions
   - Update `MEMORYSMITH_API_KEY` with the new value.
4. **Verify connectivity:**
   ```powershell
   # Start the agent and check startup logs for successful connection
   dotnet run --project WebUI.Blazor --launch-profile http
   # Look for: "memory=<url>" in the startup banner
   ```
5. **Record the rotation** in the task tracking system:
   - Create a comment on a maintenance task noting the date and scope.

### Step-by-Step: Purging Secrets from Git History

If a secret was accidentally committed:

1. **Revoke the secret immediately** (mandatory — do this before history rewrite).
2. **Install git filter-repo:**
   ```powershell
   pip install git-filter-repo
   ```
3. **Create a `replace-text.txt` file** mapping the exposed secret to a placeholder:
   ```
   exposed-secret-value==REDACTED
   ```
4. **Run git filter-repo:**
   ```powershell
   git filter-repo --replace-text replace-text.txt --force
   ```
5. **Force-push the cleaned history:**
   ```powershell
   git push origin --force --all
   ```
6. **Notify all collaborators** to rebase their local branches on the new history.

### Step-by-Step: Git History Scan (Verify Clean)

To check if any secrets exist in the full git history:

```powershell
pwsh ./Scripts/Invoke-SecretScan.ps1 -ScanAll
```

For a deeper scan that checks all commits (not just HEAD):

```powershell
# Requires gitleaks or trufflehog installed
# Example with gitleaks:
gitleaks detect --source . --report-path artifacts/gitleaks-report.json
```

---

## Enforcement

| Gate | Tool | Action |
|------|------|--------|
| Pre-commit | `Scripts/Invoke-SecretScan.ps1` | Blocks commit if secrets found |
| CI push | `.github/workflows/ci.yml` → Secret scan step | Report-only (S-3) |
| Quarterly audit | Manual `Invoke-SecretScan.ps1 -ScanAll` | Scheduled review |

---

## Exception Log

| Date | Exception | Rationale | Approved By |
|------|-----------|-----------|-------------|
| | | | |
