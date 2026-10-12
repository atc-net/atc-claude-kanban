namespace Atc.Claude.Kanban.Tests.Helpers;

/// <summary>
/// Tests for <see cref="Atc.Claude.Kanban.Helpers.TokenCostCalculator"/>.
/// Expected figures are Anthropic's published list prices per million tokens.
/// </summary>
public sealed class TokenCostCalculatorTests
{
    [Theory]
    [InlineData("claude-fable-5-1", 10.0)]
    [InlineData("claude-mythos-5-1", 10.0)]
    [InlineData("claude-fable-5", 10.0)]
    [InlineData("claude-opus-5-5", 4.0)]
    [InlineData("claude-opus-5", 5.0)]
    [InlineData("claude-opus-4-8", 5.0)]
    [InlineData("claude-opus-4-7", 5.0)]
    [InlineData("claude-opus-4-6", 5.0)]
    [InlineData("claude-opus-4-5", 5.0)]
    [InlineData("claude-opus-4-1-20250805", 15.0)]
    [InlineData("claude-opus-4-20250514", 15.0)]
    [InlineData("claude-sonnet-5-5", 2.0)]
    [InlineData("claude-sonnet-5", 2.0)]
    [InlineData("claude-sonnet-4-6", 3.0)]
    [InlineData("claude-sonnet-4-5-20250929", 3.0)]
    [InlineData("claude-haiku-5-5", 0.10)]
    [InlineData("claude-haiku-4-5-20251001", 1.0)]
    [InlineData("claude-haiku-3-5", 0.80)]
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
    [InlineData("claude-fable-5-1", 50.0)]
    [InlineData("claude-opus-5-5", 20.0)]
    [InlineData("claude-opus-5", 25.0)]
    [InlineData("claude-opus-4-8", 25.0)]
    [InlineData("claude-opus-4-1-20250805", 75.0)]
    [InlineData("claude-sonnet-5-5", 10.0)]
    [InlineData("claude-sonnet-4-6", 15.0)]
    [InlineData("claude-haiku-5-5", 0.50)]
    [InlineData("claude-haiku-4-5", 5.0)]
    [InlineData("claude-haiku-3-5", 4.0)]
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
    [InlineData("claude-fable-5-1", 0.25)]
    [InlineData("claude-fable-5", 1.0)]
    [InlineData("claude-opus-5-5", 0.20)]
    [InlineData("claude-opus-5", 0.50)]
    [InlineData("claude-opus-4-8", 0.50)]
    [InlineData("claude-opus-4-1-20250805", 1.50)]
    [InlineData("claude-sonnet-5-5", 0.10)]
    [InlineData("claude-sonnet-5", 0.20)]
    [InlineData("claude-sonnet-4-6", 0.30)]
    [InlineData("claude-haiku-5-5", 0.01)]
    [InlineData("claude-haiku-4-5", 0.10)]
    [InlineData("claude-haiku-3-5", 0.08)]
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

    [Theory]
    [InlineData("claude-fable-5-1", 12.50)]
    [InlineData("claude-opus-5-5", 5.00)]
    [InlineData("claude-opus-4-8", 6.25)]
    [InlineData("claude-opus-4-1-20250805", 18.75)]
    [InlineData("claude-sonnet-5-5", 2.50)]
    [InlineData("claude-sonnet-4-6", 3.75)]
    [InlineData("claude-haiku-5-5", 0.125)]
    [InlineData("claude-haiku-4-5", 1.25)]
    public void Calculate_PricesCacheWritePerMillion_ByModel(
        string model,
        double expectedCost)
    {
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            inputTokens: 0,
            outputTokens: 0,
            cacheCreationTokens: 1_000_000,
            cacheReadTokens: 0,
            model: model);

        // Assert
        cost.Should().BeApproximately(expectedCost, 1e-9);
    }

    [Theory]
    [InlineData("claude-opus-5-5", 0.05)]
    [InlineData("claude-sonnet-5-5", 0.05)]
    [InlineData("claude-fable-5-1", 0.025)]
    [InlineData("claude-opus-4-8", 0.10)]
    [InlineData("claude-sonnet-4-6", 0.10)]
    [InlineData("claude-haiku-4-5", 0.10)]
    public void Calculate_AppliesTheModelsOwnCacheReadRatio_NotAFixedTenth(
        string model,
        double expectedRatio)
    {
        // Arrange - the newest models discount cache reads further than the older 0.1x, which
        // is why pricing carries an explicit cache-read figure per model.
        var inputCost = Kanban.Helpers.TokenCostCalculator.Calculate(
            1_000_000, 0, 0, 0, model);
        var cacheReadCost = Kanban.Helpers.TokenCostCalculator.Calculate(
            0, 0, 0, 1_000_000, model);

        // Act
        var ratio = cacheReadCost / inputCost;

        // Assert
        ratio.Should().BeApproximately(expectedRatio, 1e-9);
    }

    [Fact]
    public void Calculate_PrefersTheSpecificVersion_OverTheFamily()
    {
        // Arrange - "claude-opus-5-5" contains "opus-5" too, and the two are priced apart.
        var opus55 = Kanban.Helpers.TokenCostCalculator.Calculate(
            1_000_000, 0, 0, 0, "claude-opus-5-5");
        var opus5 = Kanban.Helpers.TokenCostCalculator.Calculate(
            1_000_000, 0, 0, 0, "claude-opus-5");

        // Assert
        opus55.Should().BeApproximately(4.0, 1e-9);
        opus5.Should().BeApproximately(5.0, 1e-9);
    }

    [Fact]
    public void Calculate_ChargesRetiredOpusAtItsOwnHigherPrice()
    {
        // Arrange - Opus 4 and 4.1 cost three times what 4.5 and later do, so treating every
        // Opus alike would overstate a modern session and understate an old one.
        var legacy = Kanban.Helpers.TokenCostCalculator.Calculate(
            1_000_000, 1_000_000, 0, 0, "claude-opus-4-1-20250805");
        var current = Kanban.Helpers.TokenCostCalculator.Calculate(
            1_000_000, 1_000_000, 0, 0, "claude-opus-4-5");

        // Assert
        legacy.Should().BeApproximately(90.0, 1e-9);
        current.Should().BeApproximately(30.0, 1e-9);
    }

    [Theory]
    [InlineData("claude-opus-9-9", 4.0, 20.0)]
    [InlineData("claude-sonnet-9-9", 2.0, 10.0)]
    [InlineData("claude-haiku-9-9", 0.10, 0.50)]
    [InlineData("claude-fable-9-9", 10.0, 50.0)]
    public void Calculate_FallsBackToTheCurrentModelOfAKnownFamily(
        string model,
        double expectedInputCost,
        double expectedOutputCost)
    {
        // Arrange - an id newer than this table still names its family, so the current member
        // of that family is a far better guess than the overall default.
        // Act
        var inputCost = Kanban.Helpers.TokenCostCalculator.Calculate(
            1_000_000, 0, 0, 0, model);
        var outputCost = Kanban.Helpers.TokenCostCalculator.Calculate(
            0, 1_000_000, 0, 0, model);

        // Assert
        inputCost.Should().BeApproximately(expectedInputCost, 1e-9);
        outputCost.Should().BeApproximately(expectedOutputCost, 1e-9);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some-other-vendor-model")]
    public void Calculate_FallsBackToSonnet_WhenTheModelIsUnknown(string? model)
    {
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            inputTokens: 1_000_000,
            outputTokens: 1_000_000,
            cacheCreationTokens: 0,
            cacheReadTokens: 0,
            model: model);

        // Assert
        cost.Should().BeApproximately(12.0, 1e-9);
    }

    [Fact]
    public void Calculate_DoesNotBillAnUnknownOpus4xAtTheRetiredPrice()
    {
        // Arrange - a key of "opus-4" would match any later 4.x id and charge the retired
        // $15/$75 instead of the $5/$25 every Opus since 4.5 has cost.
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            1_000_000, 0, 0, 0, "claude-opus-4-9");

        // Assert
        cost.Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void Calculate_MatchesTheModelIdCaseInsensitively()
    {
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            1_000_000, 0, 0, 0, "CLAUDE-OPUS-5-5");

        // Assert
        cost.Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void Calculate_SumsEveryTokenKind()
    {
        // Arrange - Opus 5.5: 4 input, 5 cache write, 0.20 cache read, 20 output.
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            inputTokens: 1_000_000,
            outputTokens: 1_000_000,
            cacheCreationTokens: 1_000_000,
            cacheReadTokens: 1_000_000,
            model: "claude-opus-5-5");

        // Assert
        cost.Should().BeApproximately(4.0 + 20.0 + 5.0 + 0.20, 1e-9);
    }

    [Fact]
    public void Calculate_ReturnsZero_WhenThereAreNoTokens()
    {
        // Act
        var cost = Kanban.Helpers.TokenCostCalculator.Calculate(
            0, 0, 0, 0, "claude-opus-5-5");

        // Assert
        cost.Should().Be(0);
    }
}