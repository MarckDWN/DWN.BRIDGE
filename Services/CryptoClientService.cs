using AIBridge.Shared.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIBridge.Services;

/// <summary>
/// Servizio di decrittografia lato client WPF.
/// Replica la sola operazione <c>Decrypt</c> + <c>DeriveSessionKey</c> di CryptoService server-side.
/// Il client NON cifra mai (direzione unica: server → client).
/// </summary>
public interface ICryptoClientService
{
    /// <summary>Deriva la session key dal JWT (HKDF-SHA256) — stessa logica del server.</summary>
    byte[] DeriveSessionKey(string jwtToken);

    /// <summary>Decifra AES-GCM e deserializza il payload in <typeparamref name="T"/>.</summary>
    T? Decrypt<T>(string ivBase64, string tagBase64, string ciphertextBase64, byte[] sessionKey);
}

public sealed class CryptoClientService : ICryptoClientService
{
    // Stessa salt usata dal server (ConfigurableHkdfSalt non è disponibile client-side:
    // il client usa il valore concordato per sviluppo. In produzione viene distribuita via config.)
    private const string DefaultSalt = "aibridge-v1-salt-changeme-in-prod";
    private readonly byte[] _hkdfSalt;

    public CryptoClientService(string? hkdfSalt = null)
    {
        _hkdfSalt = Encoding.UTF8.GetBytes(hkdfSalt ?? DefaultSalt);
    }

    //public byte[] DeriveSessionKey(string jwtToken)
    //{
    //    // Deriva 32 byte (256 bit) dallo hash del JWT usando HKDF-SHA256
    //    var inputKey = Encoding.UTF8.GetBytes(jwtToken);
    //    return HKDF.DeriveKey(
    //        hashAlgorithmName: HashAlgorithmName.SHA256,
    //        ikm:               inputKey,
    //        outputLength:      32,
    //        salt:              _hkdfSalt,
    //        info:              null);
    //}
    public byte[] DeriveSessionKey(string jwtToken)
    {
        // 1. Decodifica il JWT sul client ed estrae il claim 'sub'
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadToken(jwtToken) as JwtSecurityToken;

        // Trova il claim 'sub' (Subject)
        var subClaim = jsonToken?.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

        if (string.IsNullOrEmpty(subClaim))
        {
            throw new InvalidOperationException("Il token JWT fornito non contiene un claim 'sub' valido.");
        }

        // 2. Converte in byte SOLO il claim 'sub' (stessa logica del server)
        var inputKey = Encoding.UTF8.GetBytes(subClaim);

        // 3. Genera la chiave simmetrica deterministica
        return HKDF.DeriveKey(
            hashAlgorithmName: HashAlgorithmName.SHA256,
            ikm: inputKey,
            outputLength: 32,
            salt: _hkdfSalt,
            info: null);
    }
    public T? Decrypt<T>(string ivBase64, string tagBase64, string ciphertextBase64, byte[] sessionKey)
    {
        var iv         = Convert.FromBase64String(ivBase64);
        var tag        = Convert.FromBase64String(tagBase64);
        var ciphertext = Convert.FromBase64String(ciphertextBase64);
        var plaintext  = new byte[ciphertext.Length];

        using var aes = new AesGcm(sessionKey, AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(iv, ciphertext, tag, plaintext);

        var json = Encoding.UTF8.GetString(plaintext);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
