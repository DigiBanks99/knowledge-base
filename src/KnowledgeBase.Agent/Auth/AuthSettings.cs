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

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
