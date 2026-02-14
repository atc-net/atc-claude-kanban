namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="TeamService"/>.
/// </summary>
public sealed class TeamServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly MemoryCache cache;
    private readonly JsonSerializerOptions jsonSerializerOptions;

    public TeamServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        cache = new MemoryCache(new MemoryCacheOptions());
        jsonSerializerOptions = JsonSerializerOptionsFactory.Create();
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
    public async Task GetTeamConfig_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new TeamService(tempDir, cache, jsonSerializerOptions);

        // Act
        var config = await service.GetTeamConfigAsync("nonexistent", cancellationToken);

        // Assert
        config.Should().BeNull();
    }

    [Fact]
    public async Task GetTeamConfig_ReadsTeamConfiguration()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var teamDir = Path.Combine(tempDir, "teams", "my-team");
        Directory.CreateDirectory(teamDir);

        var teamConfig = new
        {
            team_name = "my-team",
            description = "Test team",
            members = new[]
            {
                new { name = "researcher", agentId = "abc123", agentType = "general-purpose" },
                new { name = "coder", agentId = "def456", agentType = "general-purpose" },
            },
        };

        await File.WriteAllTextAsync(
            Path.Combine(teamDir, "config.json"),
            JsonSerializer.Serialize(teamConfig),
            cancellationToken);

        var service = new TeamService(tempDir, cache, jsonSerializerOptions);

        // Act
        var config = await service.GetTeamConfigAsync("my-team", cancellationToken);

        // Assert
        config.Should().NotBeNull();
        config!.TeamName.Should().Be("my-team");
        config.Description.Should().Be("Test team");
        config.Members.Should().HaveCount(2);
        config.Members![0].Name.Should().Be("researcher");
        config.Members[1].Name.Should().Be("coder");
    }

    [Fact]
    public async Task GetTeamConfig_ReturnsNull_ForMalformedJson()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var teamDir = Path.Combine(tempDir, "teams", "bad-team");
        Directory.CreateDirectory(teamDir);
        await File.WriteAllTextAsync(
            Path.Combine(teamDir, "config.json"),
            "not valid json {{{",
            cancellationToken);

        var service = new TeamService(tempDir, cache, jsonSerializerOptions);

        // Act
        var config = await service.GetTeamConfigAsync("bad-team", cancellationToken);

        // Assert
        config.Should().BeNull();
    }

    [Fact]
    public async Task GetTeamConfig_UsesCache()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var teamDir = Path.Combine(tempDir, "teams", "cached-team");
        Directory.CreateDirectory(teamDir);

        await File.WriteAllTextAsync(
            Path.Combine(teamDir, "config.json"),
            JsonSerializer.Serialize(new { team_name = "cached-team", description = "Original" }),
            cancellationToken);

        var service = new TeamService(tempDir, cache, jsonSerializerOptions);

        // Act — first call populates cache
        var firstResult = await service.GetTeamConfigAsync("cached-team", cancellationToken);

        // Modify file on disk — cache should still return old value
        await File.WriteAllTextAsync(
            Path.Combine(teamDir, "config.json"),
            JsonSerializer.Serialize(new { team_name = "cached-team", description = "Modified" }),
            cancellationToken);

        var secondResult = await service.GetTeamConfigAsync("cached-team", cancellationToken);

        // Assert
        firstResult!.Description.Should().Be("Original");
        secondResult!.Description.Should().Be("Original");
    }
}