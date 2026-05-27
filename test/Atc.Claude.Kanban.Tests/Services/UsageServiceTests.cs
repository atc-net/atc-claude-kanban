namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="UsageService"/>.
/// </summary>
public sealed class UsageServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly MemoryCache cache;

    public UsageServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-usage-test-" + Guid.NewGuid().ToString("N"));
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
    public async Task GetUsage_IncludesLeadSessionAndSubagentRows()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectDir = Path.Combine(tempDir, "projects", "hashU");
        Directory.CreateDirectory(projectDir);

        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "session-u.jsonl"),
            JsonSerializer.Serialize(new
            {
                type = "assistant",
                message = new
                {
                    model = "claude-opus-4-6",
                    content = new[] { new { type = "text", text = "lead" } },
                    usage = new { input_tokens = 1000, output_tokens = 500, cache_creation_input_tokens = 100, cache_read_input_tokens = 2000 },
                },
            }),
            cancellationToken);

        var subagentsDir = Path.Combine(projectDir, "session-u", "subagents");
        Directory.CreateDirectory(subagentsDir);
        await File.WriteAllTextAsync(
            Path.Combine(subagentsDir, "agent-sub1.jsonl"),
            string.Join(
                "\n",
                JsonSerializer.Serialize(new { type = "user", slug = "explore", message = new { content = "go" } }),
                JsonSerializer.Serialize(new
                {
                    type = "assistant",
                    message = new
                    {
                        model = "claude-sonnet-4-6",
                        content = new[] { new { type = "text", text = "sub" } },
                        usage = new { input_tokens = 200, output_tokens = 100, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
                    },
                })),
            cancellationToken);

        var service = new UsageService(
            new SessionActivityService(tempDir, cache),
            new SubagentService(tempDir, cache));

        // Act
        var usage = await service.GetUsageAsync("session-u", cancellationToken);

        // Assert
        usage.Should().NotBeNull();
        usage!.ContextTokens.Should().Be(3100); // latest lead turn: 1000 + 2000 + 100
        usage.Rows.Should().HaveCount(2);
        usage.Rows[0].Kind.Should().Be("session");
        usage.Rows[0].Label.Should().Be("Session");
        usage.Rows[0].Model.Should().Be("claude-opus-4-6");

        var agentRow = usage.Rows[1];
        agentRow.Kind.Should().Be("agent");
        agentRow.Model.Should().Be("claude-sonnet-4-6");
        agentRow.TotalTokens.Should().Be(300); // 200 + 100
        usage.TotalCostUsd.Should().BeGreaterThan(0);
    }
}