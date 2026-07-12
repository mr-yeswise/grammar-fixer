using AdysTech.CredentialManager;

namespace GrammarFixer.Services;

/// <summary>
/// Stores/retrieves the Groq API key via Windows Credential Manager (DPAPI).
/// Key is never written to disk in plaintext.
/// Reference: https://github.com/AdysTech/CredentialManager
/// </summary>
public static class CredentialService
{
    private const string TargetName = "GrammarFixer_GroqApiKey";

    public static void SaveApiKey(string apiKey)
    {
        var cred = new NetworkCredential(TargetName, apiKey);
        CredentialManager.SaveCredentials(TargetName, cred);
    }

    public static string? LoadApiKey()
    {
        try
        {
            var cred = CredentialManager.GetCredentials(TargetName);
            return cred?.Password;
        }
        catch { return null; }
    }

    public static void DeleteApiKey()
    {
        try { CredentialManager.RemoveCredentials(TargetName); }
        catch { /* not stored, ignore */ }
    }
}
