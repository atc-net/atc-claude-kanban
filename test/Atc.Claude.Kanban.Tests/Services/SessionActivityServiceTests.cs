namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="SessionActivityService"/>.
/// </summary>
public sealed class SessionActivityServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly MemoryCache cache;

    public SessionActivityServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-activity-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        cache = new MemoryCache(new MemoryCacheOptions());
    }

    public void Dispose()
    {
        cache.Dispose();

        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DeriveStatus_ReturnsIdle_WhenElapsedOver60s()
    {
        var tail = JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type = "text", text = "done" } } } });
        var status = SessionActivityService.DeriveStatusFromEntries(tail, 61);
        status.Should().Be("idle");
    }

    [Fact]
    public void DeriveStatus_ReturnsThinking_WhenAssistantToolUseWithin15s()
    {
        var tail = JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new
            {
                content = new[] { new { type = "tool_use", id = "t1", name = "Read" } },
            },
        });

        var status = SessionActivityService.DeriveStatusFromEntries(tail, 5);
        status.Should().Be("thinking");
    }

    [Fact]
    public void DeriveStatus_ReturnsWaiting_WhenAssistantTextWithin15s()
    {
        var tail = JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new
            {
                content = new[] { new { type = "text", text = "Here is my response." } },
            },
        });

        var status = SessionActivityService.DeriveStatusFromEntries(tail, 5);
        status.Should().Be("waiting");
    }

    [Fact]
    public void DeriveStatus_ReturnsThinking_WhenProgressIsLastEntry()
    {
        // Progress is the most recent entry — within 15s = thinking
        var tail = string.Join(
            "\n",
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                message = new { content = new[] { new { type = "tool_use", id = "t1", name = "Bash" } } },
            }),
            JsonSerializer.Serialize(new { type = "progress", data = new { type = "hook_progress" } }));

        var status = SessionActivityService.DeriveStatusFromEntries(tail, 5);
        status.Should().Be("thinking");
    }

    [Fact]
    public void DeriveStatus_ReturnsThinking_WhenUserEntryWithin15s()
    {
        var tail = JsonSerializer.Serialize(new { type = "user", message = new { content = "my question" } });
        var status = SessionActivityService.DeriveStatusFromEntries(tail, 2);
        status.Should().Be("thinking");
    }

    [Fact]
    public void DeriveStatus_ReturnsError_WhenErrorEntryPresent()
    {
        var tail = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type = "text", text = "ok" } } } }),
            JsonSerializer.Serialize(new { type = "error", message = "something broke" }));

        var status = SessionActivityService.DeriveStatusFromEntries(tail, 5);
        status.Should().Be("error");
    }

    [Fact]
    public void DeriveStatus_ReturnsWaiting_WhenToolUsePendingOver15s()
    {
        // Timestamp must be >15s ago for waiting detection (uses entry timestamp, not file mtime)
        var oldTimestamp = DateTime.UtcNow.AddSeconds(-30).ToString("O");
        var tail = JsonSerializer.Serialize(new
        {
            type = "assistant",
            timestamp = oldTimestamp,
            message = new
            {
                content = new[] { new { type = "tool_use", id = "t1", name = "Bash" } },
            },
        });

        // 25s elapsed (file mtime) + 30s old timestamp — waiting for permission
        SessionActivityService.DeriveStatusFromEntries(tail, 25).Should().Be("waiting");

        // 120s elapsed — still waiting, not idle (permission prompt doesn't expire)
        SessionActivityService.DeriveStatusFromEntries(tail, 120).Should().Be("waiting");
    }

    [Fact]
    public void DeriveStatus_ReturnsIdle_WhenTextResponseBetween15And60s()
    {
        var tail = JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new
            {
                content = new[] { new { type = "text", text = "done" } },
            },
        });

        var status = SessionActivityService.DeriveStatusFromEntries(tail, 30);
        status.Should().Be("idle");
    }

    [Fact]
    public void DeriveStatus_ReturnsIdle_WhenEmptyTail()
    {
        var status = SessionActivityService.DeriveStatusFromEntries(string.Empty, 5);
        status.Should().Be("idle");
    }

    [Fact]
    public async Task GetActivityStatus_ReturnsIdle_WhenNoJsonlFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new SessionActivityService(tempDir, cache);

        var status = await service.GetActivityStatusAsync("nonexistent", cancellationToken);
        status.Should().Be("idle");
    }

    [Fact]
    public async Task GetTokenUsage_ReturnsNull_WhenNoJsonlFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new SessionActivityService(tempDir, cache);

        var usage = await service.GetTokenUsageAsync("nonexistent", cancellationToken);
        usage.Should().BeNull();
    }

    [Fact]
    public async Task GetTokenUsage_AccumulatesTokensFromUsageBlocks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hash-tok");
        Directory.CreateDirectory(projectDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                message = new
                {
                    model = "claude-opus-4-6",
                    content = new[] { new { type = "text", text = "first" } },
                    usage = new
                    {
                        input_tokens = 100,
                        output_tokens = 50,
                        cache_creation_input_tokens = 10,
                        cache_read_input_tokens = 200,
                    },
                },
            }),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                message = new
                {
                    model = "claude-opus-4-6",
                    content = new[] { new { type = "text", text = "second" } },
                    usage = new
                    {
                        input_tokens = 150,
                        output_tokens = 75,
                        cache_creation_input_tokens = 20,
                        cache_read_input_tokens = 300,
                    },
                },
            }));

        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-tok.jsonl"),
            jsonl,
            cancellationToken);

        var service = new SessionActivityService(tempDir, cache);

        var usage = await service.GetTokenUsageAsync("session-tok", cancellationToken);

        usage.Should().NotBeNull();
        usage!.InputTokens.Should().Be(250);
        usage.OutputTokens.Should().Be(125);
        usage.CacheCreationTokens.Should().Be(30);
        usage.CacheReadTokens.Should().Be(500);
        usage.Model.Should().Be("claude-opus-4-6");
        usage.CostUsd.Should().BeGreaterThan(0);
    }
}