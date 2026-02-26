namespace C5T8fBtWY.WinFormsMcp.Server.Utilities;

/// <summary>
/// Token estimation utilities for UI tree budget management.
/// Helps prevent responses from exceeding LLM context limits.
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    /// Default characters per token estimate.
    /// Based on typical English text where 1 token ≈ 4 characters.
    /// XML/JSON tends to be slightly more verbose.
    /// </summary>
    public const double CharsPerToken = 4.0;

    /// <summary>
    /// Estimate token count from character count.
    /// </summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <param name="charsPerToken">Characters per token ratio (default 4).</param>
    /// <returns>Estimated token count.</returns>
    public static int EstimateFromCharCount(string? text, int charsPerToken = 4)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (charsPerToken <= 0)
            charsPerToken = 4;

        return (text.Length + charsPerToken - 1) / charsPerToken;
    }

    /// <summary>
    /// Estimate token count from XML content.
    /// XML has higher overhead due to tags, so we use a slightly lower chars/token ratio.
    /// </summary>
    /// <param name="xml">The XML content to estimate tokens for.</param>
    /// <returns>Estimated token count.</returns>
    public static int EstimateFromXml(string? xml)
    {
        if (string.IsNullOrEmpty(xml))
            return 0;

        // XML has more overhead per token due to repetitive tag names
        // Use 3.5 chars per token instead of 4
        return (int)(xml.Length / 3.5);
    }

    /// <summary>
    /// Check if text would exceed a token budget.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <param name="budget">The maximum allowed tokens.</param>
    /// <returns>True if the text would exceed the budget.</returns>
    public static bool ExceedsBudget(string? text, int budget)
    {
        if (budget <= 0)
            return false;

        return EstimateFromCharCount(text) > budget;
    }

    /// <summary>
    /// Estimate token count for a UI element.
    /// Each element typically includes: type, name, bounds, automation ID, and other properties.
    /// </summary>
    /// <returns>Estimated tokens per UI element.</returns>
    public static int EstimatePerElement()
    {
        return Constants.Display.TokensPerElement;
    }

    /// <summary>
    /// Estimate how many UI elements can fit within a token budget.
    /// </summary>
    /// <param name="budget">The token budget.</param>
    /// <param name="headerTokens">Tokens reserved for response header/wrapper.</param>
    /// <returns>Estimated maximum number of elements.</returns>
    public static int MaxElementsForBudget(int budget, int headerTokens = 50)
    {
        int availableBudget = budget - headerTokens;
        if (availableBudget <= 0)
            return 0;

        int tokensPerElement = EstimatePerElement();
        return availableBudget / tokensPerElement;
    }

    /// <summary>
    /// Estimate token count from JSON content.
    /// JSON is slightly more compact than XML.
    /// </summary>
    /// <param name="json">The JSON content to estimate tokens for.</param>
    /// <returns>Estimated token count.</returns>
    public static int EstimateFromJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return 0;

        // JSON is slightly more compact, use standard 4 chars per token
        return EstimateFromCharCount(json, 4);
    }

    /// <summary>
    /// Calculate remaining budget after reserving space for a prefix.
    /// </summary>
    /// <param name="totalBudget">The total token budget.</param>
    /// <param name="prefixText">Text that has already been used.</param>
    /// <returns>Remaining token budget.</returns>
    public static int RemainingBudget(int totalBudget, string? prefixText)
    {
        int used = EstimateFromCharCount(prefixText);
        return System.Math.Max(0, totalBudget - used);
    }
}
