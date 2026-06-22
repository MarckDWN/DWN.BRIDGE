using System;
using System.Security.Cryptography;
using System.Text;

namespace AIBridge.Services;

/// <summary>
/// Cifra/decifra stringhe sensibili (es. connection string SQL) con DPAPI Windows.
/// Scope: CurrentUser — solo l'utente Windows corrente può decifrare.
/// Le stringhe cifrate sono in Base64. Non vengono mai inviate al server.
/// </summary>
public static class DpapiHelper
{
    private const string DpapiPrefix = "DPAPI:";

    /// <summary>
    /// Cifra una stringa in chiaro con DPAPI (CurrentUser) e restituisce Base64 prefissato.
    /// Restituisce la stringa invariata se è già cifrata o è vuota.
    /// </summary>
    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext) || plaintext.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            return plaintext;

        try
        {
            var cipherBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext),
                null,
                DataProtectionScope.CurrentUser);

            return DpapiPrefix + Convert.ToBase64String(cipherBytes);
        }
        catch
        {
            // Se DPAPI non è disponibile (es. headless CI), restituisce in chiaro
            return plaintext;
        }
    }

    /// <summary>
    /// Decifra una stringa protetta con DPAPI.
    /// Se la stringa non ha il prefisso DPAPI la tratta come già in chiaro (retrocompatibilità).
    /// </summary>
    public static string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext) || !ciphertext.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            return ciphertext;

        try
        {
            var base64 = ciphertext[DpapiPrefix.Length..];
            var plainBytes = ProtectedData.Unprotect(
                Convert.FromBase64String(base64),
                null,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            // Decifrazione fallita (utente diverso, dati corrotti): restituisce vuoto
            return string.Empty;
        }
    }

    /// <summary>True se la stringa è cifrata con DPAPI.</summary>
    public static bool IsProtected(string value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(DpapiPrefix, StringComparison.Ordinal);
}
