namespace KnowledgeBase.Agent.Research.Models;

/// <summary>
/// Normalises result URLs so the same page found twice (by another source, through a tracking link or over http) is
/// recognised as seen.
/// </summary>
public static class ResultUrls
{
    private static readonly HashSet<string> TrackingParameters =
        new(["fbclid", "gclid", "mc_cid", "mc_eid", "ref", "ref_src"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The URL with what does not change the page removed: the scheme (http and https are one), a leading <c>www.</c>,
    /// the default port, the fragment, tracking parameters and a trailing slash, with the remaining query parameters
    /// sorted. None for anything that is not an absolute http or https URL.
    /// </summary>
    public static Maybe<string> Normalise(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return Maybe.None;
        }

        string host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host["www.".Length..];
        }

        string port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        string path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : string.Empty;
        string[] parameters =
        [
            .. uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(parameter => !IsTracking(parameter))
                .Order(StringComparer.Ordinal)
        ];
        string query = parameters.Length > 0 ? $"?{string.Join('&', parameters)}" : string.Empty;

        return $"https://{host}{port}{path}{query}";
    }

    /// <summary>The host of a normalised or raw URL, without a leading <c>www.</c>; none when it is not a URL.</summary>
    public static Maybe<string> Host(string url) => Normalise(url).Map(normalised => new Uri(normalised).IdnHost);

    private static bool IsTracking(string parameter)
    {
        string name = Uri.UnescapeDataString(parameter.Split('=', 2)[0]);
        return name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || TrackingParameters.Contains(name);
    }
}
