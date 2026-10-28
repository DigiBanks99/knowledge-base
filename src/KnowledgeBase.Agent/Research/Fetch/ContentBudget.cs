using KnowledgeBase.Agent.Research.Curation;

namespace KnowledgeBase.Agent.Research.Fetch;

/// <summary>A fetched page's content, cut to fit the triage token budget when it did not.</summary>
public sealed record FittedContent(string Text, bool Truncated);

/// <summary>
/// Cuts fetched content to a token budget, counting with the model server's tokenizer so the cut is exact for the
/// model that will read it. When the server cannot count, the conservative estimate is used instead, so an outage
/// or a dry run without a model never stalls fetching.
/// </summary>
internal sealed class ContentBudget(IPromptTokenCounter counter)
{
    // Proportional cuts land close to the budget; a few passes settle what tokenization makes uneven.
    private const int Passes = 3;
    private const double Margin = 0.95;
    private const int MaxCharactersPerToken = 8;

    private static readonly EstimatedTokenCounter Estimate = new();

    public async Task<FittedContent> FitAsync(string text, int tokenBudget, CancellationToken cancellationToken)
    {
        // No tokenizer packs more than this many characters into a token, so anything past it is cut before counting
        // rather than sent to the model server a few times over.
        string fitted = Cut(text, tokenBudget * MaxCharactersPerToken);
        bool cut = fitted.Length < text.Length;

        IPromptTokenCounter current = counter;
        int tokens = await CountAsync(fitted);
        if (tokens <= tokenBudget)
        {
            return new FittedContent(fitted, cut);
        }

        for (int pass = 0; pass < Passes && tokens > tokenBudget; pass++)
        {
            int target = (int)(fitted.Length * (double)tokenBudget / tokens * Margin);
            fitted = Cut(fitted, Math.Max(0, target));
            tokens = await CountAsync(fitted);
        }

        // Whatever the passes left over budget is cut to one character a token, which fits even dense text (code, CJK)
        // whatever the tokenizer.
        return new FittedContent(tokens <= tokenBudget ? fitted : Cut(fitted, tokenBudget), true);

        async Task<int> CountAsync(string candidate)
        {
            Result<int, Error> counted = await current.CountAsync(candidate, cancellationToken);
            if (counted.IsSuccess)
            {
                return counted.Value;
            }

            current = Estimate;
            return (await Estimate.CountAsync(candidate, cancellationToken)).Value;
        }
    }

    /// <summary>
    /// <paramref name="text"/> cut to at most <paramref name="length"/> characters, at the last paragraph break when
    /// one is in the second half, else at the last whitespace, so the cut never splits a word.
    /// </summary>
    private static string Cut(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        string head = text[..length];
        int paragraph = head.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (paragraph >= length / 2)
        {
            return head[..paragraph].TrimEnd();
        }

        int space = head.LastIndexOfAny([' ', '\n', '\t']);
        return (space > 0 ? head[..space] : head).TrimEnd();
    }
}
