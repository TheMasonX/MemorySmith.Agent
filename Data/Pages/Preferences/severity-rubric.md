# 📐 Formalized Rubrics  
(Severity = Impact × Likelihood)

---

## 🧱 **Impact Rubric (I)**  
Impact measures **how bad the outcome is if the issue manifests**.  
Scale: **1–5**, logarithmic progression (each step ≈ 2–3× worse).

| Score | Label | Definition |
|------|--------|------------|
| **1 — Minimal** | Cosmetic or negligible effect. No correctness risk. No user‑visible degradation. |
| **2 — Low** | Minor correctness issues, small inefficiencies, or slight degradation in agent behavior. No data loss. |
| **3 — Moderate** | Noticeable degradation in agent autonomy, planning quality, or world model fidelity. Incorrect behavior in some scenarios. |
| **4 — High** | Major correctness failures, broken workflows, corrupted state, or significant reliability issues. |
| **5 — Critical** | Catastrophic failure: data loss, agent stuck/deadlock, world model collapse, or systemic architectural breakage. |

Impact is evaluated across:  
- **Correctness**  
- **Safety / data integrity**  
- **Agent autonomy**  
- **Planner reliability**  
- **World model fidelity**  
- **Operational continuity**

---

## 🎲 **Likelihood Rubric (L)**  
Likelihood measures **how probable it is that the issue will occur in real execution**.  
Scale: **1–5**, based on evidence and architectural exposure.

| Score | Label | Definition |
|------|--------|------------|
| **1 — Rare** | Requires highly specific conditions; no observed occurrences; low architectural exposure. |
| **2 — Uncommon** | Possible but not typical; limited exposure; no observed failures but plausible. |
| **3 — Occasional** | Happens in some scenarios; moderate exposure; intermittent symptoms or logs. |
| **4 — Frequent** | Regularly triggered in normal operation; high exposure; observed multiple times. |
| **5 — Certain** | Guaranteed or near‑guaranteed to occur; deterministic failure path; reproducible. |

Likelihood is evaluated using:  
- **Observed behavior**  
- **Telemetry**  
- **Architectural exposure**  
- **Historical defect patterns**  
- **Complexity / branching factor**  
- **Concurrency / timing sensitivity**

---

## 🔥 **Severity Calculation (S)**  
Severity is **not a label** — it is a computed value:

\[
S = I \cdot L
\]

Scale: **1–25**  
Interpretation:

| Range | Severity Class | Meaning |
|-------|----------------|---------|
| **1–4** | Low | Minor issue; low urgency. |
| **5–9** | Moderate | Should be addressed; meaningful risk. |
| **10–16** | High | Significant issue; prioritize soon. |
| **17–25** | Critical | Top‑priority fix; high risk and high impact. |

Severity is always accompanied by:  
- **Confidence %**  
- **Evidence citations**  
- **Classification** (Verified Issue / Probable Issue / Architectural Concern / Open Question)

---

## 🧭 **Confidence Scale**  
Confidence expresses **how certain the finding is**, based on evidence.

| Confidence | Meaning |
|------------|---------|
| **95–100%** | Verified with direct evidence (code, logs, reproduction). |
| **80–94%** | Strong evidence; highly probable. |
| **60–79%** | Probable; indirect evidence or partial reproduction. |
| **40–59%** | Plausible but unconfirmed; architectural reasoning. |
| **<40%** | Low confidence; requires investigation. |

---

## 📎 **Evidence Citation Style**  
Every finding must cite **specific, traceable sources**:

- File paths  
- Line numbers  
- Commit hashes  
- Telemetry logs  
- Reproduction steps  
- Observed agent behavior  
- Planning traces  
- World state snapshots  

Format example:

> Evidence: `MemorySmith.Agent/Planning/HTN/TaskResolver.cs:142–188` (commit `81f267b4`) — observed null dereference path when `worldState.currentTarget == null`.

---

## 🧩 **Classification Rules**  
Every finding must be labeled as one of:

- **Verified Issue** — reproducible or directly observable.  
- **Probable Issue** — strong evidence but not fully reproduced.  
- **Architectural Concern** — structural risk, design flaw, or long‑term maintainability issue.  
- **Open Question** — requires clarification, missing documentation, or ambiguous behavior.
