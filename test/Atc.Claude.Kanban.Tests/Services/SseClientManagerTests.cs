namespace Atc.Claude.Kanban.Tests.Services;

/// <summary>
/// Tests for <see cref="SseClientManager"/>.
/// </summary>
public sealed class SseClientManagerTests
{
    [Fact]
    public void ClientCount_ReturnsZero_WhenNoClientsConnected()
    {
        // Arrange
        var manager = new SseClientManager();

        // Act & Assert
        manager.ClientCount.Should().Be(0);
    }

    [Fact]
    public void AddClient_ReturnsUniqueClientId()
    {
        // Arrange
        var manager = new SseClientManager();

        // Act
        var (id1, _) = manager.AddClient();
        var (id2, _) = manager.AddClient();

        // Assert
        id1.Should().NotBeNullOrEmpty();
        id2.Should().NotBeNullOrEmpty();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void AddClient_IncreasesClientCount()
    {
        // Arrange
        var manager = new SseClientManager();

        // Act
        manager.AddClient();
        manager.AddClient();
        manager.AddClient();

        // Assert
        manager.ClientCount.Should().Be(3);
    }

    [Fact]
    public void RemoveClient_DecreasesClientCount()
    {
        // Arrange
        var manager = new SseClientManager();
        var (id1, _) = manager.AddClient();

        manager.AddClient();

        // Act
        manager.RemoveClient(id1);

        // Assert
        manager.ClientCount.Should().Be(1);
    }

    [Fact]
    public void RemoveClient_CompletesChannel()
    {
        // Arrange
        var manager = new SseClientManager();
        var (clientId, channel) = manager.AddClient();

        // Act
        manager.RemoveClient(clientId);

        // Assert — writing should fail after channel is completed
        channel.Writer.TryWrite(new Contracts.Events.SseNotification
        {
            Type = "update",
        }).Should().BeFalse();
    }

    [Fact]
    public void RemoveClient_DoesNotThrow_ForUnknownClientId()
    {
        // Arrange
        var manager = new SseClientManager();

        // Act
        var act = () => manager.RemoveClient("nonexistent-id");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void BroadcastNotification_SendsToAllClients()
    {
        // Arrange
        var manager = new SseClientManager();
        var (_, channel1) = manager.AddClient();
        var (_, channel2) = manager.AddClient();

        var notification = new Contracts.Events.SseNotification
        {
            Type = "update",
            SessionId = "session-1",
        };

        // Act
        manager.BroadcastNotification(notification);

        // Assert
        channel1.Reader.TryRead(out var received1).Should().BeTrue();
        channel2.Reader.TryRead(out var received2).Should().BeTrue();
        received1!.Type.Should().Be("update");
        received1.SessionId.Should().Be("session-1");
        received2!.Type.Should().Be("update");
    }

    [Fact]
    public void BroadcastNotification_DoesNotThrow_WhenNoClients()
    {
        // Arrange
        var manager = new SseClientManager();
        var notification = new Contracts.Events.SseNotification
        {
            Type = "update",
        };

        // Act
        var act = () => manager.BroadcastNotification(notification);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void BroadcastNotification_DropsOldest_WhenChannelIsFull()
    {
        // Arrange
        var manager = new SseClientManager();
        var (_, channel) = manager.AddClient();

        // Fill the channel (bounded at 100)
        for (var i = 0; i < 100; i++)
        {
            manager.BroadcastNotification(new Contracts.Events.SseNotification
            {
                Type = "update",
                SessionId = $"old-{i}",
            });
        }

        // Act — this should succeed by dropping the oldest
        manager.BroadcastNotification(new Contracts.Events.SseNotification
        {
            Type = "update",
            SessionId = "newest",
        });

        // Assert — drain the channel and verify the newest message is present
        var messages = new List<Contracts.Events.SseNotification>();
        while (channel.Reader.TryRead(out var msg))
        {
            messages.Add(msg);
        }

        messages.Should().HaveCount(100); // Bounded at 100 — oldest was dropped
        messages[^1].SessionId.Should().Be("newest");
        messages[0].SessionId.Should().Be("old-1"); // "old-0" was dropped
    }
}