using System.Globalization;

namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// Domains the author never wants fetched, such as content farms. Blocking a domain blocks its subdomains too. Kept in
/// <c>research/domains.json</c>; the UI to edit it comes later.
/// </summary>
public sealed record BlockedDomains(IReadOnlyList<string> Blocked)
{
    public static BlockedDomains None { get; } = new([]);

    /// <summary>Whether <paramref name="url"/> is on a blocked domain. A string that is not a URL is never blocked.</summary>
    public bool Blocks(string url) => ResultUrls.Host(url)
        .Map(host => Blocked.Any(domain => IsWithin(host, Normalise(domain))))
        .GetValueOrDefault(false);

    /// <summary>
    /// The domain as URL hosts are compared: lowercase, without a leading <c>www.</c> and in its ASCII (punycode) form,
    /// so <c>bücher.example</c> matches a result on <c>xn--bcher-kva.example</c>.
    /// </summary>
    private static string Normalise(string domain)
    {
        string trimmed = domain.Trim().TrimEnd('.');
        try
        {
            trimmed = new IdnMapping().GetAscii(trimmed);
        }
        catch (ArgumentException)
        {
            // Not a valid domain name; compared as written, it simply never matches.
        }

        trimmed = trimmed.ToLowerInvariant();
        return trimmed.StartsWith("www.", StringComparison.Ordinal) ? trimmed["www.".Length..] : trimmed;
    }

    private static bool IsWithin(string host, string domain) =>
        domain.Length > 0
        && (host == domain || host.EndsWith($".{domain}", StringComparison.Ordinal));
}
