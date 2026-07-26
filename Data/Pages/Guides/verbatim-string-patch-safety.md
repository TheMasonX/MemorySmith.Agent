## Verbatim String Patch Safety (Rule E-1)

Summary
- C# files that contain verbatim string literals (`@"..."`) or raw string literals (`"""..."""`) are fragile when edited via automated text-manipulation tools. Edits may corrupt escape sequences, backslashes, or braces and lead to compile failures.

Problem
- Patching these files incorrectly can introduce unrecognized escape sequences, broken JSON in embedded strings, or mismatched quotes that cause the build to fail and are time-consuming to debug.

Safe Patch Recipe
1. Fetch the current blob SHA and download the full file contents using the GitHub file API.
2. Edit the file locally in a safe editor (or in a sandbox copy) — do not attempt in-place regex replacements that touch verbatim blocks.
3. Create a params file containing the full file contents and metadata (owner, repo, path, message, sha, branch).
4. Use `github__create_or_update_file` with the `paramsFile` approach so the MCP/GitHub client performs the base64 encoding and commit atomically.

Why this helps
- Editing the full file and committing as a single operation avoids partial or double-encoding of quoted content and preserves escaping used inside verbatim/raw strings.

When a small change *must* be made
- Prefer non-invasive changes: add logging calls outside of verbatim blocks, extract constants, or add new helper methods rather than changing inside long verbatim strings.

Example (safe):
- Replace a naked `catch { /* best-effort */ }` with a logged warning outside verbatim strings:

```csharp
try { _ws.Dispose(); }
catch (Exception ex)
{
    logger?.LogWarning(ex, "disposing old socket failed: {Message}", ex.Message);
}
```

Contacts
- For assistance or to review a risky change, tag `@SteveBot` and include the file path and the exact change intended.

Related tasks
- See `TSK-0434` for creating this note and `TSK-0425`/`TSK-0426` for logging fixes and audits.
