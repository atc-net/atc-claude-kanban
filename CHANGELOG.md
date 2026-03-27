# Changelog

## [1.9.2](https://github.com/atc-net/atc-claude-kanban/compare/v1.9.1...v1.9.2) (2026-03-27)


### Bug Fixes

* preserve subagent panel collapsed state across SSE updates ([66a8f3d](https://github.com/atc-net/atc-claude-kanban/commit/66a8f3d4f615b9ff3e679b8d458cc9dab279325e))

## [1.9.1](https://github.com/atc-net/atc-claude-kanban/compare/v1.9.0...v1.9.1) (2026-03-24)


### Bug Fixes

* enrich non-task sessions with activity status and token usage ([560cdb5](https://github.com/atc-net/atc-claude-kanban/commit/560cdb5d14fdba523ae8a765034485e1f649fa70))
* improve active filter and token cost thresholds ([66e6a9d](https://github.com/atc-net/atc-claude-kanban/commit/66e6a9dab91b862b8d91385eef4a2b86564d5662))

## [1.9.0](https://github.com/atc-net/atc-claude-kanban/compare/v1.8.0...v1.9.0) (2026-03-24)


### Features

* add mermaid.js diagram rendering in plan modals ([572e1d4](https://github.com/atc-net/atc-claude-kanban/commit/572e1d4fbac40ce6cbc462a906f75ad768d311ea))
* add message pagination with infinite scroll ([ebc4408](https://github.com/atc-net/atc-claude-kanban/commit/ebc4408b69cd85bf7cdddf6a0cf2eb920efc2200))
* add system message filtering and compaction labels ([399f25a](https://github.com/atc-net/atc-claude-kanban/commit/399f25aa42dbbbbf7ce4cf733a8f6820e564b186))
* add tool-specific icons and consecutive tool grouping ([c6ca882](https://github.com/atc-net/atc-claude-kanban/commit/c6ca8829bf4d2a79ec43546f7126f26dda467775))
* extract and display agent names from JSONL ([ef2b4f2](https://github.com/atc-net/atc-claude-kanban/commit/ef2b4f25bd0e5e594d74144f7353ecbc17404e38))


### Bug Fixes

* discover sessions from JSONL files without task directories ([ccabe49](https://github.com/atc-net/atc-claude-kanban/commit/ccabe4917a429344d2fff84bb24e6804b9667156))
* fuzzy search backslash paths, scroll overflow, modal sizing, cache keys ([a353184](https://github.com/atc-net/atc-claude-kanban/commit/a3531846afdd8507cddf89ed2cadc63ddaed5067))

## [1.8.0](https://github.com/atc-net/atc-claude-kanban/compare/v1.7.0...v1.8.0) (2026-03-21)


### Features

* add drag-drop, scratchpad, resizable panels, and open-in-editor ([151339f](https://github.com/atc-net/atc-claude-kanban/commit/151339f9abc38ac44cc75ee4cfffafd6361691cd))
* add session activity status and token/cost tracking ([4daa58b](https://github.com/atc-net/atc-claude-kanban/commit/4daa58bc2caf58cffa57c15e2e9171a3675b5222))
* add session message log panel with JSONL transcript parsing ([50affe2](https://github.com/atc-net/atc-claude-kanban/commit/50affe2d460af2f7fcbc7a26999296936c63ab09))


### Bug Fixes

* wire subagent log viewer to match agents by description ([b6c7206](https://github.com/atc-net/atc-claude-kanban/commit/b6c72063aaaabeca167899b5f2962b1c7082fd06))


### Performance Improvements

* add JSON pre-filter guard to reduce first-chance exceptions ([58286c6](https://github.com/atc-net/atc-claude-kanban/commit/58286c6fe0f1177fb0db6ca5abd4d88ffda26263))

## [1.7.0](https://github.com/atc-net/atc-claude-kanban/compare/v1.6.0...v1.7.0) (2026-03-03)


### Features

* add 3-state subagent lifecycle, last-message capture, and adaptive polling ([9ee080b](https://github.com/atc-net/atc-claude-kanban/commit/9ee080b57fb54a2cbf935f8963eb91e9c7b19cf5))

## [1.6.0](https://github.com/atc-net/atc-claude-kanban/compare/v1.5.0...v1.6.0) (2026-03-02)


### Features

* add copy-to-clipboard button on subagent cards ([166d05c](https://github.com/atc-net/atc-claude-kanban/commit/166d05c0498fb5295ec7f2514f726b72e6cdf05a))
* expose transcript path and add open-folder button for subagents ([7a9b5a5](https://github.com/atc-net/atc-claude-kanban/commit/7a9b5a5360d490b9f967962049eb3ac9b779b788))
* sort sessions by task activity in filtered views ([913a0c4](https://github.com/atc-net/atc-claude-kanban/commit/913a0c459cd162b77118819342bc1cae334d7a47))


### Bug Fixes

* preserve scroll position on SSE-triggered re-renders ([0bcdf98](https://github.com/atc-net/atc-claude-kanban/commit/0bcdf98e7d0f71c61d569626f975c0ddef3fad20))

## [1.5.0](https://github.com/atc-net/atc-claude-kanban/compare/v1.4.0...v1.5.0) (2026-02-25)


### Features

* add background auto-update check via NuGet on startup ([161c39e](https://github.com/atc-net/atc-claude-kanban/commit/161c39ed7062980c80e104abde5b3ff3ced44322))

## [1.4.0](https://github.com/atc-net/atc-claude-kanban/compare/v1.3.0...v1.4.0) (2026-02-24)


### Features

* add gradient coloring to Claude Kanban label and use braille characters ([54b0a32](https://github.com/atc-net/atc-claude-kanban/commit/54b0a32742cb89d63a3bf3e6d97be6acc02b0fd5))
* add styled watcher registration output to console ([fe317fe](https://github.com/atc-net/atc-claude-kanban/commit/fe317fe17b240a9865b0679044dd1ca9ae9b8aed))


### Bug Fixes

* set console output encoding to UTF-8 for dotnet tool emoji rendering ([fd8e48e](https://github.com/atc-net/atc-claude-kanban/commit/fd8e48e7e1e833473e258f1396992b5ff6c7811b))

## [1.3.0](https://github.com/atc-net/atc-claude-kanban/compare/v1.2.1...v1.3.0) (2026-02-24)


### Features

* add colored ASCII art startup banner ([ddae574](https://github.com/atc-net/atc-claude-kanban/commit/ddae5747c0793ec69f02aa17dacc1c2b61c7a7d5))

## [1.2.1](https://github.com/atc-net/atc-claude-kanban/compare/v1.2.0...v1.2.1) (2026-02-23)


### Bug Fixes

* apply project filter to completed sessions instead of showing all ([855f0d3](https://github.com/atc-net/atc-claude-kanban/commit/855f0d36aee2123278c45e113d50dc7ee3073f82))

## [1.2.0](https://github.com/atc-net/atc-claude-kanban/compare/v1.1.3...v1.2.0) (2026-02-21)


### Features

* add loading state to addNote mutation ([a5c7337](https://github.com/atc-net/atc-claude-kanban/commit/a5c733713e71a091ae82f3798eea12461f4457c3))
* add loading state to confirmDelete and replace alert() with toast ([dcc0c83](https://github.com/atc-net/atc-claude-kanban/commit/dcc0c83fbeccd7b2fd36e45cf6ebaea9ae415094))
* add loading state to saveTaskField mutation ([84c7117](https://github.com/atc-net/atc-claude-kanban/commit/84c7117033961e933e077d68396347694e7abda8))
* add loading state with progress to bulk delete session tasks ([0ee8db0](https://github.com/atc-net/atc-claude-kanban/commit/0ee8db0cf23a82a319c0bdbdb78de9e45d0e9b98))
* add reusable showToast helper and disabled button styles ([9155621](https://github.com/atc-net/atc-claude-kanban/commit/9155621b8439a6878772a71d327b3136f62e634b))
* show more of the subagent description when card is expanded ([17e3920](https://github.com/atc-net/atc-claude-kanban/commit/17e392092045e6524c5279dc00b51f3734178d0f))


### Bug Fixes

* always refresh current session data on any SSE event ([8bf7385](https://github.com/atc-net/atc-claude-kanban/commit/8bf7385cc1cf9ba718f0b7dbbec901a70f5cadcc))
* prevent modal content clicks from closing the dialog ([dfc24e4](https://github.com/atc-net/atc-claude-kanban/commit/dfc24e449899f429231cb63d9d783c013cc4a55b))
* remove 100-character truncation from subagent descriptions ([729290e](https://github.com/atc-net/atc-claude-kanban/commit/729290e70f9526ed9c1c2d947a0bfd46dfade209))

## [1.1.3](https://github.com/atc-net/atc-claude-kanban/compare/v1.1.2...v1.1.3) (2026-02-20)


### Bug Fixes

* clean teammate-message tags from subagent descriptions on backend before truncation ([558dccc](https://github.com/atc-net/atc-claude-kanban/commit/558dcccb0dda2cc4443901e53d2d94b02115d0ed))
* preserve session progress when task files are removed but directory remains ([9b8ce06](https://github.com/atc-net/atc-claude-kanban/commit/9b8ce06fb39f9b7b4fa7ac397c66415df2656875))
* replace reset filter reload icon with X-circle and add loading spinner ([93d1140](https://github.com/atc-net/atc-claude-kanban/commit/93d114082dd08ddee864e17a08eb70edcceb7af4))
* use monospace font for headers and logo to match content style ([b5f0995](https://github.com/atc-net/atc-claude-kanban/commit/b5f0995fbc5a8f3099672920c7ff20893d41af27))

## [1.1.2](https://github.com/atc-net/atc-claude-kanban/compare/v1.1.1...v1.1.2) (2026-02-20)


### Bug Fixes

* preserve expanded subagent card state across SSE re-renders ([7422aa7](https://github.com/atc-net/atc-claude-kanban/commit/7422aa7db55eaa2ab6da6f2447bb9c25132d54f9))

## [1.1.1](https://github.com/atc-net/atc-claude-kanban/compare/v1.1.0...v1.1.1) (2026-02-18)


### Bug Fixes

* Added .kanban:has(.owner-filter-bar.visible) { padding-top: 60px; } so the column headers are pushed down when the "All Members" dropdown is visible ([226209e](https://github.com/atc-net/atc-claude-kanban/commit/226209e97e74e3a36d7415af7fb8af33915c5167))
* auto-select session when project filter matches exactly one ([17581a8](https://github.com/atc-net/atc-claude-kanban/commit/17581a8b12fc9b9af55c8373d60920bc972216b4))
* inherit project and branch from lead session for team sessions ([fc9dd68](https://github.com/atc-net/atc-claude-kanban/commit/fc9dd68c674eb4739aa743ddd0bf2d25040ef007))
* invalidate subagent cache on metadata-update SSE events ([aa95015](https://github.com/atc-net/atc-claude-kanban/commit/aa95015e28db8cbed2484afcc1bb911a727ae5b5))
* merge team lead sessions so subagents appear on the team row ([828c3ba](https://github.com/atc-net/atc-claude-kanban/commit/828c3ba96687050210dc6f779bc45f279489e673))
* remove dead TeamName property from TeamConfig ([c91173c](https://github.com/atc-net/atc-claude-kanban/commit/c91173c2f15f0571535ddca72295c7c103ef254e))
* TeamConfig JSON property names use camelCase to match Claude Code ([bcc013e](https://github.com/atc-net/atc-claude-kanban/commit/bcc013e572c43bfcd0acf2c26b80d91e87636c9f))

## [1.1.0](https://github.com/atc-net/atc-claude-kanban/compare/v1.0.0...v1.1.0) (2026-02-18)


### Features

* add auto-archive for stale sessions ([88e8aa7](https://github.com/atc-net/atc-claude-kanban/commit/88e8aa7e46eefd57b7180e5f09fbdcb6585e412a))
* add desktop and sound notifications on task completion ([5c2465a](https://github.com/atc-net/atc-claude-kanban/commit/5c2465af3634098637bfc4aff5a9253dc079a5e0))
* add kanban dashboard for Claude Code task sessions ([2e78372](https://github.com/atc-net/atc-claude-kanban/commit/2e783729f54d022ec193732f61a5b1d362b9933e))
* add session timeline view with task duration bars ([754d9ce](https://github.com/atc-net/atc-claude-kanban/commit/754d9cec94f2dc59e2a6323c8475ab4ecf0ee246))
* auto-find available port when default is taken ([83177ee](https://github.com/atc-net/atc-claude-kanban/commit/83177ee7b34095051e41cebdffa9fe6a40818282))


### Bug Fixes

* clear BLOCKED badge when blocking task completes ([2a3cc91](https://github.com/atc-net/atc-claude-kanban/commit/2a3cc91a3ce789d641b374f3dd86d599f36e9cff))
* exclude stale sessions from Active Only filter ([e00004e](https://github.com/atc-net/atc-claude-kanban/commit/e00004ea52214670ebde3aea04e97fc71b3a8ccf))
* make entire timeline row clickable and hoverable ([c0e1619](https://github.com/atc-net/atc-claude-kanban/commit/c0e16199358d118fc046260f32d457ab535b7a67))
