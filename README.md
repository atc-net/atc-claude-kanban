# Atc.Claude.Kanban

Real-time Kanban dashboard for monitoring [Claude Code](https://docs.anthropic.com/en/docs/claude-code) agent tasks, sessions, and subagents through a browser-based board.

- 📊 **Real-time Kanban board** — tasks flow through Pending → In Progress → Completed as Claude works
- 🤖 **Agent team support** — color-coded team members, owner filtering, member badges
- 🧩 **Subagent visibility** — see active subagents spawned via the Task tool, parsed from JSONL transcripts
- 🔗 **Task dependencies** — visual blockedBy/blocks relationships
- 📡 **Server-Sent Events** — instant updates via file watching, no polling
- ⌨️ **Keyboard navigation** — vim-style (hjkl) + arrow keys, sidebar/board focus toggling
- 🌙 **Dark/light themes** — system preference detection
- 🔍 **Fuzzy search** — across sessions, tasks, descriptions, and project paths
- 📝 **Plan viewer** — view and open Claude Code plans directly from the dashboard

## 📋 Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later, via RollForward)

## 🚀 Getting Started

### Install

```powershell
dotnet tool install -g atc-claude-kanban
```

### Run

```powershell
# Start the dashboard (default: http://localhost:3456)
atc-claude-kanban

# Start and open browser automatically
atc-claude-kanban --open

# Custom port
atc-claude-kanban --port 8080

# Custom Claude directory
atc-claude-kanban --dir ~/.claude-work
```

Then open your browser to `http://localhost:3456` and watch your Claude Code tasks in real time.

## ✨ Features

### 📊 Kanban Board

Three-column board showing task status with live updates:

| Column | Description |
|--------|-------------|
| **Pending** | Tasks waiting to start |
| **In Progress** | Tasks Claude is actively working on (pulsing indicator) |
| **Completed** | Finished tasks |

### 🤖 Agent Teams

When Claude Code spawns agent teams, the dashboard shows:
- Color-coded owner badges per team member
- Owner filtering dropdown
- Team info modal with member details
- Task counts per agent

### 🧩 Subagents

When Claude Code spawns subagents via the Task tool, the dashboard shows:
- Active subagent count badge in the sidebar (only when subagents are running)
- Collapsible subagent panel below Kanban columns with status dots, model info, and descriptions
- "Show all" toggle to view historical subagents (default: active only)
- Parsed from JSONL transcript files at `~/.claude/projects/{hash}/{sessionId}/subagents/`

### ⌨️ Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `?` | Show help |
| `j`/`k` | Navigate up/down |
| `h`/`l` | Navigate columns |
| `Enter` | Toggle task detail |
| `Tab` | Switch sidebar/board focus |
| `[` | Toggle sidebar |
| `I` | Session info |
| `P` | Open session plan |
| `C` | Copy project path |
| `F` | Open project folder |
| `D` | Delete task |

### 📡 How It Works

```
Claude Code writes task JSON files
        ↓
FileSystemWatcher detects changes
        ↓
Debounce + parse + cache (IMemoryCache)
        ↓
Broadcast via Server-Sent Events
        ↓
Browser updates Kanban board in real-time
```

The tool watches these paths under `~/.claude/`:

| Path | Content |
|------|---------|
| `tasks/{sessionId}/*.json` | Individual task files |
| `teams/{teamName}/config.json` | Team configurations |
| `projects/{hash}/sessions-index.json` | Session metadata |
| `projects/{hash}/{sessionId}/subagents/agent-*.jsonl` | Subagent transcripts |
| `plans/{slug}.md` | Plan markdown files |

## 📂 Claude Code Directory Structure

The dashboard reads from `~/.claude/`, where Claude Code stores all session data:

```
~/.claude/
├── tasks/                              ← PRIMARY: task files (what the Kanban reads)
│   ├── {sessionId}/*.json              ← Session-scoped tasks (UUID)
│   └── {teamName}/*.json               ← Team-scoped tasks (named)
│
├── teams/                              ← Team configurations (agent swarms)
│   └── {teamName}/config.json          ← Members, roles, lead agent
│
├── projects/                           ← Session metadata (enrichment)
│   └── {path-hash}/
│       ├── sessions-index.json         ← Session list with project path, git branch
│       ├── {sessionId}.jsonl           ← Session transcript (one JSON object per line)
│       └── {sessionId}/subagents/      ← Subagent transcripts
│
└── plans/                              ← Plan markdown files
    └── {slug}.md
```

**JSON vs JSONL**: Task files, team configs, and session indexes are standard `.json` (single object). Session transcripts are `.jsonl` (JSON Lines — one JSON object per line, append-only). The dashboard only reads `.json` files; `.jsonl` transcripts are used for metadata discovery (project path, git branch).

**Session discovery**: Sessions appear on the board if they have task `.json` files under `tasks/`, or if they have active subagents under `projects/`. The `projects/` and `teams/` directories enrich sessions with metadata (project name, git branch, team members).

## 🏗️ Architecture

- **ASP.NET Core Minimal APIs** with `Atc.Rest.MinimalApi` endpoint definitions
- **FileSystemWatcher** + `System.Threading.Channels` for event-driven file monitoring
- **Server-Sent Events** via `Results.Stream` with raw UTF-8 byte writes and `Task.Delay` heartbeats
- **Async service layer** — all file I/O uses `ReadAllTextAsync`/`WriteAllTextAsync`
- **IMemoryCache** with TTL expiration (10s sessions, 5s teams)
- **Embedded static files** — single HTML dashboard served via `ManifestEmbeddedFileProvider`
- **Delegated event listeners** — frontend uses `data-action` attributes instead of inline `onclick`

## 🤝 How to contribute

[Contribution Guidelines](https://atc-net.github.io/introduction/about-atc#how-to-contribute)

[Coding Guidelines](https://atc-net.github.io/introduction/about-atc#coding-guidelines)
