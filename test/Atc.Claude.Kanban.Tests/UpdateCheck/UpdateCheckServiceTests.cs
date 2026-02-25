namespace Atc.Claude.Kanban.Tests.UpdateCheck;

/// <summary>
/// Tests for <see cref="UpdateCheckService"/>.
/// </summary>
public sealed class UpdateCheckServiceTests : IDisposable
{
    private readonly string tempDir;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly JsonSerializerOptions ignoreNullSerializerOptions;

    public UpdateCheckServiceTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "atc-kanban-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        jsonSerializerOptions = JsonSerializerOptionsFactory.Create();
        ignoreNullSerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CacheRoundTrip_WriteThenRead_ReturnsCorrectData()
    {
        // Arrange
        var cache = new UpdateCheckCache
        {
            LastCheck = new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero),
            LatestVersion = "1.5.0",
            UpdatePerformed = true,
        };

        var json = JsonSerializer.Serialize(cache, jsonSerializerOptions);

        // Act
        var deserialized = JsonSerializer.Deserialize<UpdateCheckCache>(json, jsonSerializerOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.LastCheck.Should().Be(new DateTimeOffset(2026, 2, 25, 8, 0, 0, TimeSpan.Zero));
        deserialized.LatestVersion.Should().Be("1.5.0");
        deserialized.UpdatePerformed.Should().BeTrue();
    }

    [Fact]
    public void NuGetVersionIndex_DeserializesCorrectly()
    {
        // Arrange
        const string json = """{"versions":["1.1.0","1.2.0","1.3.0-preview.1","1.4.0"]}""";

        // Act
        var index = JsonSerializer.Deserialize<NuGetVersionIndex>(json, jsonSerializerOptions);

        // Assert
        index.Should().NotBeNull();
        index!.Versions.Should().HaveCount(4);
        index.Versions[0].Should().Be("1.1.0");
        index.Versions[3].Should().Be("1.4.0");
    }

    [Fact]
    public void NuGetVersionIndex_FiltersPreRelease()
    {
        // Arrange
        const string json = """{"versions":["1.4.0","1.5.0-preview.1","1.5.0-rc.1","1.5.0"]}""";
        var index = JsonSerializer.Deserialize<NuGetVersionIndex>(json, jsonSerializerOptions);

        // Act
        var stableVersions = index!.Versions
            .Where(v => !v.Contains('-', StringComparison.Ordinal))
            .Select(v => Version.TryParse(v, out var parsed) ? parsed : null)
            .Where(v => v is not null)
            .ToList();

        var latestStable = stableVersions.Max();

        // Assert
        stableVersions.Should().HaveCount(2);
        latestStable.Should().NotBeNull();
        latestStable!.ToString(3).Should().Be("1.5.0");
    }

    [Fact]
    public void SseNotification_VersionUpdate_SerializesCorrectly()
    {
        // Arrange
        var notification = new SseNotification
        {
            Type = "version-update",
            CurrentVersion = "1.4.0",
            LatestVersion = "1.5.0",
        };

        // Act
        var json = JsonSerializer.Serialize(notification, ignoreNullSerializerOptions);

        // Assert
        json.Should().Contain("\"type\":\"version-update\"");
        json.Should().Contain("\"currentVersion\":\"1.4.0\"");
        json.Should().Contain("\"latestVersion\":\"1.5.0\"");
        json.Should().NotContain("\"sessionId\"");
        json.Should().NotContain("\"teamName\"");
    }

    [Fact]
    public void SseNotification_RegularUpdate_OmitsVersionFields()
    {
        // Arrange
        var notification = new SseNotification
        {
            Type = "update",
            SessionId = "session-1",
        };

        // Act
        var json = JsonSerializer.Serialize(notification, ignoreNullSerializerOptions);

        // Assert
        json.Should().Contain("\"type\":\"update\"");
        json.Should().Contain("\"sessionId\":\"session-1\"");
        json.Should().NotContain("\"currentVersion\"");
        json.Should().NotContain("\"latestVersion\"");
    }

    [Fact]
    public void SseClientManager_BroadcastsVersionUpdate()
    {
        // Arrange
        var manager = new SseClientManager();
        var (_, channel) = manager.AddClient();

        // Act
        manager.BroadcastNotification(new SseNotification
        {
            Type = "version-update",
            CurrentVersion = "1.4.0",
            LatestVersion = "1.5.0",
        });

        // Assert
        channel.Reader.TryRead(out var received).Should().BeTrue();
        received!.Type.Should().Be("version-update");
        received.CurrentVersion.Should().Be("1.4.0");
        received.LatestVersion.Should().Be("1.5.0");
    }

    [Fact]
    public async Task MockHttpHandler_ReturnsConfiguredResponse()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"versions":["1.0.0","2.0.0"]}"""),
        });

        using var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync(
            new Uri("https://example.com/test"),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("2.0.0");
    }

    [Fact]
    public async Task UpdateCheckService_HandlesNuGetFailure_Gracefully()
    {
        // Arrange
        using var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var manager = new SseClientManager();

        using var service = new UpdateCheckService(
            client,
            manager,
            jsonSerializerOptions,
            NullLogger<UpdateCheckService>.Instance);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Act — should not throw even with a failing HTTP client
        await service.StartAsync(cts.Token);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert — reaching here without exception means success
        manager.ClientCount.Should().Be(0);
    }

    [Fact]
    public async Task CacheFile_WriteThenRead_Survives()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cacheFilePath = Path.Combine(tempDir, "update-check.json");
        var cache = new UpdateCheckCache
        {
            LastCheck = DateTimeOffset.UtcNow,
            LatestVersion = "2.0.0",
            UpdatePerformed = false,
        };

        // Act
        var json = JsonSerializer.Serialize(cache, jsonSerializerOptions);
        await File.WriteAllTextAsync(cacheFilePath, json, cancellationToken);

        var readJson = await File.ReadAllTextAsync(cacheFilePath, cancellationToken);
        var result = JsonSerializer.Deserialize<UpdateCheckCache>(readJson, jsonSerializerOptions);

        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be("2.0.0");
        result.UpdatePerformed.Should().BeFalse();
    }

    [Fact]
    public void CorruptCacheFile_DeserializesAsNull()
    {
        // Arrange
#pragma warning disable JSON001
        const string corruptJson = "not valid json {{{";
#pragma warning restore JSON001

        // Act
        UpdateCheckCache? result = null;
        try
        {
            result = JsonSerializer.Deserialize<UpdateCheckCache>(corruptJson, jsonSerializerOptions);
        }
        catch (JsonException)
        {
            // Expected — corrupt JSON should throw
        }

        // Assert
        result.Should().BeNull();
    }
}