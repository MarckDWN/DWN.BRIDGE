using System.Management;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace AIBridge.Services;

/// <summary>
/// Calcola l'Hardware ID della macchina corrente.
/// Algoritmo: SHA-256( UserSID + MotherboardSerial ).
/// Fallback se serial non disponibile: SHA-256( UserSID + MachineGuid da Registry ).
/// 
/// Proprietà di privacy:
/// - Non contiene nome utente, email, IP o qualsiasi PII.
/// - Deterministico: stessa macchina + stesso utente Windows = stesso HWID.
/// - Risultato: stringa hex lowercase di 64 caratteri.
/// - Non viene mai persistito su disco.
/// </summary>
public interface IHwidService
{
    /// <summary>Calcola (o restituisce dalla cache) l'HWID della macchina.</summary>
    string ComputeHwid();
}

public sealed class HwidService : IHwidService
{
    private string? _cached;

    public string ComputeHwid()
    {
        if (_cached is not null) return _cached;

        var sid    = GetUserSid();
        var serial = GetMotherboardSerial() ?? GetMachineGuid() ?? "fallback";

        var raw   = $"{sid}|{serial}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        _cached   = Convert.ToHexString(bytes).ToLowerInvariant();
        return _cached;
    }

    private static string GetUserSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value ?? "UNKNOWN_SID";
        }
        catch
        {
            return "UNKNOWN_SID";
        }
    }

    private static string? GetMotherboardSerial()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SerialNumber FROM Win32_BaseBoard");
            foreach (ManagementObject obj in searcher.Get())
            {
                var serial = obj["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(serial) &&
                    serial != "To be filled by O.E.M." &&
                    serial != "None" &&
                    serial.Length > 4)
                    return serial;
            }
        }
        catch { /* WMI non disponibile */ }
        return null;
    }

    private static string? GetMachineGuid()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                "MachineGuid",
                null)?.ToString();
        }
        catch { return null; }
    }
}
