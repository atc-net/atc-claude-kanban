namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="SubagentService"/>.
/// </summary>
public sealed class SubagentServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly MemoryCache cache;

    public SubagentServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-test-" + Guid.NewGuid().ToString("N"));
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
    public async Task GetSubagents_ReturnsEmpty_WhenNoProjectsDirectory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("some-session", cancellationToken);

        // Assert
        subagents.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSubagents_ReturnsEmpty_WhenNoSubagentsDirectory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hash123", "session-1");
        Directory.CreateDirectory(projectDir);

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-1", cancellationToken);

        // Assert
        subagents.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSubagents_ParsesSubagentFile()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash123", "session-x", "subagents");
        Directory.CreateDirectory(subagentsDir);

        var jsonl = string.Join(
            "\n",
            JsonSerializer.Serialize(new { type = "user", slug = "explore-code", timestamp = "2025-06-01T10:00:00Z", cwd = "/home/user/project", message = new { content = "Find all API endpoints in the codebase" } }),
            JsonSerializer.Serialize(new { type = "assistant", message = new { model = "claude-opus-4-6", content = "I'll search for API endpoints." } }));

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-abc1234.jsonl"),
            jsonl,
            cancellationToken);

        var service = new SubagentService(tempDir, cache);

        // Act
        var subagents = await service.GetSubagentsForSessionAsync("session-x", cancellationToken);

        // Assert
        subagents.Should().HaveCount(1);
        var agent = subagents[0];
        agent.AgentId.Should().Be("abc1234");
        agent.SessionId.Should().Be("session-x");
        agent.Slug.Should().Be("explore-code");
        agent.Model.Should().Be("claude-opus-4-6");
        agent.Cwd.Should().Be("/home/user/project");
        agent.Description.Should().Contain("Find all API endpoints");
    }

    [Fact]
    public void GetSubagentCounts_ReturnsZero_WhenNoProjects()
    {
        // Arrange
        var service = new SubagentService(tempDir, cache);

        // Act
        var (total, active) = service.GetSubagentCounts("some-session");

        // Assert
        total.Should().Be(0);
        active.Should().Be(0);
    }

    [Fact]
    public async Task GetSubagentCounts_CountsFiles()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subagentsDir = Path.Combine(tempDir, "projects", "hash456", "session-y", "subagents");
        Directory.CreateDirectory(subagentsDir);

        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-aaa.jsonl"),
            "{\"type\":\"user\"}",
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-bbb.jsonl"),
            "{\"type\":\"user\"}",
            cancellationToken);

        var service = new SubagentService(tempDir, cache);

        // Act
        var (total, active) = service.GetSubagentCounts("session-y");

        // Assert
        total.Should().Be(2);

        // Both files were just created, so they should be active (within 30s threshold)
        active.Should().Be(2);
    }
}