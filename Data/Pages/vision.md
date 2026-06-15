# Vision Subsystem

Vision is divided into three layers, each with a different LLM dependency level:

## 1. World Vision (Deterministic)

Reads Mineflayer's live state — blocks, biomes, entities. No LLM.

APIs exposed:
- `GetBlockAt(x, y, z)` → block type string
- `GetNearbyEntities()` → entity list with positions

Implemented in `Agent.Vision.WorldVision`. Updated on every `WorldEvent` from the adapter.

## 2. Spatial Analysis (Algorithmic)

Computes environmental metrics by sampling terrain. No LLM.

`ISpatialAnalyzer` produces a `SpatialAnalysis` record:

```csharp
record SpatialAnalysis(
    double FlatnessRatio,    // 0–1: how flat the ground is (good for building)
    double TreeCoverage,     // fraction of nearby blocks that are logs/leaves
    double WaterProximity,   // distance to nearest water source
    string[] Tags            // e.g. ["flat", "forest", "near-water"]
);
```

Tags are injected into the context pack when the planner evaluates building sites.

Example fact stored in MemorySmith:
> "Build site at (120, 64, -300): flatness:0.85, near-water, forest edge"

## 3. Aesthetic / Multimodal Vision (LLM)

Takes a screenshot of the agent's current build and feeds it to a vision-capable model for critique.

```csharp
public interface IVisionModel
{
    Task<string> CritiqueAsync(byte[] screenshotBytes, string stylePrompt, ...);
}
```

Example critique output:
> "Image shows small windows and uneven towers. Recommend widening arch spans and equalizing tower heights."

This feedback is translated into actionable plan refinements (e.g. `PlaceBlock` corrections or replanning the roof phase).

**Confidence: Moderate (0.7)** — vision adds real aesthetic value but is the most speculative subsystem. Phase 4.

## Providers

| Provider | Vision-Capable | Notes |
|---|---|---|
| Ollama (local) | Yes (Qwen-VL, Gemma) | Free, offline, requires GPU |
| OpenAI Vision | Yes (GPT-4o) | Cloud, $$$, most capable |
| Mock | Yes (canned) | Testing only |

## Implementation Status

| Component | Status |
|---|---|
| `IVisionModel` interface | ✅ Defined |
| `ISpatialAnalyzer` interface | ✅ Defined |
| `WorldVision` stub | ✅ Defined |
| Spatial algorithm implementations | ⬜ Phase 4 |
| Multimodal model client | ⬜ Phase 4 |
| `TakeScreenshot` tool | ⬜ Phase 4 |
