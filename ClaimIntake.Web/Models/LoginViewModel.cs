// ============================================================
// FILE: ClaimIntake.Web/Models/LoginViewModel.cs
// PURPOSE: Model for login form
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace ClaimIntake.Web.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Username is required")]
        [StringLength(100, MinimumLength = 3,
            ErrorMessage = "Username must be between 3 and 100 characters")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        [StringLength(255, MinimumLength = 6,
            ErrorMessage = "Password must be between 6 and 255 characters")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me for 30 days")]
        public bool RememberMe { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
