# Agent Configuration Reference

> **Source:** `WebUI.Blazor/appsettings.json` + `IOptions<T>` binding in `Program.cs`  
> **Last updated:** 2026-07-11  
> **Options classes:** `MinecraftAdapterConfig`, `ChatOptions`, `SafetyOptions`, `RestMemoryGatewayOptions`

Configuration for `MemorySmith.Agent` is rooted under the `Agent` section in `appsettings.json`. Environment variables can override individual keys via the standard `__` separator (e.g. `Agent__Minecraft__ServerPort=25565`).

---

## Top-level

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Agent:Enabled` | `bool` | `true` | Enables the agent background service. When `false`, the agent loop does not start (useful for running the Blazor dashboard standalone). |
| `Agent:Logging:Default` | `string` | `"Debug"` | Default Serilog minimum level for agent-scoped loggers. Overrides the root `Logging:LogLevel:Default` for the `Agent.*` namespace. |
| `Agent:Logging:Overrides` | `Dictionary` | — | Per-logger level overrides, e.g. `WebUI.Blazor.AgentBackgroundService: Debug`. |

---

## Minecraft adapter — `Agent:Minecraft`

Bound to `MinecraftAdapterConfig` (`Agent.World.Minecraft`).

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Agent:Minecraft:ServerHost` | `string` | `"localhost"` | Minecraft server hostname for the Node.js bot. |
| `Agent:Minecraft:ServerPort` | `int` | `25565` | Minecraft server port. |
| `Agent:Minecraft:BotUsername` | `string` | `"AgentBot"` | In-game bot username. |
| `Agent:Minecraft:AutoStartNode` | `bool` | `false` | If `true`, spawns the Node.js process automatically on `ConnectAsync`. |
| `Agent:Minecraft:NodeScriptPath` | `string` | `""` | Path to `MineflayerAdapter/index.js`. Auto-start is skipped when empty. |
| `Agent:Minecraft:NodeStartTimeoutMs` | `int` | `10000` | Milliseconds to wait for the Node WebSocket server before timing out. |
| `Agent:Minecraft:WebSocketUrl` | `string` | `"ws://localhost:3000"` | WebSocket URL the C# adapter connects to (matches Node.js WS server port). |
| `Agent:Minecraft:WebSocketPort` | `int` | `3000` | Port the Node.js WebSocket server listens on. |
| `Agent:Minecraft:AdapterSecret` | `string?` | `null` | Shared secret for WebSocket handshake auth. Set via `Agent__Minecraft__AdapterSecret` env var. Never commit a real value. |

---

## Chat/LLM — `Agent:Chat`

Bound to `ChatOptions` (`Agent.Planning.Llm`).

### LLM provider

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Agent:Chat:LlmEnabled` | `bool` | `false` | Enable LLM-powered chat interpretation. `false` = pattern-matching only. |
| `Agent:Chat:LlmProvider` | `string` | `"ollama"` | Provider identifier: `ollama`, `openai`, `openrouter`, `deepseek`, `github-copilot`, `anthropic`, `gemini` |
| `Agent:Chat:LlmModel` | `string` | `"llama3.2"` | Model name passed to the provider. |
| `Agent:Chat:LlmBaseUrl` | `string` | `""` | Provider base URL. When empty, uses the provider's standard endpoint. |
| `Agent:Chat:LlmApiKey` | `string?` | `null` | API key for cloud providers. Read from `MSA_LLM_API_KEY` env var when set. |
| `Agent:Chat:LlmTimeoutSeconds` | `int` | `10` | Per-request LLM timeout in seconds. |
| `Agent:Chat:LlmMaxResponseTokens` | `int` | `300` | Max tokens the LLM may generate per response. `0` = model default. |

### Confidence & rate limiting

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Agent:Chat:LlmConfidenceThreshold` | `double` | `0.6` | Minimum confidence (0.0–1.0) before acting on LLM intent. |
| `Agent:Chat:PlayerCooldownSeconds` | `int` | `3` | Minimum seconds between LLM calls for the same player. |
| `Agent:Chat:GlobalPerMinuteMax` | `int` | `5` | Max LLM calls across all players per minute. |

### Chat behaviour

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Agent:Chat:MaxMessageLength` | `int` | `1024` | Max in-game message length accepted for interpretation. |
| `Agent:Chat:ChatMaxResponseLength` | `int` | `500` | Max characters for bot chat responses. Longer responses are split at sentence boundaries. |
| `Agent:Chat:MaxResponseDistanceBlocks` | `double` | `64.0` | Distance (blocks) beyond which the bot ignores non-directed messages. |
| `Agent:Chat:CommandExecutionEnabled` | `bool` | `false` | Allow the agent to dispatch Minecraft server commands via chat. Safe-by-default. |
| `Agent:Chat:DeniedCommands` | `string[]?` | `null` | Commands to filter out of the LLM's KNOWN COMMANDS prompt section. |
| `Agent:Chat:ConversationWindowSeconds` | `int` | `60` | Seconds after bot last spoke during which any message is treated as conversation. |
| `Agent:Chat:ChatHistoryMaxTurns` | `int` | `30` | Max turns retained in conversation history buffer. |

### Chat logging

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Agent:Chat:Logging:Enabled` | `bool` | `false` | Enable chat transcript logging. |

---

## MemorySmith API — `Agent:Memory`

Bound to `RestMemoryGatewayOptions` (`Agent.Memory`).

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Agent:Memory:BaseUrl` | `string` | `"http://localhost:5000"` | Base URL of the MemorySmith API instance. |
| `Agent:Memory:ApiKey` | `string?` | `null` | Optional API key sent as `X-Api-Key` header. Read from `MEMORYSMITH_API_KEY` env var when set. |
| `Agent:Memory:TimeoutSeconds` | `int` | `30` | HTTP request timeout in seconds. |
| `Agent:Memory:DefaultPageRole` | `string` | `"Anonymous"` | Default minimum role for pages created by the agent. |
| `Agent:Memory:ItemCacheTtlSeconds` | `int` | `60` | TTL for the item-registry in-memory cache (seconds). |
| `Agent:Memory:NullCacheTtlSeconds` | `int` | `5` | Separate TTL for not-found cache entries (seconds). |

---

## Build — `Agent:Build`

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Agent:Build:MaxConcurrentPlaceBlock` | `int` | `8` | Max concurrent PlaceBlock dispatches per cycle. |

---

## Safety — `Agent:Safety`

Bound to `SafetyOptions` (`WebUI.Blazor.Options`).

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Agent:Safety:AllowDestructiveCommands` | `bool` | `false` | Allow the LLM to dispatch commands in the DeniedCommands set (e.g. `/op`, `/kill`, `/gamemode`). |
| `Agent:Safety:DeniedCommands` | `string[]` | *(built-in list)* | Hard safety layer — commands the LLM must never dispatch. Merged with `AgentBackgroundService.DefaultDeniedCommands`. |

---

## Environment variables

| Variable | Overrides | Description |
|----------|-----------|-------------|
| `MEMORYSMITH_API_KEY` | `Agent:Memory:ApiKey` | API key for the MemorySmith REST API. |
| `MSA_LLM_API_KEY` | `Agent:Chat:LlmApiKey` | API key for cloud LLM providers. |
| `Agent__Minecraft__AdapterSecret` | `Agent:Minecraft:AdapterSecret` | WebSocket handshake shared secret. |

---

## Agent notes

- **`CommandExecutionEnabled`** was changed from `true` to `false` in Sprint 58 (TSK-0319) as a safe-by-default default. Set it to `true` in `appsettings.json` to restore command execution.
- **DeniedCommands** in config are *merged* (union) with the built-in `AgentBackgroundService.DefaultDeniedCommands` — config can add to the deny list but never remove from it (TSK-0324).
- **Environment variables** take precedence over `appsettings.json` values via the standard .NET configuration layering.
