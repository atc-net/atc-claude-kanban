# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A .NET 10 dotnet tool that serves a real-time Kanban dashboard for monitoring Claude Code agent tasks, sessions, and subagents. It watches `~/.claude/` directories, broadcasting changes via Server-Sent Events to a browser-based board.

## Build & Test

```powershell
# Build (Release mode enforces all analyzer rules as errors)
dotnet build -c Release

# Run tests (115 tests across 11 test classes)
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
- **Token cost calculation** — model-aware pricing (Fable 5 $10/$50, Opus 4.5+ $5/$25, Sonnet $3/$15, Haiku 4.5 $1/$5 per 1M tokens) with cache creation (1.25×) and cache read (0.10×) multipliers
- **Context-window size** — the latest turn's prompt tokens (`SessionTokenUsage.ContextTokens` = input + cache_read + cache_creation of the last `usage` block, overwritten per turn). The model's window size isn't in the transcript, so the frontend infers it: 200K, or 1M once context exceeds 200K
- **Per-participant usage** — `UsageService` composes the lead session's usage with each subagent's transcript usage (`SessionActivityService.GetTokenUsageForPathAsync`) for the Session Usage modal. Usage is bucketed per model so a session that switches models mid-run (e.g. Opus 4.7→4.8) is priced per model and shows a `models[]` breakdown under the participant row
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
- **Task writes replace, never truncate** — `WriteTaskFileAsync` writes to `<file>.<pid>.<seq>.tmp` beside the target and moves it over, because Claude Code reads these files while we write them and an in-place write truncates first. The move is retried (10 attempts, backing off) since Windows reports a sharing violation rather than waiting when the target is open, and the temp file is removed in a `finally` so a failed write cannot leave a `.tmp` the board would read as a task
- **Task timestamps** — Claude Code task JSON files don't store `createdAt`/`updatedAt`; enriched at read time from `File.GetCreationTimeUtc`/`File.GetLastWriteTimeUtc`
- **Blocked badge** — must check actual status of blocking tasks, not just presence of `blockedBy` array (completed blockers should clear the badge)
- **An agent's result is whichever record comes last** — `FindLastAgentResult` scans a transcript tail backwards and takes the newest of an assistant text block and a `SubagentHandback` / `StructuredOutput` call. Background agents end on `SubagentHandback` (its `input.message` holds the report), so preferring assistant text would show mid-run narration as the result. The scan reads 5KB first and widens to 256KB only when that window holds no complete record, which happens whenever the hand-back line alone is larger than 5KB — in practice it usually is
- **Auto-port** — probes port availability with `TcpListener` before building the app; explicit `--port` fails fast with a styled error
- **JSONL pre-filter guard** — all JSONL parsers check `line[0] != '{'` before calling `JsonDocument.Parse` to avoid first-chance `JsonReaderException` spam from partial lines after tail seeks
- **Activity status uses conversation timestamps** — file mtime is unreliable for elapsed time because Claude Code hooks write progress entries that reset the mtime; the `timestamp` field inside the last `assistant`/`user` JSONL entry is used instead
- **Only conversation entries may decide activity status** — Claude Code appends bookkeeping entries after every turn (`attachment`, `last-prompt`, `ai-title`, `mode`, `permission-mode`, `atis-latch`…), so the last line of a live transcript is almost never a conversation entry. `IsStatusSignalType` allowlists `assistant`/`user`/`progress`; reading the newest line of any type reported every running session as `idle`. Keep it an allowlist — a denylist breaks again the next time a new sidecar type appears
- **In-flight work stays live for the full 60s idle window** — a model turn pauses longer than the 15s thinking threshold between transcript writes, and the frontend polls activity on a similar interval, so classifying only inside the shorter window made a busy session flicker to `idle`. A finished `text` answer is the exception: it reads `waiting` only under 15s, then goes quiet
- **Session recency comes from the transcript** — `ModifiedAt` is advanced to the transcript's last-write time wherever that is newer (`ApplyTranscriptRecency`), because task files and subagent directories stop changing while a conversation continues. It never moves backwards, so a resumed transcript cannot make a fresh task list look stale. The sidebar requests only the 20 most recent and filters by project client-side, so a stale timestamp hides a session entirely
- **Message panel tool result correlation** — `tool_use.id` from assistant messages is matched to `tool_result.tool_use_id` from user messages in a single-pass JSONL scan
- **Auto-update** — `UpdateCheckService` (BackgroundService) checks NuGet flat container API on startup with 24h cache at `{LocalApplicationData}/atc-claude-kanban/update-check.json`. Suppressed by `ATC_NO_UPDATE_CHECK=1`, `--no-update-check`, or CI env vars (`CI`, `TF_BUILD`, `GITHUB_ACTIONS`). SSE broadcasts `version-update` notifications; `SseClientManager` replays pending version-update to late-connecting clients via `pendingVersionUpdate` field
- **Context tokens are latest-turn, not cumulative** — `ContextTokens` is overwritten per `usage` block so it ends on the most recent turn; the cumulative `InputTokens`/`CacheReadTokens` totals are much larger (cache read repeats the context every turn) and must NOT be used for the context-window bar
- **Context-window size is inferred, not recorded** — the JSONL has no window-size field, so the frontend assumes 200K and treats anything over 200K as a 1M session. A 1M session below 200K is mislabeled until it crosses the threshold; the real value would require the plugin's context-status data, which we do not ship
- **No rate-limit data** — upstream's rate-limit footer reads `rate_limits` from a plugin-written context-status source we don't have; there is no rate-limit info in the JSONL transcripts, so that feature is not portable as-is
- **Usage blocks must be deduped by `message.id`** — a multi-block assistant turn is written as several JSONL lines that all repeat the same `message.id` and the same `usage` object; counting every line double-counts (often 2×+). `AccumulateTokenUsageAsync` skips a line once its `message.id` is seen. Lines without a `message.id` (e.g. synthetic test fixtures) are always counted
- **Input/output come from `usage.iterations[]`** — one assistant message can span several internal API requests; the top-level `usage.input_tokens`/`output_tokens` only reflect the last one, so they undercount input massively (160×+ in practice). When `usage.iterations[]` has >1 entry, input/output are summed across it. Cache read/creation are per-message (identical across iterations) and are taken from the top-level, NOT summed. `ContextTokens` also uses the top-level input (one turn's prompt), not the iteration sum
- **Cost is a list-price estimate, ~20–30% under Claude's `/usage`** — the dedup + per-model + iterations fixes bring the estimate from ~4× over to within ~30% (erring low). The residual is mostly cache-creation tiering: `/usage` bills far more expensive cache-*write* than the transcript's `cache_creation_input_tokens` (flat == nested 5m/1h) exposes, and cache-write costs 12.5× cache-read. That billing-pipeline detail is not in the JSONL, and `/usage` is itself an estimate captured at a different moment, so an exact match is not achievable from the transcript alone
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
- **One light-theme predicate** — `isLightTheme()` is the only correct answer to "which palette is on screen". The `light` class is set only when a theme was chosen explicitly; with no saved preference the CSS follows `prefers-color-scheme`, so a bare `classList.contains('light')` reports dark while the page renders light. Every consumer (theme icon, highlight.js stylesheet swap, mermaid init, the toggle itself) must call it rather than re-deriving
- **Single file** — all CSS/HTML/JS in `wwwroot/index.html` (deliberate: simplifies embedded resource distribution)
- **All browser storage goes through `storage`** — `storage.getItem`/`setItem`/`removeItem` wrap `localStorage`, which throws outright when site data is blocked by policy or in a private window, and when the quota is full. Several preference reads run while the page is still being set up, so a direct call there blanks the board instead of losing one setting. Never call `localStorage` directly outside the wrapper
- **localStorage persistence** — user preferences survive page reloads: theme, view, notifications, archived expanded, message panel open state, collapsed project groups (`collapsedGroups`), pinned sessions (`pinnedSessions`)
- **View toggle** — Kanban vs Timeline views with `currentView` state; both render on data updates, visibility controlled by `applyView()`
- **Notification API** — request permission on first bell-icon click; `previousTaskStatuses` Map tracks `in_progress → completed` transitions
- **Web Audio API** — synthesized chime (C5 523Hz + E5 659Hz sine waves), no audio files
- **Message panel** — toggleable right-side panel (Shift+L or toolbar icon) showing session JSONL conversation log with tool parameter badges and clickable file paths
- **Tool detail modal** — clicking a tool entry opens its full arguments via `renderToolParamsHtml` (objects/JSON-string args pretty-printed); the server sends object args as raw JSON strings (`GetRawText`), so the renderer detects and re-formats them. AskUserQuestion entries also render captured answers (`renderAnswerPayloadHtml`). Tool inputs are stashed in a per-render `toolDetailCache` keyed by uuid/toolUseId (not embedded in DOM attributes)
- **Session log line dedup** — `toolDetailText` strips a redundant leading tool-name prefix from line 2 of a tool entry (line 1 already shows the name); the line is hidden when nothing remains
- **Live vs active** — `isSessionLive()` means "doing something right now" and is the only thing allowed to drive the pulse dot; it reads `activityStatus` and active subagents, never task state, because an agent that stops leaves its task marked `in_progress` forever. `isSessionActive()` is wider (live, or holding running work) and drives the project-group count and auto-expand
- **Activity status indicators** — green border (thinking), amber border (waiting), red border (error) on sidebar sessions; derived from JSONL tail-read
- **Project grouping** — `renderGroupedSessionsHtml` groups non-archived sessions by project under collapsible `.project-group-header`s with an active/total count (`isSessionActive`); collapsed state persists in localStorage (`collapsedGroups`). Flat list while searching or when no session has a project
- **Session picker (Shift+P)** — filterable jump list over every loaded session, matching name, project folder, full path, branch and id. Ranking is `rankSessionsForPicker` (pure, so it can be exercised standalone): better match class first, then recency. Bound on `e.code === 'KeyP'` so it stays on the same physical key across layouts, and checked *before* the bare `p` bindings, which match `'P'` too. Choosing a session expands its collapsed section; `renderSessions` already force-includes the selected session, so a pick hidden by the current filter still shows
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
