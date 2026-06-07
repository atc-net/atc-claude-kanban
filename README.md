# Atc.Claude.Kanban

Real-time Kanban dashboard for monitoring [Claude Code](https://docs.anthropic.com/en/docs/claude-code) agent tasks, sessions, and subagents through a browser-based board.

<p align="center">
  <img src="docs/overview-dark.png" alt="Dashboard overview — dark theme" width="900">
</p>

**Board & tasks**

- 📊 **Real-time Kanban board** — tasks flow Pending → In Progress → Completed as Claude works
- 📈 **Timeline view** — horizontal bars of task durations, colored by status, with hover tooltips
- 🖱️ **Drag-drop** — move tasks between columns by dragging
- 🔗 **Task dependencies** — visual blockedBy/blocks relationships with smart badge clearing
- 📦 **Auto-archive** — stale sessions (>7 days, no active tasks) collapse into an "Archived" section
- 🚫 **Session dismiss** — temporarily hide sessions from the active list without deleting them

**Sessions & sidebar**

- 🗂️ **Project grouping** — sessions grouped by project under collapsible headers with active/total counts; sections auto-expand when active work lands in them
- 📌 **Session pinning** — pin sessions to a collapsible group at the top (persisted)
- 🎯 **Activity status** — thinking/waiting/idle/error indicators per session, derived from JSONL
- 🏁 **Session goals** — an active `/goal` condition surfaces as a card subtitle and in the session info modal
- 🧮 **Context-window meter** — per-session bar showing how full the context window is (200K, or 1M inferred)
- 💰 **Token & cost tracking** — accumulated token usage and model-aware cost per session
- 🔍 **Fuzzy search** — across sessions, tasks, descriptions, and project paths
- 📓 **Scratchpad** — per-session notes with localStorage persistence and a sidebar badge

**Insight panels**

- 💬 **Session message log** — transcript with full tool-argument detail (incl. MCP), AskUserQuestion answers, and image attachments
- 💵 **Usage breakdown** — token/cost split across the lead session and each subagent, by model
- 🔧 **Tool statistics** — tool-call counts with success / failed / rejected breakdown and output-impact share
- 📝 **Plan viewer** — view and open Claude Code plans with Mermaid.js diagram rendering

**Agents & teams**

- 🤖 **Agent teams** — color-coded team members, owner filtering, member badges
- 🧩 **Subagent visibility** — active subagents with descriptions, names, and copy-to-clipboard prompts

**Live & real-time**

- 📡 **Server-Sent Events** — instant updates via file watching, no polling
- ⚡ **Smart polling** — skips polling when the tab is hidden, catches up on focus
- 🔔 **Desktop notifications** — browser notifications + sound chime when tasks complete
- ✏️ **Open in editor** — click file paths in the message log to open in VS Code

**Interface & platform**

- ⌨️ **Keyboard navigation** — vim-style (hjkl) + arrow keys, sidebar/board focus toggling
- 🌙 **Dark / light themes** — system preference detection
- 🔌 **Auto-port discovery** — finds an available port when the default is taken
- 🔄 **Auto-update** — checks NuGet for new versions on startup

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

# Custom port (fails fast if port is unavailable)
atc-claude-kanban --port 8080

# Custom Claude directory
atc-claude-kanban --dir ~/.claude-work

# Skip the NuGet update check on startup
atc-claude-kanban --no-update-check
```

Then open your browser to `http://localhost:3456` and watch your Claude Code tasks in real time.

<p align="center">
  <img src="docs/cli-started.png" alt="CLI startup banner" width="500">
</p>

> **Auto-port:** When using the default port and it's already in use, the tool automatically tries up to 10 consecutive ports (3456, 3457, ...). When `--port` is specified explicitly, the tool fails fast.

## ✨ Features

### 📊 Kanban Board

Three-column board showing task status with live updates:

| Column | Description |
|--------|-------------|
| **Pending** | Tasks waiting to start |
| **In Progress** | Tasks Claude is actively working on (pulsing indicator) |
| **Completed** | Finished tasks |

### 📈 Timeline View

Toggle between Kanban and Timeline views using the view toggle buttons in the header:

- Horizontal bars show each task's duration from creation to last update
- Color-coded by status: gray (pending), orange with glow (in-progress), green (completed)
- Hover tooltips show task name, status, duration, and start time
- Click any bar to open the task detail panel
- Time axis adapts to data range (seconds/minutes/hours/days)
- View preference persists across page reloads

<p align="center">
  <img src="docs/timeline-dark.png" alt="Timeline view" width="900">
</p>

### 🔔 Desktop Notifications

Click the bell icon in the header to enable browser notifications:

- Fires when a task transitions from **in_progress** to **completed**
- Includes a two-tone audio chime (synthesized via Web Audio API — no audio files)
- Click the notification to focus the window and open the completed task
- Preference saved in localStorage

### 📦 Auto-Archive

Sessions older than 7 days with no in-progress tasks are automatically archived:

- Archived sessions appear in a collapsible "Archived (N)" section at the bottom of the sidebar
- Dimmed to 50% opacity for visual distinction
- Expand/collapse state persists across page reloads
- Hidden during search or when filtering to active sessions

### 🤖 Agent Teams

When Claude Code spawns agent teams, the dashboard shows:
- Color-coded owner badges per team member
- Owner filtering dropdown
- Team info modal with member details
- Task counts per agent

<p align="center">
  <img src="docs/session-info-dark.png" alt="Team session info modal showing members and per-member task counts" width="900">
</p>

<p align="center">
  <img src="docs/team-board-dark.png" alt="Team session board with owner-coloured tasks and a subagents footer" width="900">
</p>

### 🧩 Subagents

When Claude Code spawns subagents via the Task tool, the dashboard shows:
- Active subagent count badge in the sidebar (only when subagents are running)
- Collapsible subagent panel below Kanban columns with status dots, model info, and descriptions
- Agent names and short descriptions extracted from the parent session's Agent tool_use blocks
- Foreground agent correlation via `toolUseResult` entries
- Copy button on task prompts and expandable/scrollable detail view
- "Show all" toggle to view historical subagents (default: active only)
- Parsed from JSONL transcript files at `~/.claude/projects/{hash}/{sessionId}/subagents/`

### 💬 Session Message Log

Toggle with the chat icon in the toolbar or `Shift+L`:

- View the conversation transcript (user prompts, assistant responses, tool calls)
- Tool parameter badges; click any tool entry to open its **full arguments** in the detail modal (including `mcp__*` tools, with object/array args shown as formatted JSON)
- **Queued messages** — prompts queued mid-turn appear in the log with a `queued` badge (they aren't re-emitted as normal user lines, so they'd otherwise be invisible)
- **Structured command & notification detail** — slash commands, command output, and task notifications render as labelled blocks in the detail modal instead of raw `<command-name>`/`<task-notification>` XML; the list collapses a slash command to its name
- **Compaction collapse** — each `/compact` shows as a single expandable "Compacted" entry carrying the continuation summary, not several stacked markers
- **AskUserQuestion** entries show the question, the chosen answer, and each option's description
- **User image attachments** appear as chips that open a full-size preview
- Read tool calls show inline offset/limit annotations (e.g., `L45 +30`)
- Clickable file paths open in VS Code (from the message log and the tool detail modal)
- Subagent log drill-in (click agent tool calls to view subagent conversation)
- Infinite scroll with pagination for long conversations
- Resizable panel (drag the left edge)
- Open/closed state persists across page reloads (localStorage)
- Click a message to open a detail modal with a fullscreen toggle for wide tool outputs

<p align="center">
  <img src="docs/msg-log-dark.png" alt="Session log panel with tool icons, a queued prompt badge, and per-model assistant labels" width="900">
</p>

<p align="center">
  <img src="docs/msg-detail-dark.png" alt="AskUserQuestion answers rendered in the message detail modal" width="900">
</p>

### ℹ️ Session Info

Click the info button on any session to view detailed metadata:

- Session ID, project path, git branch, and description
- Working directory (CWD) shown when it differs from the project root
- **Active goal** — the session's current `/goal` condition, shown in full (a met or cleared goal disappears)
- Plan viewer, copy path, open folder, **Tool Statistics**, and **Session Usage** actions
- **Dismiss button** — temporarily hide a session from the active list (in-memory only, restores on reload or in "All" view)

### 🎯 Activity Status

Sessions show real-time activity indicators in the sidebar:

| Status | Indicator | Condition |
|--------|-----------|-----------|
| **Thinking** | Green border | Claude is actively working (tool calls, processing) |
| **Waiting** | Amber border | An unanswered tool call sits at the JSONL tail (e.g. a permission or input prompt) |
| **Error** | Red border | Recent error in session |
| **Idle** | No indicator | No activity for 15+ seconds |

### 💰 Token & Cost Tracking

Each session shows accumulated token usage and estimated cost:

- Token count (e.g., "45.9M tokens") and cost (e.g., "$76.19")
- Color-coded by cost: green (<$0.50), yellow (<$2), orange (<$5), red (>=$5)
- Model-aware pricing: Opus 4.5+ ($5/$25), Sonnet ($3/$15), Haiku 4.5 ($1/$5) per 1M tokens, with cache-creation (1.25×) and cache-read (0.10×) multipliers

### 🧮 Context Window & Usage

Each session row shows a **context-window bar** — the latest turn's prompt size (input + cache) as a percentage of the model's window, color-coded green → amber → orange. The window size isn't recorded in the transcript, so it's inferred: 200K by default, or 1M once a session's context exceeds 200K.

The **Session Usage** modal (pie-chart icon in the session info modal) breaks token usage and estimated cost down **by participant and model** — the lead session plus each subagent, grouped under counted "Lead sessions" / "Subagents" subheaders, with **input / output / cache-read / cache-write** columns per model. A session that switches models mid-run (e.g. Opus 4.7 → 4.8) is priced per model and shown as separate rows. Handy for spotting, e.g., Explore subagents running on Haiku while the lead runs on Opus.

> Cost is a list-price estimate from the per-message `usage` blocks in the transcript; it typically lands ~20–30% under Claude Code's own `/usage` (cache-creation tiering isn't recorded in the JSONL).

<p align="center">
  <img src="docs/usage-modal-dark.png" alt="Session usage modal with per-subagent model breakdown" width="900">
</p>

### 🔧 Tool Statistics

The **Tool Statistics** modal (bar-chart icon in the session info modal) aggregates every tool call in the session into a sortable table — per-tool counts with **success / failed / rejected** outcomes and an output-impact share, plus summary chips. "Rejected" counts user-denied permission prompts (detected from the JSONL `toolUseResult`).

<p align="center">
  <img src="docs/tool-stats-dark.png" alt="Tool statistics modal" width="900">
</p>

### 🗂️ Project Grouping & Pinning

Sidebar sessions are grouped by project under collapsible headers. Each header shows an **active/total** count (e.g. `2/5`, active part in green) and a project-view button. A collapsed section **auto-expands** when newly-active work lands in it (and when you switch to the "Active Only" filter), so running sessions stay visible; idle sections stay collapsed. **Pin** any session via its pin icon to lift it into a collapsible "Pinned" group at the top. Collapsed groups and pins persist in `localStorage`.

### 🌗 Themes

Dark and light themes follow your system preference and can be toggled with the header icon (or `T`); the choice persists across reloads.

<p align="center">
  <img src="docs/UI-white.png" alt="Dashboard overview — light theme" width="900">
</p>

### ⌨️ Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `?` | Show help |
| `j`/`k` | Navigate up/down |
| `h`/`l` | Navigate columns |
| `Enter` | Toggle task detail |
| `Tab` | Switch sidebar/board focus |
| `[` | Toggle sidebar |
| `T` | Toggle theme |
| `A` | Show all tasks |
| `R` | Refresh data |
| `I` | Session info |
| `P` | Open session plan |
| `C` | Copy project path |
| `F` | Open project folder |
| `D` | Delete task |
| `Ctrl+D` | Dismiss selected session |
| `Shift+C` | Copy session id |
| `Shift+L` | Toggle message log panel |
| `N` | Toggle scratchpad |

<p align="center">
  <img src="docs/help-modal-dark.png" alt="Keyboard shortcuts overlay" width="900">
</p>

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

## ⚙️ Environment Variables

| Variable | Description |
|----------|-------------|
| `ATC_NO_UPDATE_CHECK=1` | Disable the NuGet update check on startup |

The update check is also automatically suppressed in CI environments (`CI`, `TF_BUILD`, or `GITHUB_ACTIONS` env vars detected).

## 🏗️ Architecture

- **ASP.NET Core Minimal APIs** with `Atc.Rest.MinimalApi` endpoint definitions
- **FileSystemWatcher** + `System.Threading.Channels` for event-driven file monitoring
- **Server-Sent Events** via `Results.Stream` with raw UTF-8 byte writes and `Task.Delay` heartbeats
- **Async service layer** — all file I/O uses `ReadAllTextAsync`/`WriteAllTextAsync`
- **IMemoryCache** with TTL expiration (10s sessions, 5s teams)
- **Embedded static files** — single HTML dashboard served via `ManifestEmbeddedFileProvider`
- **Smart polling** — skips activity polls when browser tab is hidden; catches up on focus
- **Selective fetching** — metadata SSE events skip task list fetching for reduced API overhead

## 🤝 How to contribute

[Contribution Guidelines](https://atc-net.github.io/introduction/about-atc#how-to-contribute)

[Coding Guidelines](https://atc-net.github.io/introduction/about-atc#coding-guidelines)
