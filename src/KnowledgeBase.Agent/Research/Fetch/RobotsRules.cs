using System.Text.RegularExpressions;

namespace KnowledgeBase.Agent.Research.Fetch;

/// <summary>
/// The rules of a site's robots.txt that apply to the fetcher (RFC 9309): the groups naming its product token, or the
/// <c>*</c> group when none does. The longest matching rule wins, and Allow wins a tie.
/// </summary>
public sealed class RobotsRules
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly IReadOnlyList<Rule> _rules;

    private RobotsRules(IReadOnlyList<Rule> rules) => _rules = rules;

    /// <summary>No rules: everything may be fetched, as when a site has no robots.txt.</summary>
    public static RobotsRules AllowAll { get; } = new([]);

    /// <summary>Nothing may be fetched, as when a site's robots.txt cannot be reached.</summary>
    public static RobotsRules DisallowAll { get; } = new([new Rule(Allow: false, "/", ToRegex("/"))]);

    public static RobotsRules Parse(string robotsTxt, string productToken)
    {
        List<Rule> named = [];
        List<Rule> wildcard = [];
        List<string> agents = [];
        bool inRules = false;
        bool namesUs = false;
        foreach (string rawLine in robotsTxt.Split('\n'))
        {
            string line = rawLine.Split('#', 2)[0].Trim();
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            string field = line[..colon].Trim().ToLowerInvariant();
            string value = line[(colon + 1)..].Trim();
            switch (field)
            {
                case "user-agent":
                    // A user-agent line after rules starts a new group.
                    if (inRules)
                    {
                        agents.Clear();
                        inRules = false;
                    }

                    agents.Add(value);
                    namesUs |= value.Equals(productToken, StringComparison.OrdinalIgnoreCase);
                    break;
                case "allow" or "disallow":
                    inRules = true;
                    // An empty Disallow allows everything, which is the same as no rule.
                    if (value.Length == 0 || agents.Count == 0)
                    {
                        break;
                    }

                    Rule rule = new(field == "allow", value, ToRegex(value));
                    if (agents.Any(agent => agent.Equals(productToken, StringComparison.OrdinalIgnoreCase)))
                    {
                        named.Add(rule);
                    }

                    if (agents.Contains("*"))
                    {
                        wildcard.Add(rule);
                    }

                    break;
            }
        }

        // A group naming the fetcher replaces the * group even when it has no rules (it then allows everything).
        return new RobotsRules(namesUs ? named : wildcard);
    }

    /// <summary>Whether a page at <paramref name="pathAndQuery"/> (such as <c>/blog/post?id=1</c>) may be fetched.</summary>
    public bool IsAllowed(string pathAndQuery)
    {
        if (pathAndQuery.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Rule? winner = null;
        foreach (Rule rule in _rules)
        {
            if (!Matches(rule, pathAndQuery))
            {
                continue;
            }

            if (winner is null
                || rule.Pattern.Length > winner.Pattern.Length
                || (rule.Pattern.Length == winner.Pattern.Length && rule.Allow))
            {
                winner = rule;
            }
        }

        return winner?.Allow ?? true;
    }

    private static bool Matches(Rule rule, string pathAndQuery)
    {
        try
        {
            return rule.Regex.IsMatch(pathAndQuery);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pattern too costly to match errs on not fetching: a Disallow counts as matching, an Allow does not.
            return !rule.Allow;
        }
    }

    /// <summary><c>*</c> matches any characters and a trailing <c>$</c> anchors the end; the rest is literal.</summary>
    private static Regex ToRegex(string pattern)
    {
        bool anchored = pattern.EndsWith('$');
        string body = anchored ? pattern[..^1] : pattern;
        string expression = "^" + string.Join(".*", body.Split('*').Select(Regex.Escape)) + (anchored ? "$" : string.Empty);
        return new Regex(expression, RegexOptions.CultureInvariant, MatchTimeout);
    }

    private sealed record Rule(bool Allow, string Pattern, Regex Regex);
}
