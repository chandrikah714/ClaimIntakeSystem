// ============================================================
// FILE: ClaimIntake.Domain/Models/ClaimDto.cs
// PURPOSE: These are our "data shapes" - like empty forms
//          that we fill in with actual claim information.
//
// BEGINNER TIP: A "DTO" means Data Transfer Object.
// Think of it as an envelope - it holds data as it travels
// from one place to another in our system.
// ============================================================

namespace ClaimIntake.Domain.Models;

/// <summary>
/// Represents a medical claim submitted by a user.
/// This travels from the Web Form → Azure Function → Service Bus → Processor → Database
/// </summary>
public class ClaimDto
{
    // ClaimId: A unique ID we generate automatically for every claim.
    // GUID = "Globally Unique Identifier" - looks like: 3f2504e0-4f89-11d3-9a0c-0305e82c3301
    // No two GUIDs are ever the same - perfect for unique IDs!
    public string ClaimId { get; set; } = Guid.NewGuid().ToString();

    // MemberId: The insurance member's ID number (e.g., "MBR-00123")
    public string MemberId { get; set; } = string.Empty;

    // ProviderId: The doctor or hospital's ID (e.g., "PRV-45678")
    public string ProviderId { get; set; } = string.Empty;

    // DiagnosisCode: ICD-10 medical code (e.g., "A01.1" = Typhoid fever)
    // ICD-10 is the international system doctors use to describe conditions
    public string DiagnosisCode { get; set; } = string.Empty;

    // ClaimAmount: How much money is being claimed (e.g., 1250.00)
    // decimal is used for money - never use double/float for money!
    public decimal ClaimAmount { get; set; }

    // SubmittedBy: The username of who clicked Submit
    public string SubmittedBy { get; set; } = string.Empty;

    // SubmittedAt: When it was submitted (always UTC time zone)
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// This is what we put on the Service Bus AFTER encrypting the claim.
/// The real claim data is hidden inside CipherText - scrambled!
/// Even if someone intercepts this message, they can't read it.
/// </summary>
public class EncryptedPayload
{
    // CipherText: The scrambled (encrypted) claim data.
    // Looks like: "hU3d9Kzm8eN2pL+QR7sV..." - unreadable gibberish!
    public string CipherText { get; set; } = string.Empty;

    // IV = Initialization Vector. A random number used in encryption.
    // IMPORTANT: Every message gets a DIFFERENT IV for security.
    // Using the same IV twice is a security bug!
    public string IV { get; set; } = string.Empty;  // Stored as Base64 string

    // KeyId: Which encryption key was used. Helps during key rotation.
    // Key rotation = changing your encryption key periodically (like changing passwords)
    public string KeyId { get; set; } = "v1";

    // EncryptedAt: When this was encrypted (for auditing)
    public DateTime EncryptedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The result we return to the web app after submitting a claim.
/// Tells the UI whether it worked or not.
/// </summary>
public class ClaimSubmissionResult
{
    public bool Success { get; set; }
    public string? ClaimId { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ValidationErrors { get; set; } = new();

    // Factory methods - shortcuts to create Success or Failure results
    public static ClaimSubmissionResult Ok(string claimId) =>
        new() { Success = true, ClaimId = claimId };

    public static ClaimSubmissionResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };

    public static ClaimSubmissionResult Invalid(List<string> errors) =>
        new() { Success = false, ValidationErrors = errors };
}
