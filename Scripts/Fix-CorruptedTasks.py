#!/usr/bin/env python3
"""
Reconstruct severely corrupted JSON task files.
These files have unescaped quotes, literal newlines in strings,
and missing closing quotes in the description field.
"""
import json, os, re

TASK_DIR = r"D:\@Repos\MemorySmith.Agent\Data\Tasks"

def fix_tsk_0107(path):
    """tsk-0107 missing 'comments': [ header. Already almost valid."""
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    content = content.replace('"linkedPages": [],\n  \n    {', '"linkedPages": [],\n  "comments": [\n    {', 1)
    # Try to parse
    try:
        obj = json.loads(content)
        formatted = json.dumps(obj, indent=2, ensure_ascii=False)
        with open(path, 'w', encoding='utf-8') as f:
            f.write(formatted)
        return True
    except json.JSONDecodeError:
        # If it still fails, use the generic fixer
        return fix_generic(path)

def fix_generic(path):
    """Generic approach: extract fields, fix description, rebuild."""
    with open(path, 'r', encoding='utf-8') as f:
        raw = f.read()
    
    # Extract the known prefix fields
    id_match = re.search(r'"id"\s*:\s*"([^"\\]*(?:\\.[^"\\]*)*)"', raw)
    key_match = re.search(r'"key"\s*:\s*"([^"\\]*(?:\\.[^"\\]*)*)"', raw)
    title_match = re.search(r'"title"\s*:\s*"([^"\\]*(?:\\.[^"\\]*)*)"', raw)
    
    if not all([id_match, key_match, title_match]):
        print(f"  Cannot extract basic fields from {path}")
        return False
    
    task_id = id_match.group(1)
    task_key = key_match.group(1)
    task_title = title_match.group(1)
    
    # Extract description - everything between "description": " and ,\n  "type"/
    desc_match = re.search(r'"description"\s*:\s*"(.+?)",\s*\n\s*"(?:type|status|priority)"', raw, re.DOTALL)
    if desc_match:
        raw_desc = desc_match.group(1)
    else:
        # Fallback: take everything after "description": " and find the latest sensible end
        start_idx = raw.find('"description": "')
        if start_idx < 0:
            print(f"  Cannot find description in {path}")
            return False
        desc_start = start_idx + len('"description": "')
        # Look for ,\n followed by a known field
        for m in re.finditer(r',\s*\n\s*"(?:type|status|priority|assigneeMode)"', raw[desc_start:], re.DOTALL):
            end_idx = desc_start + m.start()
            raw_desc = raw[desc_start:end_idx]
            break
        else:
            raw_desc = raw[desc_start:]
    
    # Escape the description properly
    def escape_json(s):
        result = []
        for ch in s.rstrip():
            if ord(ch) < 0x20:
                if ch == '\n': result.append('\\n')
                elif ch == '\r': pass
                elif ch == '\t': result.append('\\t')
                else: pass
            elif ch == '"': result.append('\\"')
            elif ch == '\\': result.append('\\\\')
            else: result.append(ch)
        return ''.join(result)
    
    desc_escaped = escape_json(raw_desc)
    
    # Extract remaining fields from the tail of the file
    tail = ""
    type_match = re.search(r'"type"\s*:\s*"([^"]*)"', raw)
    status_match = re.search(r'"status"\s*:\s*"([^"]*)"', raw)
    priority_match = re.search(r'"priority"\s*:\s*"([^"]*)"', raw)
    
    task_type = type_match.group(1) if type_match else "Task"
    task_status = status_match.group(1) if status_match else "Backlog"
    task_priority = priority_match.group(1) if priority_match else "Medium"
    
    # Extract remaining fields from the JSON tail
    # Find everything after the description field's closing comma
    after_desc = raw[raw.find('"description"'):]
    desc_close = after_desc.find(',')
    if desc_close >= 0:
        remaining = after_desc[desc_close+1:]
    else:
        remaining = ""
    
    # Build the fixed JSON
    task = {
        "id": task_id,
        "key": task_key,
        "title": task_title,
        "description": desc_escaped,
    }
    
    # Add remaining fields from tail
    for field_match in re.finditer(r'"(\w+)"\s*:\s*(null|true|false|"[^"]*"|(?:\[[^\]]*\])|(?:\{[^\}]*\})|\d+)', remaining):
        fname = field_match.group(1)
        fvalue = field_match.group(2)
        if fname.lower() in ('description', 'id', 'key', 'title'):
            continue
        try:
            task[fname] = json.loads(fvalue)
        except:
            task[fname] = fvalue.strip('"')
    
    # Ensure critical fields exist
    task.setdefault('type', task_type)
    task.setdefault('status', task_status)
    task.setdefault('priority', task_priority)
    
    formatted = json.dumps(task, indent=2, ensure_ascii=False)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(formatted)
    return True


files = [
    "tsk-0107-fix-build-origin-sentinel-eliminate-0-0-0-as-missing-origin-signal.json",
    "tsk-0132-fix-page-search-score-0-0-under-ranking.json",
    "tsk-0133-fix-parameter-preservation-on-replan-remaining-count.json",
    "tsk-0134-add-di-startup-failure-logging-and-health-check-endpoints.json",
    "tsk-0137-fix-consecutive-failure-guard-reset-on-partial-progress.json",
]

fixed = 0
for fname in files:
    path = os.path.join(TASK_DIR, fname)
    if not os.path.exists(path):
        print(f"MISSING: {fname}"); continue
    print(f"Processing: {fname} ...", end=" ")
    
    if "tsk-0107" in fname:
        ok = fix_tsk_0107(path)
    else:
        ok = fix_generic(path)
    
    if ok:
        try:
            with open(path) as f:
                json.load(f)
            print("OK")
            fixed += 1
        except json.JSONDecodeError as e:
            print(f"VERIFY FAILED: {e}")
            # Show the file for debugging
            with open(path) as f:
                content = f.read()
            print(f"  First 500: {content[:500]}")
    else:
        print("FAILED")

print(f"\nFixed: {fixed}/{len(files)}")
if fixed != len(files): exit(1)
