namespace KnowledgeBase.Agent.Research.Triage;

/// <summary>What the Keeper reads about one fetched result.</summary>
/// <param name="Topic">The topic's brief, or its path when it has none.</param>
/// <param name="Paragraphs">The page's prose paragraphs, sent numbered from 1.</param>
/// <param name="QuoteOptions">How many of the first paragraphs a quote may be chosen from.</param>
internal sealed record KeeperPage(string Topic, string Title, string Url, IReadOnlyList<string> Paragraphs, int QuoteOptions);

/// <summary>The first call's answers, from which code decides to keep or discard.</summary>
internal sealed record Screening(double Useful, double Injection);

/// <summary>The second call's answers, asked only for a kept result.</summary>
/// <param name="QuoteProbabilities">The probability of each offered paragraph, by its number; empty when none were offered.</param>
internal sealed record Description(string Kind, double KindProbability, IReadOnlyDictionary<int, double> QuoteProbabilities);

/// <summary>
/// The decision model that triages fetched results. It answers questions with probabilities rather than writing text,
/// so it is not an agent and has no tools: code decides what its answers mean.
/// </summary>
internal interface IKeeper
{
    /// <summary>How useful the result is for the topic, and how likely it is to hold instructions aimed at an AI.</summary>
    Task<Result<Screening, Error>> ScreenAsync(KeeperPage page, CancellationToken cancellationToken);

    /// <summary>What kind of content the result is, and which paragraphs best show why it matters.</summary>
    Task<Result<Description, Error>> DescribeAsync(KeeperPage page, CancellationToken cancellationToken);
}

/// <summary>A Keeper call failed or timed out, so the result waits for the next cycle.</summary>
internal sealed class KeeperCallFailedError(string reason)
    : Error("KeeperCallFailed", $"The Keeper could not answer: {reason}");
