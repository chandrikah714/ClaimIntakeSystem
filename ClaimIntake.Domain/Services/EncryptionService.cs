// ============================================================
// FILE: ClaimIntake.Domain/Services/EncryptionService.cs
// PURPOSE: Encrypts and decrypts claim data using AES-256
//
// BEGINNER EXPLANATION:
// Imagine you have a secret message.
// You put it in a locked box (encrypt).
// Only someone with the same key can open it (decrypt).
// AES-256 is one of the strongest locks available!
// The "256" means the key is 256 bits (32 bytes) long.
// ============================================================

using ClaimIntake.Domain.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaimIntake.Domain.Services;

// This "interface" defines WHAT our encryption service can do.
// Think of it as a job description: "this person must be able to Encrypt and Decrypt"
// We use interfaces so we can swap implementations (e.g., for testing)
public interface IEncryptionService
{
    EncryptedPayload Encrypt(ClaimDto claim);
    ClaimDto Decrypt(EncryptedPayload payload);
}

/// <summary>
/// Implements AES-256 encryption.
/// AES = Advanced Encryption Standard
/// 256 = Key size in bits (very strong!)
/// CBC mode with fresh IV per message.
/// </summary>
public class AesEncryptionService : IEncryptionService
{
    // _key is our secret encryption key
    // The underscore prefix is a C# convention for private fields
    private readonly byte[] _key;

    // Constructor: called when we create a new AesEncryptionService
    // base64Key: our 256-bit key stored as a Base64 string
    public AesEncryptionService(string base64Key)
    {
        // Validate the key before storing it
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new ArgumentException("Encryption key cannot be empty!");

        _key = Convert.FromBase64String(base64Key);

        // AES-256 needs exactly 32 bytes (256 bits)
        // If the key is wrong size, fail immediately
        if (_key.Length != 32)
            throw new ArgumentException(
                $"Key must be 256-bit (32 bytes). Got {_key.Length} bytes.");
    }

    /// <summary>
    /// ENCRYPT: Takes a ClaimDto, scrambles it, returns EncryptedPayload
    ///
    /// HOW IT WORKS (simplified):
    /// 1. Convert the claim to JSON text
    /// 2. Convert JSON text to bytes
    /// 3. Generate a random IV (new one every time!)
    /// 4. Use AES to scramble the bytes with our key + IV
    /// 5. Convert scrambled bytes to Base64 string (safe to store/send)
    /// </summary>
    public EncryptedPayload Encrypt(ClaimDto claim)
    {
        // Step 1: Turn the claim object into a JSON string
        // Example: {"ClaimId":"abc-123","MemberId":"MBR-001",...}
        var jsonString = JsonSerializer.Serialize(claim);

        // Step 2: Convert the text to bytes (computers work with bytes, not text)
        var plaintextBytes = Encoding.UTF8.GetBytes(jsonString);

        // Step 3: Create the AES encryptor
        using var aes = Aes.Create();
        aes.Key = _key;           // Set our secret key
        aes.GenerateIV();         // ← IMPORTANT: Fresh random IV every time!
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Step 4: Actually encrypt the bytes
        using var encryptor = aes.CreateEncryptor();
        using var memoryStream = new MemoryStream();
        using (var cryptoStream = new CryptoStream(
            memoryStream, encryptor, CryptoStreamMode.Write))
        {
            cryptoStream.Write(plaintextBytes, 0, plaintextBytes.Length);
            // The using block automatically calls FlushFinalBlock()
        }

        // Step 5: Get the encrypted bytes and convert to Base64 for safe storage
        var encryptedBytes = memoryStream.ToArray();

        return new EncryptedPayload
        {
            // Convert bytes → Base64 string (safe to put in JSON/queue messages)
            CipherText = Convert.ToBase64String(encryptedBytes),
            IV = Convert.ToBase64String(aes.IV),  // Store IV with the message
            KeyId = "v1"  // Track which key version was used
        };
    }

    /// <summary>
    /// DECRYPT: Takes an EncryptedPayload, unscrambles it, returns ClaimDto
    ///
    /// HOW IT WORKS:
    /// 1. Convert Base64 strings back to bytes
    /// 2. Use AES with same key + same IV to unscramble
    /// 3. Convert bytes back to JSON text
    /// 4. Parse JSON back to a ClaimDto object
    /// </summary>
    public ClaimDto Decrypt(EncryptedPayload payload)
    {
        // Step 1: Convert Base64 strings back to byte arrays
        var cipherBytes = Convert.FromBase64String(payload.CipherText);
        var iv = Convert.FromBase64String(payload.IV);

        // Step 2: Set up AES decryptor with same key and the message's IV
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = iv;           // Must use the SAME IV that was used to encrypt!
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Step 3: Decrypt the bytes
        using var decryptor = aes.CreateDecryptor();
        using var memoryStream = new MemoryStream(cipherBytes);
        using var cryptoStream = new CryptoStream(
            memoryStream, decryptor, CryptoStreamMode.Read);
        using var reader = new StreamReader(cryptoStream, Encoding.UTF8);

        // Step 4: Read the decrypted JSON and deserialize back to ClaimDto
        var jsonString = reader.ReadToEnd();

        return JsonSerializer.Deserialize<ClaimDto>(jsonString)
            ?? throw new InvalidOperationException("Decryption produced null result!");
    }
}

/// <summary>
/// Helper class to generate and manage encryption keys.
/// RUN THIS ONCE to generate your key, then store it in Azure Key Vault.
/// </summary>
public static class EncryptionKeyHelper
{
    /// <summary>
    /// Generates a new random 256-bit AES key.
    /// Copy the output and store it in your Azure Key Vault or appsettings.
    /// </summary>
    public static string GenerateNewKey()
    {
        var keyBytes = new byte[32];  // 32 bytes = 256 bits
        RandomNumberGenerator.Fill(keyBytes);  // Cryptographically secure random!
        return Convert.ToBase64String(keyBytes);
    }

    // USAGE: Call this from a console app ONCE:
    // Console.WriteLine(EncryptionKeyHelper.GenerateNewKey());
    // Example output: "K7mP2xR9vL3nQ6sT8wE1yU4iO0aD5fH="
    // → Copy that and put it in Azure Key Vault
}
