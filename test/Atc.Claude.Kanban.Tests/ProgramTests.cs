namespace Atc.Claude.Kanban.Tests;

/// <summary>
/// Tests for <see cref="Program"/>.
/// </summary>
public sealed class ProgramTests
{
    private const string Home = "C:\\Users\\test";

    [Fact]
    public void ResolveDefaultClaudeDir_ReturnsHomeClaude_WhenNoEnvSet()
    {
        // Act
        var result = Program.ResolveDefaultClaudeDir(configDir: null, claudeDirEnv: null, Home);

        // Assert
        result.Should().Be(Path.Combine(Home, ".claude"));
    }

    [Fact]
    public void ResolveDefaultClaudeDir_PrefersConfigDir_OverClaudeDir()
    {
        // Act
        var result = Program.ResolveDefaultClaudeDir("/data/config", "/data/claude", Home);

        // Assert
        result.Should().Be("/data/config");
    }

    [Fact]
    public void ResolveDefaultClaudeDir_FallsBackToClaudeDir_WhenConfigDirEmpty()
    {
        // Act
        var result = Program.ResolveDefaultClaudeDir(configDir: "", claudeDirEnv: "/data/claude", Home);

        // Assert
        result.Should().Be("/data/claude");
    }

    [Fact]
    public void ResolveDefaultClaudeDir_ExpandsLeadingTilde()
    {
        // Act
        var result = Program.ResolveDefaultClaudeDir("~/.claude-work", claudeDirEnv: null, Home);

        // Assert
        result.Should().Be(Home + "/.claude-work");
    }
}