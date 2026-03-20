namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="MessageService"/>.
/// </summary>
public sealed class MessageServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly MemoryCache cache;

    public MessageServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-msg-test-" + Guid.NewGuid().ToString("N"));
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
    public async Task GetRecentMessages_ReturnsEmpty_WhenNoProjectsDirectory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new MessageService(tempDir, cache);

        // Act
        var messages = await service.GetRecentMessagesAsync("some-session", cancellationToken: cancellationToken);

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentMessages_ReturnsEmpty_WhenNoJsonlFile()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hash123");
        Directory.CreateDirectory(projectDir);

        var service = new MessageService(tempDir, cache);

        // Act
        var messages = await service.GetRecentMessagesAsync("nonexistent-session", cancellationToken: cancellationToken);

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentMessages_ParsesUserAndAssistantMessages()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hash123");
        Directory.CreateDirectory(projectDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = "2026-03-20T10:00:00Z",
                uuid = "uuid-1",
                message = new { role = "user", content = "Hello, can you help me?" },
            }),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = "2026-03-20T10:00:01Z",
                uuid = "uuid-2",
                message = new
                {
                    model = "claude-opus-4-6",
                    role = "assistant",
                    content = new[] { new { type = "text", text = "Sure! I'd be happy to help." } },
                },
            }));

        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-1.jsonl"),
            jsonl,
            cancellationToken);

        var service = new MessageService(tempDir, cache);

        // Act
        var messages = await service.GetRecentMessagesAsync("session-1", cancellationToken: cancellationToken);

        // Assert
        messages.Should().HaveCount(2);
        messages[0].Type.Should().Be("user");
        messages[0].Text.Should().Contain("Hello");
        messages[0].Uuid.Should().Be("uuid-1");
        messages[1].Type.Should().Be("assistant");
        messages[1].Text.Should().Contain("happy to help");
        messages[1].Model.Should().Be("claude-opus-4-6");
    }

    [Fact]
    public async Task GetRecentMessages_ParsesToolUseBlocks()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hash456");
        Directory.CreateDirectory(projectDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = "2026-03-20T10:00:00Z",
                uuid = "uuid-tool",
                message = new
                {
                    model = "claude-opus-4-6",
                    role = "assistant",
                    content = new object[]
                    {
                        new
                        {
                            type = "tool_use",
                            id = "toolu_123",
                            name = "Read",
                            input = new { file_path = "/src/main.cs" },
                        },
                    },
                },
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = "2026-03-20T10:00:01Z",
                uuid = "uuid-result",
                message = new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "tool_result",
                            tool_use_id = "toolu_123",
                            content = "public class Main { }",
                        },
                    },
                },
            }));

        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-tool.jsonl"),
            jsonl,
            cancellationToken);

        var service = new MessageService(tempDir, cache);

        // Act
        var messages = await service.GetRecentMessagesAsync("session-tool", cancellationToken: cancellationToken);

        // Assert
        messages.Should().HaveCount(1);
        var toolUse = messages[0];
        toolUse.Type.Should().Be("tool_use");
        toolUse.ToolName.Should().Be("Read");
        toolUse.ToolUseId.Should().Be("toolu_123");
        toolUse.Text.Should().Contain("/src/main.cs");
        toolUse.ToolResult.Should().Contain("public class Main");
        toolUse.ToolInput.Should().ContainKey("file_path");
    }

    [Fact]
    public async Task GetRecentMessages_RespectsLimit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hash-limit");
        Directory.CreateDirectory(projectDir);

        var lines = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = $"2026-03-20T10:{i:D2}:00Z",
                uuid = $"uuid-{i}",
                message = new { role = "user", content = $"Message number {i}" },
            }));
        }

        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-limit.jsonl"),
            string.Join("\n", lines),
            cancellationToken);

        var service = new MessageService(tempDir, cache);

        // Act
        var messages = await service.GetRecentMessagesAsync("session-limit", 5, cancellationToken);

        // Assert
        messages.Should().HaveCount(5);

        // Should be the last 5 messages
        messages[0].Text.Should().Contain("Message number 15");
        messages[4].Text.Should().Contain("Message number 19");
    }

    [Fact]
    public async Task GetRecentMessages_TruncatesLongText()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hash-trunc");
        Directory.CreateDirectory(projectDir);

        var longText = new string('A', 1000);
        var jsonl = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = "2026-03-20T10:00:00Z",
            uuid = "uuid-long",
            message = new { role = "user", content = longText },
        });

        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-trunc.jsonl"),
            jsonl,
            cancellationToken);

        var service = new MessageService(tempDir, cache);

        // Act
        var messages = await service.GetRecentMessagesAsync("session-trunc", cancellationToken: cancellationToken);

        // Assert
        messages.Should().HaveCount(1);
        messages[0].Text!.Length.Should().BeLessOrEqualTo(503); // 500 + "..."
        messages[0].FullText!.Length.Should().Be(1000);
    }

    [Fact]
    public async Task GetSubagentMessages_ReturnsEmpty_WhenNoFile()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new MessageService(tempDir, cache);

        // Act
        var messages = await service.GetSubagentMessagesAsync(
            "session-1",
            "agent-abc",
            cancellationToken: cancellationToken);

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSubagentMessages_ParsesSubagentTranscript()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash789", "session-sub", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = "2026-03-20T10:00:00Z",
                uuid = "uuid-sub-1",
                message = new { role = "user", content = "Find all test files" },
            }),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = "2026-03-20T10:00:01Z",
                uuid = "uuid-sub-2",
                message = new
                {
                    model = "claude-sonnet-4-6",
                    role = "assistant",
                    content = new[] { new { type = "text", text = "I found 42 test files." } },
                },
            }));

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-abc1234.jsonl"),
            jsonl,
            cancellationToken);

        var service = new MessageService(tempDir, cache);

        // Act
        var messages = await service.GetSubagentMessagesAsync(
            "session-sub",
            "abc1234",
            cancellationToken: cancellationToken);

        // Assert
        messages.Should().HaveCount(2);
        messages[0].Type.Should().Be("user");
        messages[0].Text.Should().Contain("test files");
        messages[1].Type.Should().Be("assistant");
        messages[1].Model.Should().Be("claude-sonnet-4-6");
    }

    [Fact]
    public void ParseJsonlMessages_SkipsFirstLineWhenRequested()
    {
        // Arrange — first line is partial (simulating a tail seek mid-line)
        var content = string.Join(
            "\n",
            "partial invalid json garbage",
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = "2026-03-20T10:00:00Z",
                message = new { role = "user", content = "Valid message" },
            }));

        // Act
        var messages = MessageService.ParseJsonlMessages(content, skipFirstLine: true);

        // Assert
        messages.Should().HaveCount(1);
        messages[0].Text.Should().Contain("Valid message");
    }

    [Fact]
    public void ParseJsonlMessages_SkipsMalformedLines()
    {
        // Arrange
        var content = string.Join(
            "\n",
            "not json at all",
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = "2026-03-20T10:00:00Z",
                message = new { role = "user", content = "Good message" },
            }),
            "{ broken json",
            string.Empty);

        // Act
        var messages = MessageService.ParseJsonlMessages(content, skipFirstLine: false);

        // Assert
        messages.Should().HaveCount(1);
        messages[0].Text.Should().Contain("Good message");
    }

    [Fact]
    public void ParseJsonlMessages_CorrelatesToolResultsWithToolUse()
    {
        // Arrange
        var content = string.Join(
            "\n",
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp = "2026-03-20T10:00:00Z",
                message = new
                {
                    model = "claude-opus-4-6",
                    role = "assistant",
                    content = new object[]
                    {
                        new { type = "tool_use", id = "toolu_abc", name = "Bash", input = new { command = "ls -la" } },
                    },
                },
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = "2026-03-20T10:00:01Z",
                message = new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "tool_result", tool_use_id = "toolu_abc", content = "total 42\ndrwxr-xr-x 5 user group 4096 Mar 20 10:00 ." },
                    },
                },
            }));

        // Act
        var messages = MessageService.ParseJsonlMessages(content, skipFirstLine: false);

        // Assert
        messages.Should().HaveCount(1);
        var toolUse = messages[0];
        toolUse.Type.Should().Be("tool_use");
        toolUse.ToolName.Should().Be("Bash");
        toolUse.ToolResult.Should().Contain("total 42");
    }

    [Fact]
    public void ParseJsonlMessages_BuildsDisplayTextForTools()
    {
        // Arrange
        var content = string.Join(
            "\n",
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                message = new
                {
                    role = "assistant",
                    content = new object[]
                    {
                        new { type = "tool_use", id = "t1", name = "Grep", input = new { pattern = "TODO" } },
                        new { type = "tool_use", id = "t2", name = "Edit", input = new { file_path = "/src/app.cs" } },
                        new { type = "tool_use", id = "t3", name = "WebSearch", input = new { query = "dotnet 10 features" } },
                    },
                },
            }));

        // Act
        var messages = MessageService.ParseJsonlMessages(content, skipFirstLine: false);

        // Assert
        messages.Should().HaveCount(3);
        messages[0].Text.Should().Be("Grep \"TODO\"");
        messages[1].Text.Should().Be("Edit /src/app.cs");
        messages[2].Text.Should().Be("WebSearch \"dotnet 10 features\"");
    }

    [Fact]
    public async Task GetRecentMessages_CachesResults()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hash-cache");
        Directory.CreateDirectory(projectDir);

        var jsonl = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = "2026-03-20T10:00:00Z",
            message = new { role = "user", content = "Cached message" },
        });

        var filePath = Path.Combine(projectDir, "session-cache.jsonl");
        await File.WriteAllTextAsync(filePath, jsonl, cancellationToken);

        var service = new MessageService(tempDir, cache);

        // Act — first call populates cache
        var messages1 = await service.GetRecentMessagesAsync("session-cache", cancellationToken: cancellationToken);

        // Overwrite file with different content (but same mtime due to fast execution)
        await File.WriteAllTextAsync(filePath, jsonl, cancellationToken);

        // Second call should return cached result
        var messages2 = await service.GetRecentMessagesAsync("session-cache", cancellationToken: cancellationToken);

        // Assert
        messages1.Should().HaveCount(1);
        messages2.Should().HaveCount(1);
    }

    [Fact]
    public void ParseJsonlMessages_SkipsNonConversationEntries()
    {
        // Arrange — file-history-snapshot entries should be ignored
        var content = string.Join(
            "\n",
            JsonSerializer.Serialize(new
            {
                type = "file-history-snapshot",
                messageId = "snap-1",
                snapshot = new { messageId = "snap-1" },
            }),
            JsonSerializer.Serialize(new
            {
                type = "user",
                timestamp = "2026-03-20T10:00:00Z",
                message = new { role = "user", content = "Real message" },
            }),
            JsonSerializer.Serialize(new
            {
                type = "custom-title",
                customTitle = "My Title",
            }));

        // Act
        var messages = MessageService.ParseJsonlMessages(content, skipFirstLine: false);

        // Assert — only the user message should be parsed
        messages.Should().HaveCount(1);
        messages[0].Type.Should().Be("user");
    }
}