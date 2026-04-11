// ============================================================
// FILE: ClaimIntake.Domain/Validation/ClaimValidator.cs
// PURPOSE: Checks if a claim has all required valid data
//          before we process it.
//
// BEGINNER ANALOGY: Imagine a security guard at a door.
// The validator is that guard. It checks: "Do you have all the
// right information? Is it in the right format?"
// If anything is wrong, it turns you away with a list of problems.
// ============================================================

using System.Text.RegularExpressions;
using ClaimIntake.Domain.Models;

namespace ClaimIntake.Domain.Validation;

public static class ClaimValidator
{
    // ICD-10 code pattern:
    // - Starts with a LETTER (A-Z)
    // - Followed by exactly 2 DIGITS
    // - Optionally followed by a DOT and more characters
    // Examples of valid codes: A01.1, Z99.89, B99, J06.9
    // Examples of invalid codes: "123", "AB", "cold", ""
    private static readonly Regex Icd10Pattern =
        new(@"^[A-Za-z]\d{2}(\.\w{1,4})?$", RegexOptions.Compiled);

    /// <summary>
    /// Validates all fields of a claim.
    /// Returns: (IsValid: true/false, Errors: list of error messages)
    ///
    /// C# Tuple syntax: (bool, List<string>) means we return two things at once!
    /// </summary>
    public static (bool IsValid, List<string> Errors) Validate(ClaimDto claim)
    {
        // Start with an empty list. We'll add errors as we find them.
        var errors = new List<string>();

        // ── RULE 1: MemberId is required ──────────────────────────────
        // IsNullOrWhiteSpace checks for null, empty string, or just spaces
        if (string.IsNullOrWhiteSpace(claim.MemberId))
            errors.Add("Member ID is required. Example: MBR-001");

        else if (claim.MemberId.Length < 3 || claim.MemberId.Length > 50)
            errors.Add("Member ID must be between 3 and 50 characters.");

        // ── RULE 2: ProviderId is required ───────────────────────────
        if (string.IsNullOrWhiteSpace(claim.ProviderId))
            errors.Add("Provider ID is required. Example: PRV-999");

        else if (claim.ProviderId.Length < 3 || claim.ProviderId.Length > 50)
            errors.Add("Provider ID must be between 3 and 50 characters.");

        // ── RULE 3: DiagnosisCode must be valid ICD-10 format ─────────
        if (string.IsNullOrWhiteSpace(claim.DiagnosisCode))
            errors.Add("Diagnosis Code is required. Example: A01.1");

        else if (!Icd10Pattern.IsMatch(claim.DiagnosisCode))
            errors.Add(
                "Diagnosis Code must be a valid ICD-10 format. " +
                "Examples: A01.1, Z99.89, B99. " +
                "Format: 1 letter + 2 digits + optional .suffix");

        // ── RULE 4: ClaimAmount must be a positive number ─────────────
        if (claim.ClaimAmount <= 0)
            errors.Add("Claim Amount must be greater than $0.00");

        else if (claim.ClaimAmount > 999_999.99m)
            errors.Add("Claim Amount cannot exceed $999,999.99");

        // ── RULE 5: SubmittedBy must be present ───────────────────────
        if (string.IsNullOrWhiteSpace(claim.SubmittedBy))
            errors.Add("SubmittedBy (username) is required.");

        // ── RETURN RESULT ─────────────────────────────────────────────
        // errors.Count == 0 means NO errors were found → IsValid = true
        var isValid = errors.Count == 0;
        return (isValid, errors);
    }

    /// <summary>
    /// Validates just the ICD-10 code format.
    /// Useful for real-time validation in the UI as user types.
    /// </summary>
    public static bool IsValidIcd10(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Icd10Pattern.IsMatch(code);
}
