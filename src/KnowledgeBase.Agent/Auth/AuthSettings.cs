using System.Security.Claims;

namespace KnowledgeBase.Agent.Auth;

/// <summary>
/// Sign-in with Microsoft Entra ID. The app signs the user in itself (backend for frontend): the session lives in a
/// secure, HTTP-only cookie and no tokens reach the browser.
/// </summary>
public sealed class AuthSettings
{
    public const string SectionName = "Authentication";

    /// <summary>The Entra tenant (directory) id of the app registration.</summary>
    public string? TenantId { get; init; }

    /// <summary>The application (client) id of the app registration.</summary>
    public string? ClientId { get; init; }

    /// <summary>A client secret of the app registration.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Optional object ids of the users allowed in. Entra already refuses users who are not assigned to the app when
    /// "Assignment required" is on; this is a second check inside the app.
    /// </summary>
    public string[] AllowedObjectIds { get; init; } = [];

    /// <summary>
    /// The app registration's Application ID URI (Expose an API), when it is not the default <c>api://{ClientId}</c>,
    /// such as <c>api://knowledgebase.example.com</c>. Blank means the default.
    /// </summary>
    public string? ApplicationIdUri { get; init; }

    /// <summary>The delegated scope Claude asks for when it signs in for the connector, exposed under the Application ID URI.</summary>
    public string McpScope { get; init; } = "access_as_user";

    /// <summary>The Application ID URI without a trailing slash.</summary>
    public string ResolvedApplicationIdUri =>
        string.IsNullOrWhiteSpace(ApplicationIdUri) ? $"api://{ClientId}" : ApplicationIdUri.Trim().TrimEnd('/');

    /// <summary>The full scope Claude requests, such as <c>api://{ClientId}/access_as_user</c>.</summary>
    public string McpScopeUri => $"{ResolvedApplicationIdUri}/{McpScope}";

    /// <summary>Whether the signed-in user may use the app: anyone when no allow-list is set, otherwise only the listed object ids.</summary>
    public bool IsAllowed(ClaimsPrincipal user) =>
        AllowedObjectIds.Length == 0
        || AllowedObjectIds.Contains(
            user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier") ?? user.FindFirstValue("oid"),
            StringComparer.OrdinalIgnoreCase);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
