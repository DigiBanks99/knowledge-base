using System.Globalization;

using KnowledgeBase.Agent.Research.Tools;

namespace KnowledgeBase.Agent.Research.Triage;

/// <summary>Whether a screening keeps a result, and why not when it doesn't.</summary>
/// <param name="Reason">Why the result was discarded; null when it was kept.</param>
internal sealed record KeeperDecision(bool Kept, string? Reason);

/// <summary>
/// What the Keeper's probabilities mean. The model only answers; these rules decide, so the thresholds are settings
/// and a discard's reason is written by code, never by the model.
/// </summary>
internal static class KeeperPolicy
{
    /// <summary>The name its calls are recorded under as runs: it is not one of the agents.</summary>
    public const string Agent = "Keeper";

    internal const int QuoteCount = 3;
    internal const int MaxQuoteLength = 300;

    /// <summary>
    /// Kept when useful enough and not likely an injection. An injection is reported first: it says more about the
    /// page than its usefulness does.
    /// </summary>
    public static KeeperDecision Judge(Screening screening, double keepThreshold, double injectionThreshold)
    {
        if (screening.Injection >= injectionThreshold)
        {
            return new KeeperDecision(false, $"Looks like instructions aimed at an AI (injection {Format(screening.Injection)}, at or above {Format(injectionThreshold)}).");
        }

        return screening.Useful >= keepThreshold
            ? new KeeperDecision(true, null)
            : new KeeperDecision(false, $"Not useful enough for the topic (useful {Format(screening.Useful)}, below {Format(keepThreshold)}).");
    }

    /// <summary>
    /// The likeliest paragraphs, at most <see cref="QuoteCount"/>, in order of probability. One the model barely chose
    /// (below <paramref name="floor"/>) is left out, and each is made one line of bounded length.
    /// </summary>
    public static IReadOnlyList<string> Quotes(IReadOnlyDictionary<int, double> probabilities, IReadOnlyList<string> paragraphs, double floor) =>
    [
        .. Chosen(probabilities, paragraphs.Count, floor)
            .Select(number => ScoutTools.Tidy(paragraphs[number - 1], MaxQuoteLength))
    ];

    /// <summary>The run summary of a screening, so the topic's history shows the probabilities behind each verdict.</summary>
    public static string Summarise(Screening screening, KeeperDecision decision) =>
        $"Useful {Format(screening.Useful)}, injection {Format(screening.Injection)}: {(decision.Kept ? "kept" : "discarded")}.";

    /// <summary>The run summary of a description, naming the paragraphs quoted.</summary>
    public static string Summarise(Description description, int paragraphs, double floor)
    {
        int[] quoted = [.. Chosen(description.QuoteProbabilities, paragraphs, floor)];
        string quotes = quoted.Length == 0 ? "no quotes" : $"quotes {string.Join(", ", quoted)}";
        return $"Kind {description.Kind} ({Format(description.KindProbability)}); {quotes}.";
    }

    private static IEnumerable<int> Chosen(IReadOnlyDictionary<int, double> probabilities, int paragraphs, double floor) =>
        probabilities
            .Where(option => option.Value >= floor && option.Key >= 1 && option.Key <= paragraphs)
            .OrderByDescending(option => option.Value)
            .ThenBy(option => option.Key)
            .Take(QuoteCount)
            .Select(option => option.Key);

    private static string Format(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
