namespace Atc.Claude.Kanban.Tests.Helpers;

/// <summary>
/// Tests for <see cref="Atc.Claude.Kanban.Helpers.TokenCostCalculator"/>.
/// </summary>
public sealed class TokenCostCalculatorTests
{
    [Theory]
    [InlineData("claude-fable-5", 10.0)]
    [InlineData("claude-opus-4-8", 5.0)]
    [InlineData("claude-sonnet-4-6", 3.0)]
    [InlineData("claude-haiku-4-5", 1.0)]
    public void Calculate_PricesInputPerMillion_ByModel(
        string model,
        double expectedCost)
    {
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            inputTokens: 1_000_000,
            outputTokens: 0,
            cacheCreationTokens: 0,
            cacheReadTokens: 0,
            model: model);

        // Assert
        cost.Should().BeApproximately(expectedCost, 1e-9);
    }

    [Theory]
    [InlineData("claude-fable-5", 50.0)]
    [InlineData("claude-opus-4-8", 25.0)]
    [InlineData("claude-sonnet-4-6", 15.0)]
    [InlineData("claude-haiku-4-5", 5.0)]
    public void Calculate_PricesOutputPerMillion_ByModel(
        string model,
        double expectedCost)
    {
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            inputTokens: 0,
            outputTokens: 1_000_000,
            cacheCreationTokens: 0,
            cacheReadTokens: 0,
            model: model);

        // Assert
        cost.Should().BeApproximately(expectedCost, 1e-9);
    }

    [Theory]
    [InlineData("claude-fable-5", 1.0)]
    [InlineData("claude-opus-4-8", 0.5)]
    [InlineData("claude-sonnet-4-6", 0.3)]
    [InlineData("claude-haiku-4-5", 0.1)]
    public void Calculate_PricesCacheReadPerMillion_ByModel(
        string model,
        double expectedCost)
    {
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            inputTokens: 0,
            outputTokens: 0,
            cacheCreationTokens: 0,
            cacheReadTokens: 1_000_000,
            model: model);

        // Assert
        cost.Should().BeApproximately(expectedCost, 1e-9);
    }

    [Fact]
    public void Calculate_FallsBackToSonnet_WhenModelIsNull()
    {
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            inputTokens: 1_000_000,
            outputTokens: 1_000_000,
            cacheCreationTokens: 0,
            cacheReadTokens: 0,
            model: null);

        // Assert
        cost.Should().BeApproximately(18.0, 1e-9);
    }
}