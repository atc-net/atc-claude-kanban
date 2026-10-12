namespace Atc.Claude.Kanban.Tests.EndpointDefinitions;

/// <summary>
/// Tests for <see cref="Atc.Claude.Kanban.EndpointDefinitions.UtilityEndpointDefinition"/>.
/// </summary>
public sealed class UtilityEndpointDefinitionTests
{
    [Fact]
    public void AddEditorArguments_PassesPathAsSingleArgument_WhenPathContainsQuoteAndSpace()
    {
        // Arrange
        var startInfo = new ProcessStartInfo();
        var path = "/tmp/a\" --wait --extensions-dir /evil/b.txt";

        // Act
        UtilityEndpointDefinition.AddEditorArguments(startInfo, path, line: null);

        // Assert
        startInfo.ArgumentList.Should().ContainSingle();
        startInfo.ArgumentList[0].Should().Be(path);
    }

    [Fact]
    public void AddEditorArguments_AppendsLine_WhenLineIsPositive()
    {
        // Arrange
        var startInfo = new ProcessStartInfo();

        // Act
        UtilityEndpointDefinition.AddEditorArguments(startInfo, "/tmp/file.txt", line: 42);

        // Assert
        startInfo.ArgumentList.Should().Equal("-g", "/tmp/file.txt:42");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(null)]
    public void AddEditorArguments_OmitsGotoFlag_WhenLineIsNotPositive(
        int? line)
    {
        // Arrange
        var startInfo = new ProcessStartInfo();

        // Act
        UtilityEndpointDefinition.AddEditorArguments(startInfo, "/tmp/file.txt", line);

        // Assert
        startInfo.ArgumentList.Should().Equal("/tmp/file.txt");
    }

    [Fact]
    public void OpenInEditor_ReturnsBadRequest_WhenPathIsRelative()
    {
        // Act
        var result = UtilityEndpointDefinition.OpenInEditor(new OpenInEditorRequest("relative/file.txt", Line: null));

        // Assert
        result.Result.Should().BeOfType<BadRequest<ErrorResult>>();
    }

    [Fact]
    public void OpenInEditor_ReturnsBadRequest_WhenFileDoesNotExist()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");

        // Act
        var result = UtilityEndpointDefinition.OpenInEditor(new OpenInEditorRequest(missing, Line: null));

        // Assert
        result.Result.Should().BeOfType<BadRequest<ErrorResult>>();
    }

    [Fact]
    public void OpenFolder_ReturnsBadRequest_WhenFolderDoesNotExist()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");

        // Act
        var result = UtilityEndpointDefinition.OpenFolder(new OpenFolderRequest(missing));

        // Assert
        result.Result.Should().BeOfType<BadRequest<ErrorResult>>();
    }

    [Fact]
    public void OpenFolder_ReturnsBadRequest_WhenPathIsAFile()
    {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"file-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "x");

        try
        {
            // Act
            var result = UtilityEndpointDefinition.OpenFolder(new OpenFolderRequest(file));

            // Assert
            result.Result.Should().BeOfType<BadRequest<ErrorResult>>();
        }
        finally
        {
            File.Delete(file);
        }
    }
}