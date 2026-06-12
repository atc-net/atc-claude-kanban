namespace Atc.Claude.Kanban.Tests.Helpers;

/// <summary>
/// Tests for <see cref="TaskNotificationParser"/>, including the adversarial case
/// where an agent's result text embeds a fake &lt;usage&gt; block.
/// </summary>
public sealed class TaskNotificationParserTests
{
    private const string CleanNotification = """
        <task-notification>
        <task-id>task-0001</task-id>
        <tool-use-id>toolu_sample0001</tool-use-id>
        <output-file>C:\temp\tasks\task-0001.output</output-file>
        <status>completed</status>
        <summary>Agent "Emulate work A" completed</summary>
        <result>test agent A done

        Summary of read-only busy-work (no files modified):
        - ran a version check
        - Total active time ~90s via spaced waits.</result>
        <usage><subagent_tokens>22316</subagent_tokens><tool_uses>6</tool_uses><duration_ms>118942</duration_ms></usage>
        </task-notification>
        """;

    // The result body itself describes the envelope, embedding a literal </result>
    // and a fake <usage> block (111/2/3). The real usage (22316/6/118942) trails it.
    private const string AdversarialNotification = """
        <task-notification>
        <task-id>task-0002</task-id>
        <tool-use-id>toolu_sample0002</tool-use-id>
        <output-file>C:\temp\tasks\task-0002.output</output-file>
        <status>completed</status>
        <summary>Agent "format explainer" completed</summary>
        <result>I documented the envelope. It wraps the reply in <result>...</result> and ends
        with a <usage><subagent_tokens>111</subagent_tokens><tool_uses>2</tool_uses><duration_ms>3</duration_ms></usage>
        block — those numbers above are an EXAMPLE I typed, not the real ones.</result>
        <usage><subagent_tokens>22316</subagent_tokens><tool_uses>6</tool_uses><duration_ms>118942</duration_ms></usage>
        </task-notification>
        """;

    [Fact]
    public void Parse_ReturnsNull_WhenNotATaskNotification()
    {
        TaskNotificationParser.Parse("just a normal user message").Should().BeNull();
    }

    [Fact]
    public void Parse_ExtractsMetadataResultAndUsage_FromCleanNotification()
    {
        var info = TaskNotificationParser.Parse(CleanNotification);

        info.Should().NotBeNull();
        info!.Summary.Should().Be("Agent \"Emulate work A\" completed");
        info.Status.Should().Be("completed");
        info.Result.Should().Contain("test agent A done");
        info.Result.Should().Contain("Total active time ~90s");
        info.SubagentTokens.Should().Be(22316);
        info.ToolUses.Should().Be(6);
        info.DurationMs.Should().Be(118942);
    }

    [Fact]
    public void Parse_PicksRealTrailingUsage_NotEmbeddedExample()
    {
        var info = TaskNotificationParser.Parse(AdversarialNotification);

        info.Should().NotBeNull();

        // The real trailing <usage> must win over the fake one embedded in <result>.
        info!.SubagentTokens.Should().Be(22316);
        info.ToolUses.Should().Be(6);
        info.DurationMs.Should().Be(118942);

        // The result body keeps the agent's full prose, including the embedded example.
        info.Result.Should().Contain("those numbers above are an EXAMPLE");
    }

    [Fact]
    public void FormatUsage_ProducesCompactSuffix()
    {
        var info = TaskNotificationParser.Parse(CleanNotification);

        TaskNotificationParser.FormatUsage(info!).Should().Be(" · 22.3k tok · 6 tools · 119s");
    }

    [Fact]
    public void FormatUsage_ReturnsEmpty_WhenNoUsage()
    {
        var info = new TaskNotificationInfo(
            Summary: "x",
            Status: "completed",
            Result: null,
            SubagentTokens: null,
            ToolUses: null,
            DurationMs: null);

        TaskNotificationParser.FormatUsage(info).Should().BeEmpty();
    }
}