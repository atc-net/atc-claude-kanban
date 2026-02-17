# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A .NET 10 dotnet tool that serves a real-time Kanban dashboard for monitoring Claude Code agent tasks, sessions, and subagents. It watches `~/.claude/` directories, broadcasting changes via Server-Sent Events to a browser-based board.

## Build & Test

```powershell
# Build (Release mode enforces all analyzer rules as errors)
dotnet build -c Release

# Run tests (53 tests across 7 test classes)
dotnet test

# Run the dashboard locally
dotnet run --project src/Atc.Claude.Kanban -- --open

# Custom port
dotnet run --project src/Atc.Claude.Kanban -- --port 8080
```

## Repository Structure

```
src/Atc.Claude.Kanban/
  Program.cs                    # Entry point, CLI args, auto-port discovery, WebApplication wiring
  EndpointDefinitions/          # Atc.Rest.MinimalApi IEndpointDefinition implementations
    SessionEndpointDefinition   # /api/sessions, /api/sessions/{id}, /api/sessions/{id}/agents
    TaskEndpointDefinition      # /api/tasks/all, PUT/DELETE/POST task operations
    TeamEndpointDefinition      # /api/teams/{name}
    ProjectEndpointDefinition   # /api/projects
    PlanEndpointDefinition      # /api/plans/{slug}, /api/plans/{slug}/open
    SubagentEndpointDefinition  # /api/sessions/{id}/subagents
    SseEndpointDefinition       # /api/events (SSE), /api/version, /api/cache/clear
    UtilityEndpointDefinition   # /api/open-folder
  Contracts/
    Models/                     # ClaudeTask, SessionInfo, TeamConfig, SubagentInfo, etc.
    Events/                     # SseNotification, FileChangeEvent
    Responses/                  # ErrorResult, UpdateResult, AddNoteResult, etc.
    Parameters/                 # [AsParameters] records (SessionIdParameters, TaskIdParameters, etc.)
  Helpers/
    PathHelper                  # Path traversal prevention (shared by TaskService, PlanService)
  Extensions/                   # ServiceCollectionExtensions, WebApplicationExtensions
  Services/
    SessionService              # Session discovery from tasks/ + metadata from projects/, snapshots
    TaskService                 # Task CRUD, dependency validation, notes, snapshots
    TeamService                 # Team config reading with 5s cache TTL
    PlanService                 # Plan markdown reading
    SubagentService             # Subagent JSONL transcript parsing from projects/
    ClaudeDirectoryWatcher      # BackgroundService with 4 FileSystemWatchers (extension-filtered)
    SseClientManager            # SSE client connection manager (singleton)
  wwwroot/
    index.html                  # Single-page Kanban + Timeline dashboard (embedded resource)
    images/icon.png             # ATC logo (favicon + sidebar)
  GlobalUsings.cs               # All using directives centralized here
test/Atc.Claude.Kanban.Tests/
  Helpers/                      # PathHelper tests (path traversal prevention)
  Services/                     # SessionService, TaskService, TeamService, SubagentService,
                                # PlanService, SseClientManager tests
```

## Architecture

- **ASP.NET Core Minimal APIs** with `Atc.Rest.MinimalApi` endpoint definitions
- **FileSystemWatcher** + `System.Threading.Channels` for event-driven file monitoring
- **Server-Sent Events** via `Results.Stream` with raw UTF-8 byte writes (NOT StreamWriter — Kestrel disallows synchronous Flush)
- **Heartbeats** via `Task.Delay` (NOT PeriodicTimer — can't call WaitForNextTickAsync concurrently)
- **Async service layer** — all file I/O uses `ReadAllTextAsync`/`WriteAllTextAsync`
- **IMemoryCache** with TTL expiration (10s sessions, 5s teams)
- **Embedded static files** via `ManifestEmbeddedFileProvider`

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
- **Auto-port** — only when using the default port; explicit `--port` fails fast. Catches `IOException` wrapping `SocketException(AddressAlreadyInUse)`

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
- **localStorage persistence** — user preferences (theme, view, notifications, archived expanded) survive page reloads
- **View toggle** — Kanban vs Timeline views with `currentView` state; both render on data updates, visibility controlled by `applyView()`
- **Notification API** — request permission on first bell-icon click; `previousTaskStatuses` Map tracks `in_progress → completed` transitions
- **Web Audio API** — synthesized chime (C5 523Hz + E5 659Hz sine waves), no audio files
