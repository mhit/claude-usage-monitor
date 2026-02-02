using CredentialManagement;

namespace ClaudeUsageMonitor.Services;

/// <summary>
/// Manages credentials using Windows Credential Manager
/// </summary>
public class CredentialService
{
    private const string SessionKeyTarget = "ClaudeUsageMonitor:SessionKey";
    private const string OrgIdTarget = "ClaudeUsageMonitor:OrganizationId";

    /// <summary>
    /// Save session key to Windows Credential Manager
    /// </summary>
    public void SaveSessionKey(string sessionKey)
    {
        using var credential = new Credential
        {
            Target = SessionKeyTarget,
            Password = sessionKey,
            PersistanceType = PersistanceType.LocalComputer
        };
        credential.Save();
    }

    /// <summary>
    /// Get session key from Windows Credential Manager
    /// </summary>
    public string? GetSessionKey()
    {
        using var credential = new Credential { Target = SessionKeyTarget };
        if (credential.Load())
            return credential.Password;
        return null;
    }

    /// <summary>
    /// Delete session key from Windows Credential Manager
    /// </summary>
    public void DeleteSessionKey()
    {
        using var credential = new Credential { Target = SessionKeyTarget };
        credential.Delete();
    }

    /// <summary>
    /// Save organization ID
    /// </summary>
    public void SaveOrganizationId(string orgId)
    {
        using var credential = new Credential
        {
            Target = OrgIdTarget,
            Password = orgId,
            PersistanceType = PersistanceType.LocalComputer
        };
        credential.Save();
    }

    /// <summary>
    /// Get organization ID
    /// </summary>
    public string? GetOrganizationId()
    {
        using var credential = new Credential { Target = OrgIdTarget };
        if (credential.Load())
            return credential.Password;
        return null;
    }

    /// <summary>
    /// Delete organization ID
    /// </summary>
    public void DeleteOrganizationId()
    {
        using var credential = new Credential { Target = OrgIdTarget };
        credential.Delete();
    }

    /// <summary>
    /// Check if credentials are configured
    /// </summary>
    public bool HasCredentials()
    {
        return !string.IsNullOrEmpty(GetSessionKey()) && 
               !string.IsNullOrEmpty(GetOrganizationId());
    }

    /// <summary>
    /// Clear all stored credentials
    /// </summary>
    public void ClearAll()
    {
        DeleteSessionKey();
        DeleteOrganizationId();
    }
}
