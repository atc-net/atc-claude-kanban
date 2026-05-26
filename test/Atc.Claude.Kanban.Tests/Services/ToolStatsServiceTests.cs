namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="ToolStatsService"/>.
/// </summary>
public sealed class ToolStatsServiceTests
{
    [Fact]
    public void BuildToolStats_AggregatesSuccessFailedRejectedAndUnresolved()
    {
        // Arrange — t1 succeeds, t2 fails, t3 is rejected, t4 never gets a result.
        var content = string.Join(
            "\n",
            Assistant("t1", "Bash", new { command = "ls" }),
            UserResult("t1", "ok output", toolUseResult: null),
            Assistant("t2", "Bash", new { command = "bad" }),
            UserResult("t2", "Error: something failed", toolUseResult: null),
            Assistant("t3", "Read", new { file_path = "/x" }),
            UserResult("t3", "denied", toolUseResult: "User rejected this tool use"),
            Assistant("t4", "Bash", new { command = "pending" }));

        // Act
        var stats = ToolStatsService.BuildToolStats("session-1", content);

        // Assert
        stats.TotalCalls.Should().Be(4);
        stats.UniqueTools.Should().Be(2);
        stats.TotalFailed.Should().Be(1);
        stats.TotalRejected.Should().Be(1);

        var bash = stats.Tools.Single(tool => tool.Name == "Bash");
        bash.Count.Should().Be(3);
        bash.Success.Should().Be(1);
        bash.Failed.Should().Be(1);
        bash.Rejected.Should().Be(0);

        var read = stats.Tools.Single(tool => tool.Name == "Read");
        read.Count.Should().Be(1);
        read.Rejected.Should().Be(1);
    }

    private static string Assistant(
        string id,
        string name,
        object input)
        => JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new
            {
                role = "assistant",
                content = new object[] { new { type = "tool_use", id, name, input } },
            },
        });

    private static string UserResult(
        string toolUseId,
        string content,
        string? toolUseResult)
        => JsonSerializer.Serialize(new
        {
            type = "user",
            toolUseResult,
            message = new
            {
                role = "user",
                content = new object[] { new { type = "tool_result", tool_use_id = toolUseId, content } },
            },
        });
}