// ============================================================
// FILE: ClaimIntake.Web/Models/ViewModels.cs
// PURPOSE: ViewModels are special models just for the UI.
//          They include validation rules that show errors on forms.
//
// DIFFERENCE between DTO and ViewModel:
// - ClaimDto: Used to carry data between services (no UI concerns)
// - ViewModel: Used specifically for HTML forms (has UI validation attributes)
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace ClaimIntake.Web.Models;

// ── LOGIN VIEWMODEL ──────────────────────────────────────────────────────────
/// <summary>
/// Data the user types into the Login form.
/// </summary>
public class LoginViewModel
{
    // [Required] means the field can't be left empty
    // ErrorMessage is what shows up in red under the field
    [Required(ErrorMessage = "Please enter your username.")]
    [StringLength(100, MinimumLength = 3,
        ErrorMessage = "Username must be between 3 and 100 characters.")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    // [DataType(DataType.Password)] makes the browser hide the characters with ****
    [Required(ErrorMessage = "Please enter your password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    // This will show a general error like "Invalid username or password"
    // (we don't say which one is wrong - that's a security best practice!)
    public string? ErrorMessage { get; set; }
}

// ── CLAIM FORM VIEWMODEL ─────────────────────────────────────────────────────
/// <summary>
/// Data the user types into the Submit Claim form.
/// </summary>
public class ClaimFormViewModel
{
    [Required(ErrorMessage = "Member ID is required.")]
    [StringLength(50, MinimumLength = 3,
        ErrorMessage = "Member ID must be 3-50 characters.")]
    [RegularExpression(@"^[A-Za-z0-9\-]+$",
        ErrorMessage = "Member ID can only contain letters, numbers, and hyphens.")]
    [Display(Name = "Member ID")]
    public string MemberId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Provider ID is required.")]
    [StringLength(50, MinimumLength = 3,
        ErrorMessage = "Provider ID must be 3-50 characters.")]
    [RegularExpression(@"^[A-Za-z0-9\-]+$",
        ErrorMessage = "Provider ID can only contain letters, numbers, and hyphens.")]
    [Display(Name = "Provider ID")]
    public string ProviderId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Diagnosis Code is required.")]
    [RegularExpression(@"^[A-Za-z]\d{2}(\.\w{1,4})?$",
        ErrorMessage = "Must be a valid ICD-10 code. Examples: A01.1, Z99.89")]
    [Display(Name = "Diagnosis Code (ICD-10)")]
    public string DiagnosisCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Claim Amount is required.")]
    [Range(0.01, 999999.99,
        ErrorMessage = "Claim amount must be between $0.01 and $999,999.99")]
    [DataType(DataType.Currency)]
    [Display(Name = "Claim Amount ($)")]
    public decimal ClaimAmount { get; set; }
}

// ── CONFIRMATION VIEWMODEL ───────────────────────────────────────────────────
/// <summary>
/// Data shown on the "Your claim was submitted!" confirmation page.
/// </summary>
public class ClaimConfirmationViewModel
{
    public string ClaimId { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string MemberId { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;
}
