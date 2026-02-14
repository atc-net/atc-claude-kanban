namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="PlanService"/>.
/// </summary>
public sealed class PlanServiceTests : IDisposable
{
    private readonly string tempDir;

    public PlanServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-test-" + Guid.NewGuid().ToString("N"));
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
    public async Task GetPlanForSession_ReturnsNull_WhenNoPlansDirectory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new PlanService(tempDir);

        // Act
        var content = await service.GetPlanForSessionAsync("some-session", cancellationToken);

        // Assert
        content.Should().BeNull();
    }

    [Fact]
    public async Task GetPlanForSession_ReturnsContent_WhenExactMatchExists()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var plansDir = Path.Combine(tempDir, "plans");

        Directory.CreateDirectory(plansDir);

        await File.WriteAllTextAsync(
            Path.Combine(plansDir, "session-abc.md"),
            "# My Plan\n\nSome content here.",
            cancellationToken);

        var service = new PlanService(tempDir);

        // Act
        var content = await service.GetPlanForSessionAsync("session-abc", cancellationToken);

        // Assert
        content.Should().NotBeNull();
        content.Should().Contain("# My Plan");
    }

    [Fact]
    public async Task GetPlanForSession_ReturnsContent_WhenPrefixMatchExists()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var plansDir = Path.Combine(tempDir, "plans");

        Directory.CreateDirectory(plansDir);

        await File.WriteAllTextAsync(
            Path.Combine(plansDir, "session-abc-happy-storm.md"),
            "# Prefixed Plan",
            cancellationToken);

        var service = new PlanService(tempDir);

        // Act
        var content = await service.GetPlanForSessionAsync("session-abc", cancellationToken);

        // Assert
        content.Should().NotBeNull();
        content.Should().Contain("# Prefixed Plan");
    }

    [Fact]
    public async Task GetPlanForSession_ReturnsNull_WhenNoMatchingPlan()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var plansDir = Path.Combine(tempDir, "plans");

        Directory.CreateDirectory(plansDir);

        await File.WriteAllTextAsync(
            Path.Combine(plansDir, "other-session.md"),
            "# Other Plan",
            cancellationToken);

        var service = new PlanService(tempDir);

        // Act
        var content = await service.GetPlanForSessionAsync("session-abc", cancellationToken);

        // Assert
        content.Should().BeNull();
    }

    [Fact]
    public void GetPlanFilePath_ReturnsNull_WhenNoPlansDirectory()
    {
        // Arrange
        var service = new PlanService(tempDir);

        // Act
        var path = service.GetPlanFilePath("session-abc");

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public async Task GetPlanFilePath_ReturnsExactMatch()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var plansDir = Path.Combine(tempDir, "plans");

        Directory.CreateDirectory(plansDir);

        var expectedPath = Path.Combine(plansDir, "session-abc.md");

        await File.WriteAllTextAsync(expectedPath, "# Plan", cancellationToken);

        var service = new PlanService(tempDir);

        // Act
        var path = service.GetPlanFilePath("session-abc");

        // Assert
        path.Should().NotBeNull();
        path.Should().Be(Path.GetFullPath(expectedPath));
    }

    [Fact]
    public async Task GetPlanFilePath_PrefersExactMatchOverPrefix()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var plansDir = Path.Combine(tempDir, "plans");

        Directory.CreateDirectory(plansDir);

        var exactPath = Path.Combine(plansDir, "session-abc.md");
        var prefixPath = Path.Combine(plansDir, "session-abc-extra.md");

        await File.WriteAllTextAsync(exactPath, "# Exact", cancellationToken);
        await File.WriteAllTextAsync(prefixPath, "# Prefix", cancellationToken);

        var service = new PlanService(tempDir);

        // Act
        var path = service.GetPlanFilePath("session-abc");

        // Assert
        path.Should().Be(Path.GetFullPath(exactPath));
    }

    [Fact]
    public void GetPlanFilePath_ReturnsNull_WhenNoMatchingFiles()
    {
        // Arrange
        var plansDir = Path.Combine(tempDir, "plans");

        Directory.CreateDirectory(plansDir);

        var service = new PlanService(tempDir);

        // Act
        var path = service.GetPlanFilePath("session-abc");

        // Assert
        path.Should().BeNull();
    }
}