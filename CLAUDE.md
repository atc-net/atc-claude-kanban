# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A .NET 10 dotnet tool that serves a real-time Kanban dashboard for monitoring Claude Code agent tasks, sessions, and subagents. It watches `~/.claude/` directories, broadcasting changes via Server-Sent Events to a browser-based board.

## Build & Test

```powershell
# Build (Release mode enforces all analyzer rules as errors)
dotnet build -c Release

# Run tests (113 tests across 11 test classes)
dotnet test

# Run the dashboard locally
dotnet run --project src/Atc.Claude.Kanban -- --open

# Custom port
dotnet run --project src/Atc.Claude.Kanban -- --port 8080

# Point at a synthetic fixture instead of ~/.claude (used to regenerate README
# screenshots without real session data — see scripts/seed-demo-fixture.ps1)
dotnet run --project src/Atc.Claude.Kanban -- --dir <path>
```

## Repository Structure

```
src/Atc.Claude.Kanban/
  Program.cs                    # Entry point, CLI args, auto-port discovery, WebApplication wiring
  CliOptions.cs                 # Parsed CLI argument record
  EndpointDefinitions/          # Atc.Rest.MinimalApi IEndpointDefinition implementations
    SessionEndpointDefinition   # /api/sessions, /api/sessions/{id}, /api/sessions/{id}/agents, /api/sessions/{id}/messages,
                                #   /api/sessions/{id}/messages/{uuid}/image/{blockIndex}, /api/sessions/{id}/tool-stats, /api/sessions/{id}/usage
    TaskEndpointDefinition      # /api/tasks/all, PUT/DELETE/POST task operations
    TeamEndpointDefinition      # /api/teams/{name}
    ProjectEndpointDefinition   # /api/projects
    PlanEndpointDefinition      # /api/plans/{slug}, /api/plans/{slug}/open
    SubagentEndpointDefinition  # /api/sessions/{id}/subagents, /api/sessions/{id}/subagents/{agentId}/messages
    SseEndpointDefinition       # /api/events (SSE), /api/version, /api/cache/clear
    UtilityEndpointDefinition   # /api/open-folder, /api/open-in-editor
  Contracts/
    Models/                     # ClaudeTask, SessionInfo, TeamConfig, SubagentInfo, MessageEntry, SessionTokenUsage,
                                #   AnswerPayload/AnswerQuestion/AnswerOption (AskUserQuestion), MessageImage, etc.
    Events/                     # SseNotification, FileChangeEvent
    Responses/                  # ErrorResult, UpdateResult, ToolStatsResponse/ToolStat, UsageResponse/UsageRow, etc.
    Parameters/                 # [AsParameters] records (SessionIdParameters, UserImageParameters, etc.)
  Helpers/
    PathHelper                  # Path traversal prevention (shared by TaskService, PlanService)
    TokenCostCalculator         # Model-aware token cost calculation (Opus/Sonnet/Haiku pricing)
  Extensions/                   # ServiceCollectionExtensions, WebApplicationExtensions
  Services/
    SessionService              # Session discovery from tasks/ + metadata from projects/, snapshots
    TaskService                 # Task CRUD, dependency validation, notes, snapshots
    TeamService                 # Team config reading with 5s cache TTL
    PlanService                 # Plan markdown reading
    SubagentService             # Subagent JSONL transcript parsing from projects/
    MessageService              # JSONL tail-reading for session/subagent conversation messages
    SessionActivityService      # Activity status (thinking/waiting/idle/error) + token usage (incl. latest-turn context size) from JSONL
    ToolStatsService            # Per-session tool-call aggregation (success/failed/rejected, output-impact) from JSONL
    UsageService                # Per-participant token/cost breakdown (lead session + each subagent), composes SessionActivityService + SubagentService
    ClaudeDirectoryWatcher      # BackgroundService with 4 FileSystemWatchers (extension-filtered)
    SseClientManager            # SSE client connection manager (singleton)
  UpdateCheck/
    Models/                     # NuGetVersionIndex, UpdateCheckCache (internal)
    Services/                   # UpdateCheckService (BackgroundService), logger messages
  wwwroot/
    index.html                  # Single-page Kanban + Timeline dashboard (embedded resource)
    images/icon.png             # ATC logo (favicon + sidebar)
  GlobalUsings.cs               # All using directives centralized here
test/Atc.Claude.Kanban.Tests/
  Helpers/                      # PathHelper, MockHttpMessageHandler
  Services/                     # SessionService, TaskService, TeamService, SubagentService,
                                # MessageService, SessionActivityService, PlanService,
                                # SseClientManager, UpdateCheckService, ToolStatsService, UsageService tests
scripts/
  seed-demo-fixture.ps1         # Materializes a synthetic ~/.claude tree for reproducible README screenshots (run with --dir)
```

## Architecture

- **ASP.NET Core Minimal APIs** with `Atc.Rest.MinimalApi` endpoint definitions
- **FileSystemWatcher** + `System.Threading.Channels` for event-driven file monitoring
- **Server-Sent Events** via `Results.Stream` with raw UTF-8 byte writes (NOT StreamWriter — Kestrel disallows synchronous Flush)
- **Heartbeats** via `Task.Delay` (NOT PeriodicTimer — can't call WaitForNextTickAsync concurrently)
- **Async service layer** — all file I/O uses `ReadAllTextAsync`/`WriteAllTextAsync`
- **IMemoryCache** with TTL expiration (10s sessions, 5s teams, 5s messages, 5s activity)
- **Embedded static files** via `ManifestEmbeddedFileProvider`
- **JSONL tail-reading** — 64KB adaptive buffer for messages, 32KB for activity status; seeks from end to avoid loading entire files
- **Activity status derivation** — uses conversation entry timestamps (not file mtime) to detect thinking/waiting/idle/error states; hooks writing progress entries don't reset the timer
- **Token cost calculation** — model-aware pricing (Opus $15/$75, Sonnet $3/$15, Haiku $0.80/$4 per 1M tokens) with cache creation/read multipliers
- **Context-window size** — the latest turn's prompt tokens (`SessionTokenUsage.ContextTokens` = input + cache_read + cache_creation of the last `usage` block, overwritten per turn). The model's window size isn't in the transcript, so the frontend infers it: 200K, or 1M once context exceeds 200K
- **Per-participant usage** — `UsageService` composes the lead session's usage with each subagent's transcript usage (`SessionActivityService.GetTokenUsageForPathAsync`) for the Session Usage modal
- **Tool statistics** — `ToolStatsService` scans the session JSONL once, correlating `tool_use` with `tool_result`; "rejected" is detected from the line-level `toolUseResult` string (not the block content)

## Known Pitfalls

- **`System.Math.Round`** must be fully qualified — `Atc.Math` namespace conflict shadows `Math.Round`
- **`System.Console.WriteLine`** must be fully qualified — `Atc.Console` namespace conflict shadows `Console`
- **AsyncFixer02** false positives on in-memory LINQ chains after `await` — suppress with `#pragma warning disable AsyncFixer02`
- **SSE must use unnamed events** (no `event:` line) — `EventSource.onmessage` only receives unnamed events
- **SSE must write raw UTF-8 bytes** to `Stream` — `StreamWriter` with `AutoFlush = true` triggers synchronous `Flush()` which Kestrel rejects
- **JSON options via DI** — use `Atc.Serialization.JsonSerializerOptionsFactory.Create()` registered as singleton, never allocate `JsonSerializerOptions` per-request
- **Session/task snapshots** — `ConcurrentDictionary` caches keep sessions and tasks visible after Claude Code deletes task directories; cleared on page reload via `POST /api/cache/clear`
- **File watcher extension filtering** — tasks/teams fire on `.json`, projects on `.jsonl`, plans on `.md` only; other file types are ignored to avoid spurious SSE events
- **Task timestamps** — Claude Code task JSON files don't store `createdAt`/`updatedAt`; enriched at read time from `File.GetCreationTimeUtc`/`File.GetLastWriteTimeUtc`
- **Blocked badge** — must check actual status of blocking tasks, not just presence of `blockedBy` array (completed blockers should clear the badge)
- **Auto-port** — probes port availability with `TcpListener` before building the app; explicit `--port` fails fast with a styled error
- **JSONL pre-filter guard** — all JSONL parsers check `line[0] != '{'` before calling `JsonDocument.Parse` to avoid first-chance `JsonReaderException` spam from partial lines after tail seeks
- **Activity status uses conversation timestamps** — file mtime is unreliable for elapsed time because Claude Code hooks write progress entries that reset the mtime; the `timestamp` field inside the last `assistant`/`user` JSONL entry is used instead
- **Message panel tool result correlation** — `tool_use.id` from assistant messages is matched to `tool_result.tool_use_id` from user messages in a single-pass JSONL scan
- **Auto-update** — `UpdateCheckService` (BackgroundService) checks NuGet flat container API on startup with 24h cache at `{LocalApplicationData}/atc-claude-kanban/update-check.json`. Suppressed by `ATC_NO_UPDATE_CHECK=1`, `--no-update-check`, or CI env vars (`CI`, `TF_BUILD`, `GITHUB_ACTIONS`). SSE broadcasts `version-update` notifications; `SseClientManager` replays pending version-update to late-connecting clients via `pendingVersionUpdate` field
- **Context tokens are latest-turn, not cumulative** — `ContextTokens` is overwritten per `usage` block so it ends on the most recent turn; the cumulative `InputTokens`/`CacheReadTokens` totals are much larger (cache read repeats the context every turn) and must NOT be used for the context-window bar
- **Context-window size is inferred, not recorded** — the JSONL has no window-size field, so the frontend assumes 200K and treats anything over 200K as a 1M session. A 1M session below 200K is mislabeled until it crosses the threshold; the real value would require the plugin's context-status data, which we do not ship
- **No rate-limit data** — upstream's rate-limit footer reads `rate_limits` from a plugin-written context-status source we don't have; there is no rate-limit info in the JSONL transcripts, so that feature is not portable as-is
- **Session title fallback** — `SessionService.ExtractJsonlFields` resolves the title in priority order custom-title → ai-title → agent-name (the latter two are emitted by background "claude agents" sessions); values starting with `<` are skipped
- **Keyboard shortcuts must not hijack modifier combos** — the global keydown handler matches single keys (`f`, `a`, `r`, `c`, `i`, `p`…); it returns early for Ctrl/Cmd/Alt combos (except the owned Ctrl+D) so browser shortcuts like Ctrl+F still work
- **User image attachments are fetched lazily** — `MessageEntry.Images` carries only `{blockIndex, mediaType}`; bytes are served on demand by `GET /api/sessions/{id}/messages/{uuid}/image/{blockIndex}` so base64 data never bloats the message-list payload

## Coding Conventions

- **.NET 10**, C# 14, `.slnx` solution format
- **ATC coding rules** (atc-net/atc-coding-rules) — `TreatWarningsAsErrors` in Release
- **All using directives in GlobalUsings.cs** — do NOT add per-file usings
- **No underscore-prefixed fields** (SA1309) — use `this.field = field` in constructors
- **One type per file** (MA0048) — file name must match type name
- **Expression body** for single-return methods
- **No abbreviations** in variable names (use `client` not `kvp`, `entry` not `e`)
- **XML documentation** on all public types and members — always multi-line `<summary>` format
- **Endpoint pattern** — `TypedResults` with `Results<T1,T2>` return types, `[AsParameters]` records for route/query binding, `[FromServices]` for DI services
- **Conventional commits**: `feat:`, `fix:`, `chore:`, `docs:`
- **Do NOT modify .editorconfig files** — fix code to comply with rules
- **Versioning** — managed by release-please via `Directory.build.props`

## Frontend Conventions

- **Delegated event listeners** — use `data-action` attributes, not inline `onclick` handlers
- **Log gating** — use `log.debug()`/`log.error()` wrapper (only logs on localhost/127.0.0.1)
- **Single file** — all CSS/HTML/JS in `wwwroot/index.html` (deliberate: simplifies embedded resource distribution)
- **localStorage persistence** — user preferences survive page reloads: theme, view, notifications, archived expanded, message panel open state, collapsed project groups (`collapsedGroups`), pinned sessions (`pinnedSessions`)
- **View toggle** — Kanban vs Timeline views with `currentView` state; both render on data updates, visibility controlled by `applyView()`
- **Notification API** — request permission on first bell-icon click; `previousTaskStatuses` Map tracks `in_progress → completed` transitions
- **Web Audio API** — synthesized chime (C5 523Hz + E5 659Hz sine waves), no audio files
- **Message panel** — toggleable right-side panel (Shift+L or toolbar icon) showing session JSONL conversation log with tool parameter badges and clickable file paths
- **Tool detail modal** — clicking a tool entry opens its full arguments via `renderToolParamsHtml` (objects/JSON-string args pretty-printed); the server sends object args as raw JSON strings (`GetRawText`), so the renderer detects and re-formats them. AskUserQuestion entries also render captured answers (`renderAnswerPayloadHtml`). Tool inputs are stashed in a per-render `toolDetailCache` keyed by uuid/toolUseId (not embedded in DOM attributes)
- **Session log line dedup** — `toolDetailText` strips a redundant leading tool-name prefix from line 2 of a tool entry (line 1 already shows the name); the line is hidden when nothing remains
- **Activity status indicators** — green border (thinking), amber border (waiting), red border (error) on sidebar sessions; derived from JSONL tail-read
- **Project grouping** — `renderGroupedSessionsHtml` groups non-archived sessions by project under collapsible `.project-group-header`s with an active/total count (`isSessionActive`); collapsed state persists in localStorage (`collapsedGroups`). Flat list while searching or when no session has a project
- **Session pinning** — pin toggle on each row lifts the session into a collapsible "Pinned" group at the top (`pinnedSessionIds`, persisted as `pinnedSessions`); `getSessionItems` excludes items in collapsed groups for keyboard nav
- **Context-window bar** — per-row bar from `tokenUsage.contextTokens` vs an inferred 200K/1M window, color-coded by fill
- **Token/cost display** — accumulated token counts and model-aware cost per session in sidebar
- **Session Usage & Tool Statistics modals** — pie-chart and bar-chart icons in the session info modal open `/api/sessions/{id}/usage` (lead + per-subagent model/cost) and `/api/sessions/{id}/tool-stats` (sortable per-tool table)
- **Drag-drop** — task cards are `draggable="true"`, drop on kanban columns to change status
- **Scratchpad** — per-session notes modal (N key or toolbar icon), localStorage persistence with 500ms debounce
- **Resizable panels** — drag handle on message panel edge, width persists in localStorage
- **Open-in-editor** — clickable file paths in message log open in VS Code via `POST /api/open-in-editor`
- **Wake detection** — heartbeat timer detects system sleep (>30s drift) and refreshes stale UI
- **Activity polling** — unconditional 15s poll ensures status transitions are picked up even without SSE events
