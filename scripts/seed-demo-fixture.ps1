#!/usr/bin/env pwsh
# Seeds a synthetic ~/.claude tree with made-up demo data for reproducible README
# screenshots. No real session data is used. Point the dashboard at it with:
#
#   dotnet run --project src/Atc.Claude.Kanban -- --dir <OutDir> --port 8099
#
# Usage: ./scripts/seed-demo-fixture.ps1 [-OutDir <path>]

param(
    [string]$OutDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'atc-kanban-demo')
)

$ErrorActionPreference = 'Stop'

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
$tasksRoot = Join-Path $OutDir 'tasks'
$projectsRoot = Join-Path $OutDir 'projects'
New-Item -ItemType Directory -Force -Path $tasksRoot, $projectsRoot, (Join-Path $OutDir 'teams'), (Join-Path $OutDir 'plans') | Out-Null

$ts = [System.DateTime]::UtcNow
function Stamp([int]$minutesAgo) { return $ts.AddMinutes(-$minutesAgo).ToString('o') }

function Usage($inp, $out, $cacheCreate, $cacheRead) {
    return [ordered]@{ input_tokens = $inp; output_tokens = $out; cache_creation_input_tokens = $cacheCreate; cache_read_input_tokens = $cacheRead }
}

# Writes an array of objects as one compact JSON object per line (JSONL).
function Write-Jsonl($path, $objects) {
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    $lines = foreach ($o in $objects) { $o | ConvertTo-Json -Depth 40 -Compress }
    Set-Content -Path $path -Value ($lines -join "`n") -Encoding utf8
}

function Write-Task($sessionId, $id, $subject, $status, $activeForm, $owner) {
    $dir = Join-Path $tasksRoot $sessionId
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $obj = [ordered]@{ id = "$id"; subject = $subject; status = $status }
    if ($activeForm) { $obj.activeForm = $activeForm }
    if ($owner) { $obj.owner = $owner }
    Set-Content -Path (Join-Path $dir "$id.json") -Value ($obj | ConvertTo-Json -Compress) -Encoding utf8
}

# ── Project / session identifiers (all made up) ───────────────────────────────
$acmeCwd = 'C:\Demo\acme-storefront'
$apiCwd = 'C:\Demo\demo-api'
$widgetCwd = 'C:\Demo\widget-dashboard'

$s1 = '1a2b3c4d-0001-4001-8001-000000000001' # acme — the rich showcase session
$s2 = '1a2b3c4d-0002-4002-8002-000000000002' # acme — large (1M) context
$s3 = '1a2b3c4d-0003-4003-8003-000000000003' # demo-api — Sonnet
$s4 = '1a2b3c4d-0004-4004-8004-000000000004' # widget-dashboard — small

$opus = 'claude-opus-4-7'
$opus48 = 'claude-opus-4-8'
$sonnet = 'claude-sonnet-4-6'
$haiku = 'claude-haiku-4-5-20251001'

# Tiny valid 1x1 PNG (data presence for the image-attachment chip).
$pngB64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='

# ── Message-log feature content (single-quoted here-strings keep markdown / XML literal) ──
# A markdown-rich assistant reply — renders as a truncated markdown preview in the feed.
$mdReply = @'
## Coupon validation plan

- Validate codes against **Stripe** before applying
- Reject **expired** coupons at checkout with a clear message
- Cover the edge cases with unit tests

```csharp
if (!coupon.IsValid(code, DateTime.UtcNow))
    return Reject("This coupon has expired");
```

Next: wire the coupon field into the checkout form.
'@

# A /compact continuation summary (isCompactSummary) — the preamble is stripped and the
# rest renders as an expandable "Compacted" entry.
$compactSummary = @'
This session is being continued from a previous conversation that ran out of context.

## Summary so far
- Implemented coupon validation against Stripe
- Fixed expiry handling and the failing CouponExpiryTests
- Pending: wire the coupon field into the checkout form
'@

# A background task-notification envelope — renders as a summary + usage chip whose result
# opens as markdown.
$taskNotif = @'
<task-notification>
<task-id>demo-task-coupon-audit</task-id>
<status>completed</status>
<summary>Agent "Audit coupon edge cases" completed</summary>
<result>## Audit findings

- Coupon **stacks** correctly with free shipping
- Expired coupons are rejected with a clear message
- No double-discount path found
</result>
<usage><subagent_tokens>22316</subagent_tokens><tool_uses>6</tool_uses><duration_ms>118942</duration_ms></usage>
</task-notification>
'@

# ── Session 1: acme-storefront — checkout coupons (rich showcase) ─────────────
Write-Task $s1 1 'Validate coupon codes against Stripe' 'in_progress' 'Validating coupon codes against Stripe'
Write-Task $s1 2 'Add expiry handling for coupons' 'pending' $null
Write-Task $s1 3 'Write unit tests for coupon service' 'pending' $null
Write-Task $s1 4 'Wire coupon field into checkout form' 'completed' $null

Write-Jsonl (Join-Path $projectsRoot "acme\$s1.jsonl") @(
    [ordered]@{ type = 'user'; sessionId = $s1; cwd = $acmeCwd; gitBranch = 'feature/checkout-coupons'; slug = 'checkout-coupons'; timestamp = (Stamp 40); message = [ordered]@{ role = 'user'; content = 'Add coupon validation to the checkout flow.' } },
    [ordered]@{ type = 'ai-title'; sessionId = $s1; aiTitle = 'Add checkout coupon validation' },
    # Active /goal — surfaces as the card subtitle and in the session info modal.
    [ordered]@{ type = 'attachment'; sessionId = $s1; timestamp = (Stamp 40); attachment = [ordered]@{ type = 'goal_status'; met = $false; condition = 'All coupon tests pass before merging' } },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 39); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'tool_use'; id = 'tu_read'; name = 'Read'; input = [ordered]@{ file_path = "$acmeCwd\src\Checkout.cs" } }); usage = (Usage 1200 300 8000 40000) } },
    [ordered]@{ type = 'user'; timestamp = (Stamp 39); message = [ordered]@{ role = 'user'; content = @([ordered]@{ type = 'tool_result'; tool_use_id = 'tu_read'; content = 'public class Checkout { /* ... */ }' }) } },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 38); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'tool_use'; id = 'tu_mcp'; name = 'mcp__stripe__create_coupon'; input = [ordered]@{ percent_off = 15; duration = 'once'; metadata = [ordered]@{ campaign = 'spring-sale'; source = 'checkout' } } }); usage = (Usage 900 400 2000 90000) } },
    [ordered]@{ type = 'user'; timestamp = (Stamp 38); message = [ordered]@{ role = 'user'; content = @([ordered]@{ type = 'tool_result'; tool_use_id = 'tu_mcp'; content = '{"id":"coupon_1NXabc","percent_off":15}' }) } },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 37); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'tool_use'; id = 'tu_bash1'; name = 'Bash'; input = [ordered]@{ command = 'dotnet test --filter Coupon' } }); usage = (Usage 700 200 1000 120000) } },
    [ordered]@{ type = 'user'; timestamp = (Stamp 37); message = [ordered]@{ role = 'user'; content = @([ordered]@{ type = 'tool_result'; tool_use_id = 'tu_bash1'; content = 'Error: 1 test failed (CouponExpiryTests). exit code 1' }) } },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 36); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'tool_use'; id = 'tu_edit'; name = 'Edit'; input = [ordered]@{ file_path = "$acmeCwd\src\CouponService.cs"; old_string = 'IsValid(code)'; new_string = 'IsValid(code, DateTime.UtcNow)' } }); usage = (Usage 650 220 1500 130000) } },
    [ordered]@{ type = 'user'; timestamp = (Stamp 36); message = [ordered]@{ role = 'user'; content = @([ordered]@{ type = 'tool_result'; tool_use_id = 'tu_edit'; content = 'Edited CouponService.cs' }) } },
    # A user-rejected permission prompt (counts as "rejected" in tool stats).
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 35); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'tool_use'; id = 'tu_bash2'; name = 'Bash'; input = [ordered]@{ command = 'git push --force origin feature/checkout-coupons' } }); usage = (Usage 500 120 800 135000) } },
    [ordered]@{ type = 'user'; timestamp = (Stamp 35); toolUseResult = 'The user doesn''t want to proceed with this tool use. The tool use was rejected.'; message = [ordered]@{ role = 'user'; content = @([ordered]@{ type = 'tool_result'; tool_use_id = 'tu_bash2'; content = 'User rejected the Bash command' }) } },
    # AskUserQuestion exchange (renders question + chosen answer + descriptions).
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 34); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'tool_use'; id = 'tu_ask'; name = 'AskUserQuestion'; input = [ordered]@{ questions = @([ordered]@{ question = 'How should expired coupons be handled at checkout?' }) } }); usage = (Usage 400 90 600 140000) } },
    [ordered]@{ type = 'user'; timestamp = (Stamp 34); toolUseResult = [ordered]@{ questions = @([ordered]@{ question = 'How should expired coupons be handled at checkout?'; header = 'Expiry policy'; options = @([ordered]@{ label = 'Reject with message'; description = 'Block the order and tell the shopper the coupon expired.' }, [ordered]@{ label = 'Silently ignore'; description = 'Drop the coupon and proceed at full price.' }) }); answers = [ordered]@{ 'How should expired coupons be handled at checkout?' = 'Reject with message' } }; message = [ordered]@{ role = 'user'; content = @([ordered]@{ type = 'tool_result'; tool_use_id = 'tu_ask'; content = 'User selected: Reject with message' }) } },
    # A user turn with an image attachment (chip + preview).
    [ordered]@{ type = 'user'; uuid = 'u-img-001'; timestamp = (Stamp 33); message = [ordered]@{ role = 'user'; content = @([ordered]@{ type = 'text'; text = 'Here is the failing checkout screen:' }, [ordered]@{ type = 'image'; source = [ordered]@{ type = 'base64'; media_type = 'image/png'; data = $pngB64 } }) } },
    # A prompt queued mid-turn — renders in the message log with a "queued" badge.
    [ordered]@{ type = 'queue-operation'; operation = 'enqueue'; sessionId = $s1; timestamp = (Stamp 33); content = 'also confirm the coupon stacks with free shipping' },
    # Subagent completion records — surface tool-count + duration per subagent in the Session Usage modal.
    [ordered]@{ type = 'user'; timestamp = (Stamp 38); toolUseResult = [ordered]@{ agentId = 'aexplore1'; totalTokens = 30600; totalToolUseCount = 8; totalDurationMs = 42000 }; message = [ordered]@{ role = 'user'; content = @([ordered]@{ type = 'tool_result'; tool_use_id = 'tu_explore'; content = 'Pricing is applied in CartTotals.Apply().' }) } },
    [ordered]@{ type = 'user'; timestamp = (Stamp 36); toolUseResult = [ordered]@{ agentId = 'areview1'; totalTokens = 23500; totalToolUseCount = 5; totalDurationMs = 31000 }; message = [ordered]@{ role = 'user'; content = @([ordered]@{ type = 'tool_result'; tool_use_id = 'tu_review'; content = 'Looks correct; suggest a null check.' }) } },
    # Markdown-rich assistant reply — renders as a truncated markdown preview in the feed.
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 34); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'text'; text = $mdReply }); usage = (Usage 1600 420 2000 90000) } },
    # A /compact continuation summary — renders as a single expandable "Compacted" entry.
    [ordered]@{ type = 'user'; isCompactSummary = $true; timestamp = (Stamp 33); message = [ordered]@{ role = 'user'; content = $compactSummary } },
    # A background task-notification — summary + usage chip, result opens as markdown.
    [ordered]@{ type = 'user'; timestamp = (Stamp 33); message = [ordered]@{ role = 'user'; content = $taskNotif } },
    # Mid-run model switch to Opus 4.8 — surfaces as a second row under "Lead sessions" in the Session Usage modal.
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 33); message = [ordered]@{ role = 'assistant'; model = $opus48; content = @([ordered]@{ type = 'text'; text = 'Switched to Opus 4.8 to finalize the review.' }); usage = (Usage 1800 350 2200 60000) } },
    # Final turn sets the current context size (~165k of 200k).
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 32); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'text'; text = 'Coupons now validate against Stripe with expiry handling.' }); usage = (Usage 5000 800 5000 155000) } }
)

# Session 1 subagents — running on Haiku while the lead runs on Opus.
Write-Jsonl (Join-Path $projectsRoot "acme\$s1\subagents\agent-aexplore1.jsonl") @(
    [ordered]@{ type = 'user'; slug = 'explore-pricing'; cwd = $acmeCwd; timestamp = (Stamp 39); message = [ordered]@{ role = 'user'; content = 'Find where coupon pricing is applied.' } },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 38); message = [ordered]@{ role = 'assistant'; model = $haiku; content = @([ordered]@{ type = 'text'; text = 'Pricing is applied in CartTotals.Apply().' }); usage = (Usage 2000 600 4000 28000) } }
)
Write-Jsonl (Join-Path $projectsRoot "acme\$s1\subagents\agent-areview1.jsonl") @(
    [ordered]@{ type = 'user'; slug = 'review-checkout'; cwd = $acmeCwd; timestamp = (Stamp 37); message = [ordered]@{ role = 'user'; content = 'Review the checkout coupon change.' } },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 36); message = [ordered]@{ role = 'assistant'; model = $haiku; content = @([ordered]@{ type = 'text'; text = 'Looks correct; suggest a null check.' }); usage = (Usage 1500 500 3000 19000) } }
)

# ── Session 2: acme-storefront — large (1M) context, completed ────────────────
Write-Task $s2 1 'Extract cart reducer' 'completed' $null
Write-Task $s2 2 'Migrate components to new store' 'completed' $null
Write-Jsonl (Join-Path $projectsRoot "acme\$s2.jsonl") @(
    [ordered]@{ type = 'user'; sessionId = $s2; cwd = $acmeCwd; gitBranch = 'refactor/cart-state'; timestamp = (Stamp 600); message = [ordered]@{ role = 'user'; content = 'Refactor the cart state management.' } },
    [ordered]@{ type = 'ai-title'; sessionId = $s2; aiTitle = 'Refactor cart state management' },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 590); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'text'; text = 'Done — cart state extracted into a reducer.' }); usage = (Usage 10000 2000 10000 620000) } }
)

# ── Session 3: demo-api — Sonnet, in progress, ~90k context ───────────────────
Write-Task $s3 1 'Reproduce the rate-limiter race' 'in_progress' 'Reproducing the rate-limiter race'
Write-Jsonl (Join-Path $projectsRoot "api\$s3.jsonl") @(
    [ordered]@{ type = 'user'; sessionId = $s3; cwd = $apiCwd; gitBranch = 'fix/rate-limiter'; timestamp = (Stamp 20); message = [ordered]@{ role = 'user'; content = 'Fix the rate-limiter race condition.' } },
    [ordered]@{ type = 'ai-title'; sessionId = $s3; aiTitle = 'Fix rate-limiter race condition' },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 18); message = [ordered]@{ role = 'assistant'; model = $sonnet; content = @([ordered]@{ type = 'text'; text = 'Looking into the lock ordering.' }); usage = (Usage 3000 700 3000 84000) } }
)

# ── Session 4: widget-dashboard — small context, pending ──────────────────────
Write-Task $s4 1 'Scope the chart migration' 'pending' $null
Write-Jsonl (Join-Path $projectsRoot "widget\$s4.jsonl") @(
    [ordered]@{ type = 'user'; sessionId = $s4; cwd = $widgetCwd; gitBranch = 'main'; timestamp = (Stamp 90); message = [ordered]@{ role = 'user'; content = 'Plan the chart library migration.' } },
    [ordered]@{ type = 'ai-title'; sessionId = $s4; aiTitle = 'Migrate charts to v2' },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 88); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'text'; text = 'Drafting a migration plan.' }); usage = (Usage 2000 500 1000 9000) } }
)

# ── Team session: frontend-squad (agent team rebuilding the storefront header) ─
$team = 'frontend-squad'
$s5 = '1a2b3c4d-0005-4005-8005-000000000005' # team lead session
$teamDir = Join-Path $OutDir "teams\$team"
New-Item -ItemType Directory -Force -Path $teamDir | Out-Null
$teamConfig = [ordered]@{
    name          = 'frontend-squad'
    description   = 'Rebuilds the storefront header as a small agent team.'
    leadSessionId = $s5
    leadAgentId   = 'lead01'
    workingDir    = $acmeCwd
    createdAt     = [System.DateTimeOffset]::UtcNow.AddMinutes(-50).ToUnixTimeMilliseconds()
    members       = @(
        [ordered]@{ name = 'team-lead'; agentId = 'lead01'; agentType = 'team-lead'; model = $opus },
        [ordered]@{ name = 'ui-builder'; agentId = 'ui0001'; agentType = 'frontend'; model = $sonnet },
        [ordered]@{ name = 'api-integrator'; agentId = 'api0001'; agentType = 'backend'; model = $sonnet }
    )
}
Set-Content -Path (Join-Path $teamDir 'config.json') -Value ($teamConfig | ConvertTo-Json -Depth 10) -Encoding utf8

# Team tasks live under tasks/<teamName>/ and are owned by members (owner-coloured on the board).
Write-Task $team 1 'Build responsive header shell' 'completed' $null 'ui-builder'
Write-Task $team 2 'Wire nav links to the router' 'completed' $null 'ui-builder'
Write-Task $team 3 'Add account-menu API calls' 'in_progress' 'Adding account-menu API calls' 'api-integrator'
Write-Task $team 4 'Coordinate header rollout' 'in_progress' 'Coordinating header rollout' 'team-lead'
Write-Task $team 5 'Add a search box to the header' 'pending' $null 'ui-builder'
Write-Task $team 6 'Cache the nav configuration' 'pending' $null 'api-integrator'

# Team session metadata (session id == team name) and the lead session it inherits from.
Write-Jsonl (Join-Path $projectsRoot "acme\$team.jsonl") @(
    [ordered]@{ type = 'user'; sessionId = $team; cwd = $acmeCwd; gitBranch = 'feature/new-header'; timestamp = (Stamp 50); message = [ordered]@{ role = 'user'; content = 'Rebuild the storefront header as a team.' } },
    [ordered]@{ type = 'ai-title'; sessionId = $team; aiTitle = 'Rebuild storefront header' },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 48); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'text'; text = 'Delegating to ui-builder and api-integrator.' }); usage = (Usage 4000 900 4000 70000) } }
)
Write-Jsonl (Join-Path $projectsRoot "acme\$s5.jsonl") @(
    [ordered]@{ type = 'user'; sessionId = $s5; cwd = $acmeCwd; gitBranch = 'feature/new-header'; timestamp = (Stamp 50); message = [ordered]@{ role = 'user'; content = 'Lead the header rebuild.' } },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 49); message = [ordered]@{ role = 'assistant'; model = $opus; content = @([ordered]@{ type = 'text'; text = 'On it.' }); usage = (Usage 3000 700 3000 60000) } }
)

# Team subagents (rendered in the footer of the team board), keyed by the team session id.
Write-Jsonl (Join-Path $projectsRoot "acme\$team\subagents\agent-ui00001.jsonl") @(
    [ordered]@{ type = 'user'; slug = 'ui-builder'; cwd = $acmeCwd; timestamp = (Stamp 47); message = [ordered]@{ role = 'user'; content = 'Build the header shell.' } },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 46); message = [ordered]@{ role = 'assistant'; model = $sonnet; content = @([ordered]@{ type = 'text'; text = 'Header shell complete.' }); usage = (Usage 2500 800 3000 31000) } }
)
Write-Jsonl (Join-Path $projectsRoot "acme\$team\subagents\agent-api00001.jsonl") @(
    [ordered]@{ type = 'user'; slug = 'api-integrator'; cwd = $acmeCwd; timestamp = (Stamp 45); message = [ordered]@{ role = 'user'; content = 'Wire the account-menu API.' } },
    [ordered]@{ type = 'assistant'; timestamp = (Stamp 44); message = [ordered]@{ role = 'assistant'; model = $sonnet; content = @([ordered]@{ type = 'text'; text = 'Account-menu API wired.' }); usage = (Usage 2000 600 2500 22000) } }
)

Write-Host "Seeded demo fixture at: $OutDir"
Write-Host "Run: dotnet run --project src/Atc.Claude.Kanban -- --dir `"$OutDir`" --port 8099"
