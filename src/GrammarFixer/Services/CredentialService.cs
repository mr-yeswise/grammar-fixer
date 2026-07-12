using System.Net;
using AdysTech.CredentialManager;

namespace GrammarFixer.Services;

/// <summary>
/// Stores/retrieves the Groq API key via Windows Credential Manager (DPAPI).
/// Key is never written to disk in plaintext.
/// Reference: https://github.com/AdysTech/CredentialManager
/// NetworkCredential lives in System.Net (no extra NuGet needed on .NET 8).
/// </summary>
public static class CredentialService
{
    private const string TargetName = "GrammarFixer_GroqApiKey";

    public static void SaveApiKey(string apiKey)
    {
        // Username field is unused; we store the key in the Password slot.
        var cred = new NetworkCredential("grammerfixer_user", apiKey);
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
