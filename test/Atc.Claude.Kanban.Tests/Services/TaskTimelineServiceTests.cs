namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="TaskTimelineService"/>.
/// </summary>
public sealed class TaskTimelineServiceTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";

    private static readonly DateTime Origin = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    private readonly string tempDir;

    public TaskTimelineServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-timeline-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void BuildTimeline_RecordsStartAndFinish()
    {
        // Arrange
        var content = string.Join(
            "\n",
            Created("1", minute: 0),
            StatusChange("1", "pending", "in_progress", minute: 5),
            StatusChange("1", "in_progress", "completed", minute: 12));

        // Act
        var timeline = TaskTimelineService.BuildTimeline(content);

        // Assert
        var entry = timeline.Should().ContainSingle().Subject;
        entry.TaskId.Should().Be("1");
        entry.CreatedAt.Should().Be(Origin);
        entry.Segments.Should().Equal(new TaskTimelineSegment(Origin.AddMinutes(5), Origin.AddMinutes(12)));
    }

    [Fact]
    public void BuildTimeline_KeepsOneSegmentPerRun_WhenTaskIsReopened()
    {
        // Arrange
        var content = string.Join(
            "\n",
            Created("1", minute: 0),
            StatusChange("1", "pending", "in_progress", minute: 1),
            StatusChange("1", "in_progress", "completed", minute: 4),
            StatusChange("1", "completed", "in_progress", minute: 20),
            StatusChange("1", "in_progress", "completed", minute: 30));

        // Act
        var timeline = TaskTimelineService.BuildTimeline(content);

        // Assert
        timeline.Single().Segments.Should().Equal(
            new TaskTimelineSegment(Origin.AddMinutes(1), Origin.AddMinutes(4)),
            new TaskTimelineSegment(Origin.AddMinutes(20), Origin.AddMinutes(30)));
    }

    [Fact]
    public void BuildTimeline_LeavesSegmentOpen_WhileTaskIsInProgress()
    {
        // Arrange
        var content = string.Join(
            "\n",
            Created("1", minute: 0),
            StatusChange("1", "pending", "in_progress", minute: 3));

        // Act
        var timeline = TaskTimelineService.BuildTimeline(content);

        // Assert
        timeline.Single().Segments.Should().Equal(new TaskTimelineSegment(Origin.AddMinutes(3), End: null));
    }

    [Fact]
    public void BuildTimeline_MarksCompletion_WhenTaskSkippedInProgress()
    {
        // Arrange
        var content = string.Join(
            "\n",
            Created("1", minute: 0),
            StatusChange("1", "pending", "completed", minute: 8));

        // Act
        var timeline = TaskTimelineService.BuildTimeline(content);

        // Assert
        timeline.Single().Segments.Should().Equal(new TaskTimelineSegment(Origin.AddMinutes(8), Origin.AddMinutes(8)));
    }

    [Fact]
    public void BuildTimeline_StartsAfresh_WhenIdIsDeletedAndCreatedAgain()
    {
        // Arrange — the first task #1 ran and was deleted; a new task #1 took its id
        var content = string.Join(
            "\n",
            Created("1", minute: 0),
            StatusChange("1", "pending", "in_progress", minute: 1),
            StatusChange("1", "in_progress", "deleted", minute: 2),
            Created("1", minute: 10));

        // Act
        var timeline = TaskTimelineService.BuildTimeline(content);

        // Assert
        var entry = timeline.Single();
        entry.CreatedAt.Should().Be(Origin.AddMinutes(10));
        entry.Segments.Should().BeEmpty();
    }

    [Fact]
    public void BuildTimeline_StartsAfresh_WhenIdIsCreatedAgainWithoutDelete()
    {
        // Arrange — a resumed session's new list numbers its tasks from 1 again
        var content = string.Join(
            "\n",
            Created("1", minute: 0),
            StatusChange("1", "pending", "in_progress", minute: 1),
            Created("1", minute: 10));

        // Act
        var timeline = TaskTimelineService.BuildTimeline(content);

        // Assert
        timeline.Single().Segments.Should().BeEmpty();
    }

    [Fact]
    public void BuildTimeline_IgnoresFailedUpdatesAndTaskReads()
    {
        // Arrange
        var failed = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = Origin.AddMinutes(2),
            toolUseResult = new { success = false, taskId = "1", updatedFields = Array.Empty<string>(), error = "Task not found", statusChange = new { from = "pending", to = "in_progress" } },
        });
        var read = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = Origin.AddMinutes(3),
            toolUseResult = new { task = new { id = "1", subject = "Task 1", status = "pending" } },
        });
        var content = string.Join("\n", Created("1", minute: 0), failed, read);

        // Act
        var timeline = TaskTimelineService.BuildTimeline(content);

        // Assert
        var entry = timeline.Single();
        entry.CreatedAt.Should().Be(Origin);
        entry.Segments.Should().BeEmpty();
    }

    [Fact]
    public void BuildTimeline_SkipsLinesThatOnlyMentionTheMarkers()
    {
        // Arrange — a tool output quoting a transcript is not a status change
        var quoted = JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = Origin.AddMinutes(1),
            toolUseResult = new { stdout = "\"statusChange\":{\"from\":\"pending\",\"to\":\"in_progress\"}" },
        });
        var content = string.Join("\n", "not json \"statusChange\"", quoted);

        // Act
        var timeline = TaskTimelineService.BuildTimeline(content);

        // Assert
        timeline.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTaskTimelineAsync_ReadsOnlyCompleteLines_AndPicksUpAppendedOnes()
    {
        // Arrange — the transcript ends in a half-written line
        var projectDir = Path.Combine(tempDir, "projects", "D--repo");
        Directory.CreateDirectory(projectDir);
        var jsonlPath = Path.Combine(projectDir, $"{SessionId}.jsonl");
        var startLine = StatusChange("1", "pending", "in_progress", minute: 5);
        var finishLine = StatusChange("1", "in_progress", "completed", minute: 9);
        await File.WriteAllTextAsync(jsonlPath, Created("1", minute: 0) + "\n" + startLine + "\n" + finishLine[..20], TestContext.Current.CancellationToken);
        var service = new TaskTimelineService(tempDir);

        // Act
        var first = await service.GetTaskTimelineAsync(SessionId, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(jsonlPath, Created("1", minute: 0) + "\n" + startLine + "\n" + finishLine + "\n", TestContext.Current.CancellationToken);
        var second = await service.GetTaskTimelineAsync(SessionId, TestContext.Current.CancellationToken);

        // Assert
        first!.Tasks.Single().Segments.Should().Equal(new TaskTimelineSegment(Origin.AddMinutes(5), End: null));
        second!.Tasks.Single().Segments.Should().Equal(new TaskTimelineSegment(Origin.AddMinutes(5), Origin.AddMinutes(9)));
    }

    [Fact]
    public async Task GetTaskTimelineAsync_ReturnsNull_WhenSessionHasNoTranscript()
    {
        // Arrange
        var service = new TaskTimelineService(tempDir);

        // Act
        var timeline = await service.GetTaskTimelineAsync(SessionId, TestContext.Current.CancellationToken);

        // Assert
        timeline.Should().BeNull();
    }

    private static string Created(
        string taskId,
        int minute)
        => JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = Origin.AddMinutes(minute),
            message = new
            {
                content = new[] { new { type = "tool_result", tool_use_id = $"create-{taskId}-{minute}", content = $"Task #{taskId} created successfully" } },
            },
            toolUseResult = new { task = new { id = taskId, subject = $"Task {taskId}" } },
        });

    private static string StatusChange(
        string taskId,
        string from,
        string to,
        int minute)
        => JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = Origin.AddMinutes(minute),
            toolUseResult = new { success = true, taskId, updatedFields = new[] { "status" }, statusChange = new { from, to } },
        });
}