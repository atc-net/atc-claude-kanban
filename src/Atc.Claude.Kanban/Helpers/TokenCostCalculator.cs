namespace Atc.Claude.Kanban.Helpers;

/// <summary>
/// Calculates estimated costs from token usage based on model pricing.
/// Prices are per million tokens.
/// </summary>
public static class TokenCostCalculator
{
    private const double OpusInputPrice = 15.0;
    private const double OpusOutputPrice = 75.0;
    private const double SonnetInputPrice = 3.0;
    private const double SonnetOutputPrice = 15.0;
    private const double HaikuInputPrice = 0.80;
    private const double HaikuOutputPrice = 4.0;
    private const double CacheCreationMultiplier = 0.25;
    private const double CacheReadMultiplier = 0.10;
    private const double PerMillionDivisor = 1_000_000.0;

    /// <summary>
    /// Calculates the estimated cost in USD from token counts and model name.
    /// </summary>
    /// <param name="inputTokens">Total input tokens.</param>
    /// <param name="outputTokens">Total output tokens.</param>
    /// <param name="cacheCreationTokens">Total cache creation input tokens.</param>
    /// <param name="cacheReadTokens">Total cache read input tokens.</param>
    /// <param name="model">The model name (e.g. "claude-opus-4-6").</param>
    /// <returns>Estimated cost in USD.</returns>
    public static double Calculate(
        long inputTokens,
        long outputTokens,
        long cacheCreationTokens,
        long cacheReadTokens,
        string? model)
    {
        var (inputPrice, outputPrice) = GetPricing(model);

        var inputCost = inputTokens * inputPrice / PerMillionDivisor;
        var outputCost = outputTokens * outputPrice / PerMillionDivisor;
        var cacheCreationCost = cacheCreationTokens * inputPrice * CacheCreationMultiplier / PerMillionDivisor;
        var cacheReadCost = cacheReadTokens * inputPrice * CacheReadMultiplier / PerMillionDivisor;

        return inputCost + outputCost + cacheCreationCost + cacheReadCost;
    }

    private static (double Input, double Output) GetPricing(string? model)
    {
        if (string.IsNullOrEmpty(model))
        {
            return (SonnetInputPrice, SonnetOutputPrice);
        }

        if (model.Contains("opus", StringComparison.OrdinalIgnoreCase))
        {
            return (OpusInputPrice, OpusOutputPrice);
        }

        if (model.Contains("haiku", StringComparison.OrdinalIgnoreCase))
        {
            return (HaikuInputPrice, HaikuOutputPrice);
        }

        return (SonnetInputPrice, SonnetOutputPrice);
    }
}