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

// ============================================================
// FILE: ClaimIntake.Web/Models/EnhancedViewModels.cs
// PURPOSE: ViewModels for new admin and user features
// ============================================================

// ── ADMIN DASHBOARD ──────────────────────────────────────────────────────────
public class AdminDashboardViewModel
{
    public int TotalClaims { get; set; }
    public int PendingClaims { get; set; }
    public int ApprovedClaims { get; set; }
    public int RejectedClaims { get; set; }
    public decimal TotalAmount { get; set; }
    public int ActiveUsers { get; set; }

    public decimal ApprovalRate => TotalClaims > 0 ?
        (decimal)ApprovedClaims / TotalClaims * 100 : 0;

    public decimal AverageClaimAmount => TotalClaims > 0 ?
        TotalAmount / TotalClaims : 0;
}

// ── ALL CLAIMS VIEW ──────────────────────────────────────────────────────────
public class AllClaimsViewModel
{
    public List<ClaimSummaryViewModel> Claims { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? StatusFilter { get; set; }
    public string? SearchTerm { get; set; }
}

public class ClaimSummaryViewModel
{
    public string ClaimId { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime SubmittedAt { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;

    public string StatusBadgeClass => Status switch
    {
        "Approved" => "status-approved",
        "Rejected" => "status-rejected",
        "Pending" => "status-pending",
        _ => "status-default"
    };

    public string StatusIcon => Status switch
    {
        "Approved" => "✓",
        "Rejected" => "✕",
        "Pending" => "⧖",
        _ => "—"
    };
}

// ── ADMIN CLAIM DETAIL ───────────────────────────────────────────────────────
public class AdminClaimDetailViewModel
{
    public string ClaimId { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string DiagnosisCode { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime SubmittedAt { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
    public List<StatusHistoryViewModel> StatusHistory { get; set; } = new();

    public int DaysInSystem => (DateTime.UtcNow - SubmittedAt).Days;
    public string DaysLabel => DaysInSystem == 0 ? "Today" :
        DaysInSystem == 1 ? "Yesterday" : $"{DaysInSystem} days ago";
}

// ── USER CLAIM DETAIL ────────────────────────────────────────────────────────
public class UserClaimDetailViewModel
{
    public string ClaimId { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string DiagnosisCode { get; set; } = string.Empty;
    public decimal ClaimAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime SubmittedAt { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;
    public DateTime? ProcessedAt { get; set; }
    public List<StatusHistoryViewModel> StatusHistory { get; set; } = new();

    public string StatusMessage => Status switch
    {
        "Pending" => "Your claim is being processed. This usually takes 5-10 business days.",
        "Approved" => "Congratulations! Your claim has been approved.",
        "Rejected" => "Unfortunately, your claim was not approved. Please contact support for details.",
        _ => "Claim status unknown."
    };

    public string StatusColor => Status switch
    {
        "Approved" => "#057a55",
        "Rejected" => "#c81e1e",
        "Pending" => "#d97706",
        _ => "#6b7280"
    };
}

// ── STATUS HISTORY ───────────────────────────────────────────────────────────
public class StatusHistoryViewModel
{
    public string Status { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? Notes { get; set; }

    public string FormattedDate => ChangedAt.ToString("MMMM dd, yyyy HH:mm:ss");
}

// ── USER CLAIMS VIEW ─────────────────────────────────────────────────────────
public class UserClaimsViewModel
{
    public List<ClaimSummaryViewModel> Claims { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalCount { get; set; }
    public string? StatusFilter { get; set; }

    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
}

// ── USER PROFILE ─────────────────────────────────────────────────────────────
public class UserProfileViewModel
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public DateTime CreatedAt { get; set; }
    public int TotalClaimsSubmitted { get; set; }
    public int PendingClaimsCount { get; set; }
    public int ApprovedClaimsCount { get; set; }
    public int RejectedClaimsCount { get; set; }
    public decimal TotalClaimsAmount { get; set; }

    public decimal ApprovalRate => TotalClaimsSubmitted > 0 ?
        (decimal)ApprovedClaimsCount / TotalClaimsSubmitted * 100 : 0;
}

// ── ADMIN USER REGISTRATION ──────────────────────────────────────────────────
public class AdminUserRegisterViewModel
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(100, MinimumLength = 3,
        ErrorMessage = "Username must be 3-100 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$",
        ErrorMessage = "Username can only contain letters, numbers, underscores, dots, and hyphens.")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 8,
        ErrorMessage = "Password must be at least 8 characters.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])",
        ErrorMessage = "Password must contain uppercase, lowercase, number, and special character.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "Role")]
    public string Role { get; set; } = "User";

    public string? ErrorMessage { get; set; }

    public static readonly List<string> AvailableRoles = new()
    {
        "User",
        "Admin"
    };
}

// ── USER VIEW MODEL ──────────────────────────────────────────────────────────
public class UserViewModel
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public string StatusBadge => IsActive ? "Active" : "Inactive";
    public string StatusClass => IsActive ? "status-active" : "status-inactive";
}
