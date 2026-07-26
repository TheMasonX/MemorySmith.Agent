# Safe C# Patching

## Verbatim and raw strings

Files containing C# verbatim strings (`@"..."`) or raw strings (`"""..."""`) need extra care when edited by automation. Escapes, quotes, braces, and backslashes can be transformed by an intermediary and leave the file syntactically invalid.

## Safe recipe

1. Read the complete file and identify every verbatim or raw string before editing.
2. Make the smallest possible change with a patch that preserves the string contents exactly.
3. Build the directly affected project immediately after the edit.
4. Run the focused test, then inspect the diff for unintended escape or formatting changes.

Avoid broad regular-expression replacements inside string literals. Prefer changing code outside the literal, or replace the complete file from a verified plain-text source when a string edit is unavoidable.