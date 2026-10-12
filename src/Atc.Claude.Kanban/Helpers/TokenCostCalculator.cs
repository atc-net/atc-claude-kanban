namespace Atc.Claude.Kanban.Helpers;

/// <summary>
/// Calculates estimated costs from token usage using Anthropic's published list prices.
/// All prices are US dollars per million tokens.
/// </summary>
public static class TokenCostCalculator
{
    private const double PerMillionDivisor = 1_000_000.0;

    /// <summary>
    /// Published list prices per million tokens, keyed by a fragment of the model id.
    /// </summary>
    /// <remarks>
    /// A cache read is not a fixed fraction of the input price across the lineup. Most models
    /// charge a tenth of input, but Opus 5.5 and Sonnet 5.5 charge a twentieth and Fable 5.1
    /// and Mythos 5.1 charge a fortieth, so every entry carries its own figure instead of
    /// sharing one multiplier.
    /// <para>
    /// Transcripts record cache creation as a single number and never say whether it was a
    /// five-minute or a one-hour write, so the five-minute price is used throughout. A one-hour
    /// write costs roughly twice as much, which is one reason the total reads low against
    /// Anthropic's own usage reporting.
    /// </para>
    /// <para>
    /// Haiku 5.5 is priced in two tiers, by whether a prompt exceeds 100,000 tokens. Cost is
    /// computed from per-model totals rather than per request, so the prompt sizes behind those
    /// totals are not known here and the cheaper tier is used.
    /// </para>
    /// </remarks>
    private static readonly (string Match, ModelPricing Pricing)[] Prices =
    [
        ("fable-5-1", new ModelPricing(10.00, 12.50, 0.25, 50.00)),
        ("mythos-5-1", new ModelPricing(10.00, 12.50, 0.25, 50.00)),
        ("fable-5", new ModelPricing(10.00, 12.50, 1.00, 50.00)),
        ("mythos-5", new ModelPricing(10.00, 12.50, 1.00, 50.00)),

        ("opus-5-5", new ModelPricing(4.00, 5.00, 0.20, 20.00)),
        ("opus-5", new ModelPricing(5.00, 6.25, 0.50, 25.00)),
        ("opus-4-8", new ModelPricing(5.00, 6.25, 0.50, 25.00)),
        ("opus-4-7", new ModelPricing(5.00, 6.25, 0.50, 25.00)),
        ("opus-4-6", new ModelPricing(5.00, 6.25, 0.50, 25.00)),
        ("opus-4-5", new ModelPricing(5.00, 6.25, 0.50, 25.00)),
        ("opus-4-1", new ModelPricing(15.00, 18.75, 1.50, 75.00)),
        ("opus-4-2025", new ModelPricing(15.00, 18.75, 1.50, 75.00)),

        ("sonnet-5-5", new ModelPricing(2.00, 2.50, 0.10, 10.00)),
        ("sonnet-5", new ModelPricing(2.00, 2.50, 0.20, 10.00)),
        ("sonnet-4-6", new ModelPricing(3.00, 3.75, 0.30, 15.00)),
        ("sonnet-4-5", new ModelPricing(3.00, 3.75, 0.30, 15.00)),
        ("sonnet-4", new ModelPricing(3.00, 3.75, 0.30, 15.00)),

        ("haiku-5-5", new ModelPricing(0.10, 0.125, 0.01, 0.50)),
        ("haiku-4-5", new ModelPricing(1.00, 1.25, 0.10, 5.00)),
        ("haiku-3-5", new ModelPricing(0.80, 1.00, 0.08, 4.00)),
    ];

    /// <summary>
    /// Prices used when a model id names a known family but no version in the table, which is
    /// what an id newer than this list looks like. The current member of that family is the
    /// closest available guess.
    /// <para>
    /// This is why the retired Opus 4 is keyed on its dated id rather than on "opus-4": that
    /// shorter key would also swallow any later 4.x id and bill it at three times the price
    /// every Opus since 4.5 has charged.
    /// </para>
    /// </summary>
    private static readonly (string Family, ModelPricing Pricing)[] FamilyFallbacks =
    [
        ("fable", new ModelPricing(10.00, 12.50, 0.25, 50.00)),
        ("mythos", new ModelPricing(10.00, 12.50, 0.25, 50.00)),
        ("opus", new ModelPricing(4.00, 5.00, 0.20, 20.00)),
        ("sonnet", new ModelPricing(2.00, 2.50, 0.10, 10.00)),
        ("haiku", new ModelPricing(0.10, 0.125, 0.01, 0.50)),
    ];

    /// <summary>
    /// Prices used when the model is unknown or absent. Sonnet is the middle of the lineup and
    /// the most likely answer for an id this table does not recognise.
    /// </summary>
    private static readonly ModelPricing UnknownModelPricing = new(2.00, 2.50, 0.10, 10.00);

    /// <summary>
    /// Calculates the estimated cost in USD from token counts and model name.
    /// </summary>
    /// <param name="inputTokens">Total input tokens.</param>
    /// <param name="outputTokens">Total output tokens.</param>
    /// <param name="cacheCreationTokens">Total cache creation input tokens.</param>
    /// <param name="cacheReadTokens">Total cache read input tokens.</param>
    /// <param name="model">The model name (e.g. "claude-opus-5-5").</param>
    /// <returns>Estimated cost in USD.</returns>
    public static double Calculate(
        long inputTokens,
        long outputTokens,
        long cacheCreationTokens,
        long cacheReadTokens,
        string? model)
    {
        var pricing = GetPricing(model);

        var inputCost = inputTokens * pricing.Input / PerMillionDivisor;
        var outputCost = outputTokens * pricing.Output / PerMillionDivisor;
        var cacheCreationCost = cacheCreationTokens * pricing.CacheWrite / PerMillionDivisor;
        var cacheReadCost = cacheReadTokens * pricing.CacheRead / PerMillionDivisor;

        return inputCost + outputCost + cacheCreationCost + cacheReadCost;
    }

    /// <summary>
    /// Resolves a model id to its prices, preferring the most specific match so a version
    /// always wins over the family it belongs to regardless of table order.
    /// </summary>
    /// <param name="model">The model name to resolve.</param>
    /// <returns>The prices to bill this model at.</returns>
    private static ModelPricing GetPricing(string? model)
    {
        if (string.IsNullOrEmpty(model))
        {
            return UnknownModelPricing;
        }

        ModelPricing? best = null;
        var bestLength = 0;

        foreach (var (match, pricing) in Prices)
        {
            if (match.Length > bestLength &&
                model.Contains(match, StringComparison.OrdinalIgnoreCase))
            {
                best = pricing;
                bestLength = match.Length;
            }
        }

        if (best is not null)
        {
            return best;
        }

        foreach (var (family, pricing) in FamilyFallbacks)
        {
            if (model.Contains(family, StringComparison.OrdinalIgnoreCase))
            {
                return pricing;
            }
        }

        return UnknownModelPricing;
    }

    /// <summary>
    /// List prices for one model, in US dollars per million tokens.
    /// </summary>
    /// <param name="Input">Base input price.</param>
    /// <param name="CacheWrite">Five-minute prompt-cache write price.</param>
    /// <param name="CacheRead">Cache hit and refresh price.</param>
    /// <param name="Output">Output price.</param>
    private sealed record ModelPricing(
        double Input,
        double CacheWrite,
        double CacheRead,
        double Output);
}