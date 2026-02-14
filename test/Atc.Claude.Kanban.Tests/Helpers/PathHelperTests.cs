namespace Atc.Claude.Kanban.Tests.Helpers;

/// <summary>
/// Tests for <see cref="Atc.Claude.Kanban.Helpers.PathHelper"/>.
/// </summary>
public sealed class PathHelperTests
{
    [Fact]
    public void IsUnderDirectory_ReturnsTrue_WhenFileIsDirectlyInDirectory()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "test-dir");
        var filePath = Path.Combine(directory, "file.txt");

        // Act
        var result = Kanban.Helpers.PathHelper.IsUnderDirectory(filePath, directory);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsUnderDirectory_ReturnsTrue_WhenFileIsInSubdirectory()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "test-dir");
        var filePath = Path.Combine(directory, "sub", "deep", "file.txt");

        // Act
        var result = Kanban.Helpers.PathHelper.IsUnderDirectory(filePath, directory);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsUnderDirectory_ReturnsFalse_WhenFileIsOutsideDirectory()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "test-dir");
        var filePath = Path.Combine(Path.GetTempPath(), "other-dir", "file.txt");

        // Act
        var result = Kanban.Helpers.PathHelper.IsUnderDirectory(filePath, directory);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsUnderDirectory_ReturnsFalse_ForPathTraversalAttack()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "tasks");
        var maliciousPath = Path.GetFullPath(Path.Combine(directory, "..", "secrets", "credentials.json"));

        // Act
        var result = Kanban.Helpers.PathHelper.IsUnderDirectory(maliciousPath, directory);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsUnderDirectory_ReturnsFalse_WhenDirectoryNameIsPrefix()
    {
        // Arrange — "test-dir-extra" starts with "test-dir" but is a sibling
        var directory = Path.Combine(Path.GetTempPath(), "test-dir");
        var filePath = Path.Combine(Path.GetTempPath(), "test-dir-extra", "file.txt");

        // Act
        var result = Kanban.Helpers.PathHelper.IsUnderDirectory(filePath, directory);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsUnderDirectory_HandlesTrailingDirectorySeparator()
    {
        // Arrange
        var directory = Path.GetTempPath(); // Typically ends with separator
        var filePath = Path.Combine(directory, "file.txt");

        // Act
        var result = Kanban.Helpers.PathHelper.IsUnderDirectory(filePath, directory);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsUnderDirectory_ThrowsArgumentNull_WhenFilePathIsNull()
    {
        // Act
        var act = () => Kanban.Helpers.PathHelper.IsUnderDirectory(null!, Path.GetTempPath());

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("filePath");
    }

    [Fact]
    public void IsUnderDirectory_ThrowsArgumentNull_WhenDirectoryIsNull()
    {
        // Act
        var act = () => Kanban.Helpers.PathHelper.IsUnderDirectory(Path.GetTempPath(), null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("directory");
    }
}